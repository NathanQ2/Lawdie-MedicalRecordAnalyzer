using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM.Providers;

public class AnthropicProvider : ILLMProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Responses are preceded by thinking, which counts against this limit; a full demand letter for a large case
    // needs the room. Limits this high require streaming (see CreateMessage).
    private const int MaxTokens = 32000;
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
        "You are a drafting assistant for a personal injury law firm. Write the body of a demand letter " +
        "to an insurance adjuster; the letterhead, salutation, opening paragraph, demand, and signature " +
        "are added separately, so do not write them. Use exactly these section headings, each on its own " +
        "line starting with \"## \", with a blank line before and after every heading and paragraph: " +
        "\"## Facts of the Incident\", \"## Injuries and Medical Treatment\", \"## Medical Expenses\", " +
        "\"## Lost Wages and Other Economic Losses\", \"## Pain, Suffering, and Impact on Daily Life\", and " +
        "\"## Future Medical Care\". Describe treatment chronologically by provider. Under Medical Expenses, " +
        "list each provider's charges on its own line starting with \"- \", with dates of service and " +
        "amounts, followed by the total if the documents state one. Make the client's pain and suffering " +
        "the center of the letter, as documented in the records. Each provided document is verbatim text " +
        "from one of the client's medical records. Write in your own words in a professional, persuasive " +
        "tone rather than pasting record text, but state only facts supported by the documents and cite " +
        "them for every fact; never invent, infer, or exaggerate injuries, dates, symptoms, amounts, or " +
        "causation. Where the records disagree, attribute each version to its record with its citation " +
        "rather than choosing one. Where a section needs information the documents do not contain, write " +
        "a bracketed placeholder in capitals, such as [LOST WAGES AMOUNT], and never guess. The letter goes " +
        "to the adjuster as written, so do not include notes to the attorney or JSON.";

    private const string VerificationSystemPrompt =
        "You are a legal intake quality reviewer. Compare the provided document summaries and " +
        "supporting quotes, and report any contradictions, including conflicting dates, " +
        "diagnoses, providers, or causal statements. A single document can contradict itself. " +
        "Each document is labeled with its record's file name; refer to records by that name in " +
        "your descriptions. Each supporting quote is labeled with an ID such as [Q3]; reference the quotes that " +
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

    public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default)
    {
        // The record is sent as a citable document of small chunks (lines, split into sentences), so each
        // citation maps to an exact, tight span of the text we sent.
        ChunkedText record = new ChunkedText(file.FullName, text, 0);
        if (record.Chunks.Count == 0)
        {
            throw new InvalidOperationException($"'{file.Name}' has no text to summarize.");
        }

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
                        record.ToDocument(file.Name, $"File type: {file.Extension}"),
                        new TextBlockParam() { Text = DocumentSummarizationInstruction },
                    },
                },
            ],
            Model = m_model,
        };

        Message message = await CreateMessage(parameters, cancellationToken);

        StringBuilder summary = new StringBuilder();
        List<Citation> citations = [];
        foreach (ContentBlock content in message.Content)
        {
            if (!content.TryPickText(out TextBlock? block))
            {
                continue;
            }

            summary.Append(block.Text);

            foreach (TextCitation textCitation in block.Citations ?? [])
            {
                if (textCitation.TryPickCitationContentBlockLocation(out CitationContentBlockLocation? location))
                {
                    Citation? citation = record.Resolve((int)location.StartBlockIndex, (int)location.EndBlockIndex, location.CitedText, block.Text.Trim());
                    if (citation is not null)
                    {
                        citations.Add(citation);
                    }
                }
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

    public async Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default)
    {
        (List<DocumentBlockParam> documents, CitationResolver resolve) = BuildQuoteDocuments(piCase);
        List<DraftSegment> segments = await GenerateGrounded(CaseSummarizationSystemPrompt,
            BuildGroundedInstruction(piCase, "Write the case summary, citing the quotes for every fact."), documents, resolve, cancellationToken);

        return new CaseSummary()
        {
            Summary = string.Concat(segments.Select(segment => segment.Text)).Trim(),
            Citations = segments.SelectMany(segment => segment.Citations).ToArray(),
            UnsupportedStatements = FindUnsupportedSentences(segments),
        };
    }

    public async Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default)
    {
        // Older exports have no stored passages; fall back to the quotes extracted during ingest.
        (List<DocumentBlockParam> documents, CitationResolver resolve) = passages.Count > 0
            ? BuildPassageDocuments(passages)
            : BuildQuoteDocuments(piCase);
        List<DraftSegment> segments = await GenerateGrounded(DemandDraftSystemPrompt,
            BuildGroundedInstruction(piCase, "Write the body of the demand letter, citing the records for every fact."), documents, resolve, cancellationToken);

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
     * Each passage becomes a citable document of small chunks, so citations map to exact spans that are
     * offset back into the source file's extracted text.
     */
    private static (List<DocumentBlockParam> Documents, CitationResolver Resolve) BuildPassageDocuments(IReadOnlyList<RetrievedPassage> passages)
    {
        List<ChunkedText> excerpts = passages
            .Select(passage => new ChunkedText(passage.FilePath, passage.Text, passage.StartPosition))
            .Where(excerpt => excerpt.Chunks.Count > 0)
            .ToList();

        List<DocumentBlockParam> documents = excerpts.Select(excerpt => excerpt.ToDocument(Path.GetFileName(excerpt.FilePath),
            $"Excerpt from {Path.GetFileName(excerpt.FilePath)} starting at character {excerpt.BaseOffset}.")).ToList();

        return (documents, (citation, claim) =>
        {
            if (!citation.TryPickCitationContentBlockLocation(out CitationContentBlockLocation? location) ||
                location.DocumentIndex < 0 || location.DocumentIndex >= excerpts.Count)
            {
                return [];
            }

            Citation? resolved = excerpts[(int)location.DocumentIndex].Resolve((int)location.StartBlockIndex, (int)location.EndBlockIndex, location.CitedText, claim);
            return resolved is null ? [] : [resolved];
        });
    }

    /**
     * Generates text grounded in the given citable documents, mapping each response citation back to source
     * citations with the resolver.
     */
    private async Task<List<DraftSegment>> GenerateGrounded(string system, string instruction,
        List<DocumentBlockParam> documents, CitationResolver resolve, CancellationToken cancellationToken)
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

        Message message = await CreateMessage(parameters, cancellationToken);

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

    /**
     * The task, preceded by the contradictions found in review. Only contradictions backed by quotes are included:
     * an unverified one is uncited model text and must not steer grounded writing.
     */
    internal static string BuildGroundedInstruction(PICase piCase, string task)
    {
        StringBuilder builder = new StringBuilder();

        Contradiction[] verified = piCase.Contradictions.Where(contradiction => contradiction.IsVerified).ToArray();
        if (verified.Length > 0)
        {
            builder.AppendLine("A separate review found these contradictions between or within the records:");
            foreach (Contradiction contradiction in verified)
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

    public async Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default)
    {
        string user = BuildCaseContext(piCase);
        string response = await Complete(VerificationSystemPrompt, user, cancellationToken);
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
     * Sends the request as a stream (long responses would otherwise hit HTTP timeouts), collects it into the complete
     * message, and rejects responses that stopped early (cut off by the token limit, or declined partway by a safety
     * classifier), so incomplete text is never used.
     */
    private async Task<Message> CreateMessage(MessageCreateParams parameters, CancellationToken cancellationToken)
    {
        Message message = await m_client.Messages.CreateStreaming(parameters, cancellationToken).Aggregate();
        CheckComplete(message.StopReason);
        return message;
    }

    internal static void CheckComplete(ApiEnum<string, StopReason>? stopReason)
    {
        if (stopReason == StopReason.MaxTokens)
        {
            throw new InvalidOperationException("The response reached the token limit and was cut off.");
        }

        if (stopReason == StopReason.Refusal)
        {
            throw new InvalidOperationException("The response was declined before it finished, so it was not used.");
        }
    }

    private async Task<string> Complete(string system, string user, CancellationToken cancellationToken)
    {
        MessageCreateParams parameters = new MessageCreateParams()
        {
            MaxTokens = MaxTokens,
            System = system,
            Messages = [new MessageParam() { Role = Role.User, Content = user }],
            Model = m_model,
        };

        Message message = await CreateMessage(parameters, cancellationToken);

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

            builder.Append("DOCUMENT ").Append(i + 1);
            string? filePath = i < piCase.Sources.Length ? piCase.Sources[i].FilePath : summary.Citations.FirstOrDefault()?.FilePath;
            if (filePath is not null)
            {
                builder.Append(": ").Append(Path.GetFileName(filePath));
            }

            builder.AppendLine();
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

    /**
     * Text split into citable chunks with TextChunker. BaseOffset is where the text starts within its source
     * file's extracted text, so resolved citations point into the whole file.
     */
    internal sealed class ChunkedText
    {
        public ChunkedText(string filePath, string text, int baseOffset)
        {
            FilePath = filePath;
            Text = text;
            BaseOffset = baseOffset;
            Chunks = TextChunker.Chunk(text);
        }

        public string FilePath { get; }
        public string Text { get; }
        public int BaseOffset { get; }
        public List<(int Start, int End)> Chunks { get; }

        public DocumentBlockParam ToDocument(string title, string context)
        {
            return new DocumentBlockParam()
            {
                Source = new ContentBlockSource()
                {
                    Content = Chunks.Select(chunk => (MessageContentBlockSourceContent)new TextBlockParam() { Text = Text[chunk.Start..chunk.End] }).ToList(),
                },
                Title = title,
                Context = context,
                Citations = new CitationsConfigParam() { Enabled = true },
            };
        }

        /**
         * Maps an end-exclusive chunk range to the exact source span from its first chunk to its last. The
         * API's cited text must match the chunks we sent (ignoring whitespace), so a citation that does not
         * line up with the source is dropped rather than misplaced.
         */
        public Citation? Resolve(int startBlockIndex, int endBlockIndex, string citedText, string claim)
        {
            int start = startBlockIndex;
            int end = Math.Max(endBlockIndex, start + 1);
            if (start < 0 || end > Chunks.Count)
            {
                return null;
            }

            string expected = string.Concat(Chunks.Skip(start).Take(end - start).Select(chunk => Text[chunk.Start..chunk.End]));
            if (WithoutWhitespace(expected) != WithoutWhitespace(citedText))
            {
                return null;
            }

            int spanStart = Chunks[start].Start;
            int spanEnd = Chunks[end - 1].End;
            return new Citation()
            {
                FilePath = FilePath,
                CitedText = Text[spanStart..spanEnd],
                StartPosition = BaseOffset + spanStart,
                Claim = claim,
            };
        }

        private static string WithoutWhitespace(string text)
        {
            return string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        }
    }
}
