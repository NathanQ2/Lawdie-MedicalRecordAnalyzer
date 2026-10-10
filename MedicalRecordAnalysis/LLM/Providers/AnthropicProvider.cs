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
    private const string DefaultModel = "claude-sonnet-4-5";

    private const string DocumentSummarizationSystemPrompt =
        "You are a legal intake assistant for a personal injury law firm. Extract from the " +
        "medical record only facts that are explicitly supported by the text, including " +
        "injuries, diagnoses, treatment dates, providers, and causal statements. Never infer, " +
        "assume, or invent information. Every quote must be an exact, verbatim substring of " +
        "the provided text. Respond with JSON only, using this exact shape: " +
        "{\"summary\":\"<case-relevant summary>\",\"quotes\":[\"<exact quote>\"]}";

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
        "Every quote must be an exact, verbatim substring of the provided text. Respond with " +
        "JSON only, using this exact shape: " +
        "{\"contradictions\":[{\"summary\":\"<description>\",\"quotes\":[\"<exact quote>\"]}]}. " +
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
        string user = $"Source file: {file.Name}\nFile type: {file.Extension}\n\n\n{text}";
        string response = await Complete(DocumentSummarizationSystemPrompt, user);

        DocumentSummaryDto? dto = Deserialize<DocumentSummaryDto>(response);
        if (dto is null || dto.Summary is null)
        {
            throw new InvalidOperationException($"The model returned an invalid document summary for '{file.Name}'.");
        }

        List<Citation> citations = [];
        if (dto.Quotes is not null)
        {
            foreach (string quote in dto.Quotes)
            {
                if (string.IsNullOrWhiteSpace(quote))
                {
                    continue;
                }

                int index = text.IndexOf(quote, StringComparison.Ordinal);
                if (index >= 0)
                {
                    citations.Add(new Citation() { File = file, CitedText = quote, StartPosition = index });
                }
            }
        }

        return new DocumentSummary()
        {
            Summary = dto.Summary,
            Citations = citations.ToArray(),
        };
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

                List<Citation> citations = [];
                if (contradiction.Quotes is not null)
                {
                    foreach (string quote in contradiction.Quotes)
                    {
                        if (string.IsNullOrWhiteSpace(quote))
                        {
                            continue;
                        }

                        citations.AddRange(FindMatchingCitations(piCase, quote));
                    }
                }

                contradictions.Add(new Contradiction()
                {
                    Summary = contradiction.Summary,
                    Citations = citations.ToArray(),
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

    private static string BuildCaseContext(PICase piCase)
    {
        StringBuilder builder = new StringBuilder();

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
                    builder.Append("- ").AppendLine(citation.CitedText);
                }
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static List<Citation> FindMatchingCitations(PICase piCase, string quote)
    {
        List<Citation> matches = [];

        foreach (DocumentSummary summary in piCase.DocumentSummaries)
        {
            foreach (Citation citation in summary.Citations)
            {
                if (citation.CitedText == quote)
                {
                    matches.Add(citation);
                }
            }
        }

        return matches;
    }

    private sealed class DocumentSummaryDto
    {
        public string? Summary { get; set; }
        public List<string>? Quotes { get; set; }
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
        public List<string>? Quotes { get; set; }
    }
}