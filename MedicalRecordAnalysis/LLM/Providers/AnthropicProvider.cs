using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM.Providers;

public class AnthropicProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Responses are preceded by thinking, which counts against this limit; large cases need the room.
    private const int MaxTokens = 16000;
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
        "You are a legal intake assistant for a personal injury law firm. Write a case summary " +
        "covering the incident, injuries, treatment timeline, providers, diagnoses, and any causal " +
        "relationship to the incident. Each provided document contains verbatim quotes from one of " +
        "the client's medical records. State only facts supported by these quotes and cite them for " +
        "every fact; never add facts that are not present. Where the records disagree, present each " +
        "record's version with its citation and do not choose between them. Respond with plain " +
        "prose only: no preamble, headings, or JSON.";

    private const string DemandDraftSystemPrompt =
        "You are a drafting assistant for a personal injury law firm. Write the narrative section " +
        "of a demand letter to an insurance adjuster. Make the client's pain and suffering the center " +
        "of the narrative: the incident, the injuries and diagnoses, the course of treatment, and " +
        "above all how the injuries have affected the client's daily life, as documented in the " +
        "records. Each provided document is verbatim text from one of the client's medical records. " +
        "Write in your own words as a persuasive, readable narrative rather than pasting record text, " +
        "but state only facts supported by the documents and cite them for every fact; never invent, " +
        "infer, or exaggerate injuries, dates, symptoms, or causation. Where the records disagree, " +
        "attribute each version to its record with its citation rather than choosing one. For " +
        "information a demand letter needs that the records do not contain, such as the claim number, " +
        "the insured's name, lost wages, or the amount demanded, write a bracketed placeholder in " +
        "capitals, such as [CLAIM NUMBER], and never guess. The narrative goes to the adjuster as " +
        "written, so do not include notes to the attorney. Write the narrative section only: no " +
        "letterhead, salutation, signature, headings, or JSON.";

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

        Message message = await CreateMessage(parameters);

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
        (List<DocumentBlockParam> documents, CitationResolver resolve) = BuildQuoteDocuments(piCase);
        List<DraftSegment> segments = await GenerateGrounded(CaseSummarizationSystemPrompt,
            BuildGroundedInstruction(piCase, "Write the case summary, citing the quotes for every fact."), documents, resolve);

        return new CaseSummary()
        {
            Summary = string.Concat(segments.Select(segment => segment.Text)).Trim(),
            Citations = segments.SelectMany(segment => segment.Citations).ToArray(),
            UnsupportedStatements = FindUnsupportedSentences(segments),
        };
    }

    public async Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages)
    {
        // Older exports have no stored passages; fall back to the quotes extracted during ingest.
        (List<DocumentBlockParam> documents, CitationResolver resolve) = passages.Count > 0
            ? BuildPassageDocuments(passages)
            : BuildQuoteDocuments(piCase);
        List<DraftSegment> segments = await GenerateGrounded(DemandDraftSystemPrompt,
            BuildGroundedInstruction(piCase, "Write the demand letter narrative section, citing the records for every fact."), documents, resolve);

        return new DemandDraft()
        {
            Segments = segments.ToArray(),
            UnsupportedStatements = FindUnsupportedSentences(segments),
        };
    }

    /**
     * Maps a citation in the response back to source citations, given the summary text it supports as the claim.
     */
    private delegate Citation[] CitationResolver(TextCitation citation, string claim);

    /**
     * Each record's extracted quotes become a citable custom-content document with one block per quote, so
     * content_block_location citations map straight back to those quotes.
     */
    private static (List<DocumentBlockParam> Documents, CitationResolver Resolve) BuildQuoteDocuments(PICase piCase)
    {
        List<IReadOnlyList<Citation>> documentQuotes = [];
        List<DocumentBlockParam> documents = [];
        foreach (IGrouping<string, Citation> record in NumberQuotes(piCase).GroupBy(citation => citation.FilePath))
        {
            List<Citation> quotes = record.ToList();
            documentQuotes.Add(quotes);
            documents.Add(new DocumentBlockParam()
            {
                Source = new ContentBlockSource()
                {
                    Content = quotes.Select(quote => (MessageContentBlockSourceContent)new TextBlockParam() { Text = quote.CitedText }).ToList(),
                },
                Title = Path.GetFileName(record.Key),
                Citations = new CitationsConfigParam() { Enabled = true },
            });
        }

        return (documents, (citation, claim) => citation.TryPickCitationContentBlockLocation(out CitationContentBlockLocation? location)
            ? ResolveContentBlockCitation(documentQuotes, (int)location.DocumentIndex, (int)location.StartBlockIndex, (int)location.EndBlockIndex, claim)
            : []);
    }

    /**
     * Each passage becomes a citable plain-text document, so the API cites individual sentences with
     * char_location citations that are offset back into the source file's extracted text.
     */
    private static (List<DocumentBlockParam> Documents, CitationResolver Resolve) BuildPassageDocuments(IReadOnlyList<RetrievedPassage> passages)
    {
        List<DocumentBlockParam> documents = passages.Select(passage => new DocumentBlockParam()
        {
            Source = new PlainTextSource() { Data = passage.Text },
            Title = Path.GetFileName(passage.FilePath),
            Context = $"Excerpt from {Path.GetFileName(passage.FilePath)} starting at character {passage.StartPosition}.",
            Citations = new CitationsConfigParam() { Enabled = true },
        }).ToList();

        return (documents, (citation, claim) =>
        {
            if (!citation.TryPickCitationCharLocation(out CitationCharLocation? location))
            {
                return [];
            }

            Citation? resolved = ResolvePassageCitation(passages, (int)location.DocumentIndex, location.CitedText, (int)location.StartCharIndex, claim);
            return resolved is null ? [] : [resolved];
        });
    }

    /**
     * Maps a char_location citation within a passage document to the source file, verifying the cited text
     * really occurs in the passage. Returns null for out-of-range documents or text not found in the passage.
     */
    internal static Citation? ResolvePassageCitation(IReadOnlyList<RetrievedPassage> passages, int documentIndex,
        string citedText, int startCharIndex, string claim)
    {
        if (documentIndex < 0 || documentIndex >= passages.Count)
        {
            return null;
        }

        RetrievedPassage passage = passages[documentIndex];
        int? start = LocateCitedText(passage.Text, citedText, startCharIndex);
        if (start is null)
        {
            return null;
        }

        return new Citation()
        {
            FilePath = passage.FilePath,
            CitedText = citedText,
            StartPosition = passage.StartPosition + start.Value,
            Claim = claim,
        };
    }

    /**
     * Generates text grounded in the given citable documents, mapping each response citation back to source
     * citations with the resolver.
     */
    private async Task<List<DraftSegment>> GenerateGrounded(string system, string instruction,
        List<DocumentBlockParam> documents, CitationResolver resolve)
    {
        if (documents.Count == 0)
        {
            throw new InvalidOperationException("No source text was extracted from the records, so grounded text cannot be written.");
        }

        List<ContentBlockParam> content = documents.Select(document => (ContentBlockParam)document).ToList();
        content.Add(new TextBlockParam() { Text = instruction });

        MessageCreateParams parameters = new MessageCreateParams()
        {
            MaxTokens = MaxTokens,
            System = system,
            Messages = [new MessageParam() { Role = Role.User, Content = content }],
            Model = m_model,
        };

        Message message = await CreateMessage(parameters);

        List<DraftSegment> segments = [];
        foreach (ContentBlock block in message.Content)
        {
            if (!block.TryPickText(out TextBlock? text))
            {
                continue;
            }

            string claim = text.Text.Trim();
            Citation[] citations = (text.Citations ?? []).SelectMany(citation => resolve(citation, claim)).ToArray();
            segments.Add(new DraftSegment() { Text = text.Text, Citations = citations });
        }

        if (segments.All(segment => string.IsNullOrWhiteSpace(segment.Text)))
        {
            throw new InvalidOperationException("The model returned an empty response.");
        }

        return segments;
    }

    private static string[] FindUnsupportedSentences(IEnumerable<DraftSegment> segments)
    {
        return GroundingCheck.FindUnsupportedSentences(
            segments.Select(segment => new GeneratedSegment(segment.Text, segment.Citations.Length > 0)).ToList());
    }

    private static string BuildGroundedInstruction(PICase piCase, string task)
    {
        StringBuilder builder = new StringBuilder();

        if (piCase.Contradictions.Length > 0)
        {
            builder.AppendLine("A separate review found these contradictions between or within the records:");
            foreach (Contradiction contradiction in piCase.Contradictions)
            {
                builder.Append("- ").AppendLine(contradiction.Summary);
            }
        }
        else
        {
            builder.AppendLine("A separate review found no contradictions between the records.");
        }

        builder.AppendLine();
        builder.Append(task);
        return builder.ToString();
    }

    /**
     * Maps a content_block_location citation (document index plus an end-exclusive block range) back to the
     * quotes those blocks were built from, with the summary text it supports as the Claim. Out-of-range
     * locations resolve to nothing.
     */
    internal static Citation[] ResolveContentBlockCitation(IReadOnlyList<IReadOnlyList<Citation>> documentQuotes,
        int documentIndex, int startBlockIndex, int endBlockIndex, string claim)
    {
        if (documentIndex < 0 || documentIndex >= documentQuotes.Count)
        {
            return [];
        }

        IReadOnlyList<Citation> quotes = documentQuotes[documentIndex];
        int start = Math.Max(startBlockIndex, 0);
        int end = Math.Min(Math.Max(endBlockIndex, start + 1), quotes.Count);

        List<Citation> resolved = [];
        for (int i = start; i < end; i++)
        {
            resolved.Add(new Citation()
            {
                FilePath = quotes[i].FilePath,
                CitedText = quotes[i].CitedText,
                StartPosition = quotes[i].StartPosition,
                Claim = claim,
            });
        }

        return resolved.ToArray();
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

    /**
     * Sends the request and rejects responses cut off by the token limit, so truncated text is never used.
     */
    private async Task<Message> CreateMessage(MessageCreateParams parameters)
    {
        Message message = await m_client.Messages.Create(parameters);
        if (message.StopReason == StopReason.MaxTokens)
        {
            throw new InvalidOperationException("The response reached the token limit and was cut off.");
        }

        return message;
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

        Message message = await CreateMessage(parameters);

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