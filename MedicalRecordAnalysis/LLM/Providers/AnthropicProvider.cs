using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;

namespace MedicalRecordAnalysis.LLM.Providers;

public class AnthropicProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int MaxTokens = 4096;
    private const string DefaultModel = "claude-haiku-5-5";

    private const string DocumentSummarizationSystemPrompt =
        "You are a legal intake assistant for a personal injury law firm. Extract from the " +
        "provided medical record only facts that are explicitly supported by the document, " +
        "including injuries, diagnoses, treatment dates, providers, and causal statements. " +
        "Never infer, assume, or invent information. Cite the document for every fact you " +
        "state. Respond with a plain-prose summary only: no preamble, headings, or JSON.";

    private const string DocumentSummarizationInstruction =
        "Summarize the case-relevant facts in this medical record, citing the record for each fact.";

    private const string CaseSummarizationSystemPrompt =
        "You are a legal intake assistant for a personal injury law firm. Summarize the case " +
        "from the provided document summaries and supporting quotes, covering the injury, " +
        "treatment timeline, providers, diagnoses, and any causal relationship to the incident. " +
        "Use only the provided information; never add facts that are not present. Respond with " +
        "JSON only, using this exact shape: {\"summary\":\"<case summary>\"}";

    private const string VerificationSystemPrompt =
        "You are a legal intake quality reviewer. Compare the provided document summaries and " +
        "supporting quotes, and report any contradictions, including conflicting dates, " +
        "diagnoses, providers, or causal statements. A single document can contradict itself. " +
        "Each supporting quote is labeled with an ID such as [Q3]; reference the quotes that " +
        "show each contradiction by ID only, never by restating their text. Respond with JSON " +
        "only, using this exact shape: " +
        "{\"contradictions\":[{\"summary\":\"<description>\",\"quoteIds\":[\"Q1\",\"Q2\"]}]}. " +
        "If there are no contradictions, return {\"contradictions\":[]}.";

    private readonly AnthropicClient m_client;
    private readonly string m_model;

    public AnthropicProvider(AnthropicClient? client = null, string? model = null)
    {
        m_client = client ?? new AnthropicClient();
        m_model = model ?? DefaultModel;
    }

    public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text)
    {
        // The record is sent as a plain-text document with citations enabled, so the API returns
        // char_location citations that point into the exact text we sent.
        DocumentBlockParam document = new DocumentBlockParam()
        {
            Source = new PlainTextSource() { Data = text },
            Title = file.Name,
            Context = $"File type: {file.Extension}",
            Citations = new CitationsConfigParam() { Enabled = true },
        };

        MessageCreateParams parameters = new MessageCreateParams()
        {
            MaxTokens = MaxTokens,
            System = DocumentSummarizationSystemPrompt,
            Messages =
            [
                new MessageParam()
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>()
                    {
                        document,
                        new TextBlockParam() { Text = DocumentSummarizationInstruction },
                    },
                },
            ],
            Model = m_model,
        };

        Message message = await m_client.Messages.Create(parameters);

        StringBuilder summary = new StringBuilder();
        List<Citation> citations = [];
        foreach (ContentBlock content in message.Content)
        {
            if (!content.TryPickText(out TextBlock? block))
            {
                continue;
            }

            summary.Append(block.Text);

            if (block.Citations is null)
            {
                continue;
            }

            foreach (TextCitation textCitation in block.Citations)
            {
                if (!textCitation.TryPickCitationCharLocation(out CitationCharLocation? location))
                {
                    continue;
                }

                int? start = LocateCitedText(text, location.CitedText, (int)location.StartCharIndex);
                if (start is null)
                {
                    continue;
                }

                citations.Add(new Citation()
                {
                    FilePath = file.FullName,
                    CitedText = location.CitedText,
                    StartPosition = start.Value,
                    Claim = block.Text.Trim(),
                });
            }
        }

        if (summary.Length == 0)
        {
            throw new InvalidOperationException($"The model returned an empty document summary for '{file.Name}'.");
        }

        return new DocumentSummary()
        {
            Summary = summary.ToString().Trim(),
            Citations = citations.ToArray(),
        };
    }

    /**
     * Confirms the cited text really occurs in the source. The API's character index is trusted when it
     * lines up; otherwise (e.g. index units differ for non-BMP characters) the text is searched for.
     * Returns null if the cited text cannot be found, so the citation is dropped rather than misplaced.
     */
    internal static int? LocateCitedText(string text, string citedText, int startIndex)
    {
        if (string.IsNullOrWhiteSpace(citedText))
        {
            return null;
        }

        if (startIndex >= 0 && startIndex + citedText.Length <= text.Length &&
            string.CompareOrdinal(text, startIndex, citedText, 0, citedText.Length) == 0)
        {
            return startIndex;
        }

        int index = text.IndexOf(citedText, StringComparison.Ordinal);
        return index >= 0 ? index : null;
    }

    public async Task<CaseSummary> SummarizeCase(PICase piCase)
    {
        string user = BuildCaseContext(piCase);
        string response = await Complete(CaseSummarizationSystemPrompt, user);

        CaseSummaryDto? dto = Deserialize<CaseSummaryDto>(response);
        if (dto is null || dto.Summary is null)
        {
            throw new InvalidOperationException("The model returned an invalid case summary.");
        }

        return new CaseSummary()
        {
            Summary = dto.Summary,
        };
    }

    public async Task<VerificationResult> Verify(PICase piCase)
    {
        string user = BuildCaseContext(piCase);
        string response = await Complete(VerificationSystemPrompt, user);
        List<Citation> quotes = NumberQuotes(piCase);

        VerificationResultDto? dto = Deserialize<VerificationResultDto>(response);

        List<Contradiction> contradictions = [];
        if (dto?.Contradictions is not null)
        {
            foreach (ContradictionDto contradiction in dto.Contradictions)
            {
                if (contradiction.Summary is null)
                {
                    continue;
                }

                contradictions.Add(new Contradiction()
                {
                    Summary = contradiction.Summary,
                    Citations = ResolveQuoteIds(quotes, contradiction.QuoteIds ?? []),
                });
            }
        }

        return new VerificationResult()
        {
            Contradictions = contradictions.ToArray(),
        };
    }

    private async Task<string> Complete(string system, string user)
    {
        MessageCreateParams parameters = new MessageCreateParams()
        {
            MaxTokens = MaxTokens,
            System = system,
            Messages = [new MessageParam() { Role = Role.User, Content = user }],
            Model = m_model,
        };

        Message message = await m_client.Messages.Create(parameters);

        StringBuilder builder = new StringBuilder();
        foreach (ContentBlock content in message.Content)
        {
            if (content.TryPickText(out TextBlock? text))
            {
                builder.Append(text.Text);
            }
        }

        return builder.ToString();
    }

    private static T? Deserialize<T>(string response) where T : class
    {
        int start = response.IndexOf('{');
        int end = response.LastIndexOf('}');

        if (start < 0 || end < 0 || end < start)
        {
            throw new InvalidOperationException("The model response did not contain JSON.");
        }

        return JsonSerializer.Deserialize<T>(response.Substring(start, end - start + 1), JsonOptions);
    }

    /**
     * Renders the case for the model. Each quote gets an ID (Q1, Q2, ...) matching its position in
     * NumberQuotes, and its text is JSON-escaped so multi-line quotes stay on a single line.
     */
    internal static string BuildCaseContext(PICase piCase)
    {
        StringBuilder builder = new StringBuilder();
        int quoteNumber = 0;

        for (int i = 0; i < piCase.DocumentSummaries.Length; i++)
        {
            DocumentSummary summary = piCase.DocumentSummaries[i];

            builder.Append("DOCUMENT ").Append(i + 1).AppendLine();
            builder.AppendLine(summary.Summary);

            if (summary.Citations.Length > 0)
            {
                builder.AppendLine("Supporting quotes:");
                foreach (Citation citation in summary.Citations)
                {
                    quoteNumber++;
                    builder.Append("[Q").Append(quoteNumber).Append("] ").AppendLine(JsonSerializer.Serialize(citation.CitedText));
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /**
     * Every citation in the case, in the order BuildCaseContext numbers them: quote ID Qn is element n - 1.
     */
    internal static List<Citation> NumberQuotes(PICase piCase)
    {
        return piCase.DocumentSummaries.SelectMany(summary => summary.Citations).ToList();
    }

    /**
     * Maps quote IDs returned by the model (e.g. "Q3") back to citations. Unknown or malformed IDs are dropped.
     */
    internal static Citation[] ResolveQuoteIds(IReadOnlyList<Citation> quotes, IEnumerable<string> quoteIds)
    {
        List<Citation> citations = [];
        HashSet<int> seen = [];

        foreach (string quoteId in quoteIds)
        {
            string number = quoteId.Trim().TrimStart('[').TrimEnd(']').TrimStart('Q', 'q');
            if (int.TryParse(number, out int index) && index >= 1 && index <= quotes.Count && seen.Add(index))
            {
                citations.Add(quotes[index - 1]);
            }
        }

        return citations.ToArray();
    }

    private sealed class CaseSummaryDto
    {
        public string? Summary { get; set; }
    }

    private sealed class VerificationResultDto
    {
        public List<ContradictionDto>? Contradictions { get; set; }
    }

    private sealed class ContradictionDto
    {
        public string? Summary { get; set; }
        public List<string>? QuoteIds { get; set; }
    }
}