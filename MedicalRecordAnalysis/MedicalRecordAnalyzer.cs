using System.Text.Json;
using MedicalRecordAnalysis.Caching;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis;

public class MedicalRecordAnalyzer
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };

    private readonly ILLMProvider m_llmProvider;
    private readonly IOCRProvider m_ocrProvider;

    public MedicalRecordAnalyzer(ILLMProvider? llmProvider = null, IOCRProvider? ocrProvider = null)
    {
        m_llmProvider = llmProvider ?? new AnthropicProvider();
        // Embedded text is read directly. Add an OCR provider after it to also handle scans and images.
        m_ocrProvider = ocrProvider ?? new ChainedOCRProvider([new DirectTextProvider()]);
    }

    /**
     * Extracts and summarizes each record, finds contradictions, and writes the grounded case summary. With a
     * cache, records whose contents are unchanged reuse their earlier summaries, so a failed or repeated run only
     * pays for new work; contradictions and the case summary are always redone over the whole case. When no record
     * yields a quote (for example, every file is a scan), the case has no summary and only reports what was skipped.
     */
    public async Task<PICase> Ingest(IReadOnlyList<FileInfo> files, IDocumentCache? cache = null,
        IProgress<PipelineProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        List<DocumentSummary> documentSummaries = [];
        List<SourceDocument> sources = [];
        List<SkippedFile> skippedFiles = [];

        // 1. Extract and summarize each record
        for (int i = 0; i < files.Count; i++)
        {
            progress?.Report(new PipelineProgress(PipelineStage.SummarizingDocuments, i, files.Count, files[i].Name));

            CachedDocument? document = await ReadDocument(files[i], cache, skippedFiles, cancellationToken);
            if (document is not null)
            {
                documentSummaries.Add(document.Summary);
                sources.Add(document.Source);
            }
        }

        progress?.Report(new PipelineProgress(PipelineStage.SummarizingDocuments, files.Count, files.Count));

        PICase piCase = new PICase(documentSummaries);
        piCase.Sources = sources.ToArray();
        piCase.SkippedFiles = skippedFiles.ToArray();

        // With no quotes there is nothing to check or summarize, but the case is still returned so the skipped files
        // and their reasons reach the user instead of being lost with a failed run.
        if (!piCase.DocumentSummaries.Any(summary => summary.Citations.Length > 0))
        {
            return piCase;
        }

        // 2. Find contradictions, so the case summary can present disputed facts as disputed. Quotes are flagged
        // first so the provider can tell OCR text, where odd characters may be misreads, from embedded text.
        RecognitionLocator.AssignFlags(piCase.DocumentSummaries.SelectMany(summary => summary.Citations), piCase.Sources);
        progress?.Report(new PipelineProgress(PipelineStage.FindingContradictions, 0, 1));
        VerificationResult verification = await m_llmProvider.Verify(piCase, cancellationToken);
        piCase.Contradictions = verification.Contradictions.Select(WithDistinctCitations).ToArray();

        // 3. Summarize the case, grounded in the extracted quotes
        progress?.Report(new PipelineProgress(PipelineStage.SummarizingCase, 0, 1));
        piCase.Summary = await m_llmProvider.SummarizeCase(piCase, cancellationToken);
        Citation[] citations = piCase.DocumentSummaries.SelectMany(summary => summary.Citations)
            .Concat(piCase.Contradictions.SelectMany(contradiction => contradiction.Citations))
            .Concat(piCase.Summary.Citations)
            .ToArray();
        PageLocator.AssignPages(citations, piCase.Sources);
        RecognitionLocator.AssignFlags(citations, piCase.Sources);

        return piCase;
    }

    /**
     * Drafts the demand letter: the provider writes the body from the source passages most relevant to each part
     * of the letter, inside the standard letter template.
     */
    public async Task<DemandDraft> Draft(PICase piCase, IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new PipelineProgress(PipelineStage.Drafting, 0, 1));
        RetrievedPassage[] passages = PassageRetriever.Select(piCase);
        DemandDraft body = VerbatimCitations.Apply(await m_llmProvider.DraftDemand(piCase, passages, cancellationToken), piCase.Sources);
        Citation[] citations = body.Segments.SelectMany(segment => segment.Citations).ToArray();
        PageLocator.AssignPages(citations, piCase.Sources);
        RecognitionLocator.AssignFlags(citations, piCase.Sources);
        return DemandLetterTemplate.Wrap(body);
    }

    /**
     * Ingests the records in a directory and writes the case to a JSON file (the CLI's ingest command).
     */
    public async Task Ingest(DirectoryInfo directory, FileInfo output, IDocumentCache? cache = null,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"DIR: {directory.FullName}");

        PICase piCase = await Ingest(GetRecordFiles(directory), cache, cancellationToken: cancellationToken);
        foreach (SkippedFile skipped in piCase.SkippedFiles)
        {
            Console.Error.WriteLine($"SKIPPED {Path.GetFileName(skipped.FilePath)}: {skipped.Reason}");
        }

        await SaveJson(piCase, output, cancellationToken);
    }

    /**
     * Reads a case JSON file and writes the rendered draft (the CLI's draft command).
     */
    public async Task Draft(FileInfo file, FileInfo output, CancellationToken cancellationToken = default)
    {
        PICase piCase = await LoadCase(file, cancellationToken);
        DemandDraft draft = await Draft(piCase, cancellationToken: cancellationToken);

        byte[] letter = DemandLetterRenderer.RenderBytes(draft, piCase, DemandLetterRenderer.FormatFor(output));
        await File.WriteAllBytesAsync(output.FullName, letter, cancellationToken);
    }

    /**
     * The records in a directory, by name, skipping hidden files such as .DS_Store.
     */
    public static FileInfo[] GetRecordFiles(DirectoryInfo directory)
    {
        return directory.GetFiles()
            .Where(file => !file.Attributes.HasFlag(FileAttributes.Hidden))
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task SaveJson<T>(T value, FileInfo output, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = output.Create();
        await JsonSerializer.SerializeAsync(stream, value, JsonSerializerOptions, cancellationToken);
    }

    public static Task<PICase> LoadCase(FileInfo file, CancellationToken cancellationToken = default)
    {
        return LoadJson<PICase>(file, "case", cancellationToken);
    }

    public static Task<DemandDraft> LoadDraft(FileInfo file, CancellationToken cancellationToken = default)
    {
        return LoadJson<DemandDraft>(file, "draft", cancellationToken);
    }

    private static async Task<T> LoadJson<T>(FileInfo file, string description, CancellationToken cancellationToken)
    {
        await using FileStream input = file.OpenRead();
        return await JsonSerializer.DeserializeAsync<T>(input, JsonSerializerOptions, cancellationToken)
            ?? throw new InvalidDataException($"'{file.Name}' does not contain a {description}.");
    }

    /**
     * The contradiction with each quoted span cited once, so a quote cited twice can't look like two statements.
     */
    private static Contradiction WithDistinctCitations(Contradiction contradiction)
    {
        return new Contradiction()
        {
            Summary = contradiction.Summary,
            Citations = contradiction.Citations.DistinctBy(citation => (citation.FilePath, citation.StartPosition, citation.CitationLength)).ToArray(),
        };
    }

    /**
     * Returns the record's summary and source text, from the cache when its contents are unchanged. Records that
     * cannot be read are added to skippedFiles and return null.
     */
    private async Task<CachedDocument?> ReadDocument(FileInfo file, IDocumentCache? cache, List<SkippedFile> skippedFiles,
        CancellationToken cancellationToken)
    {
        string? contentHash = null;
        if (cache is not null)
        {
            contentHash = await CachedDocument.HashFile(file, cancellationToken);
            if (await cache.Get(contentHash, cancellationToken) is CachedDocument cached)
            {
                return cached.WithFilePath(file.FullName);
            }
        }

        OCRResult result;
        try
        {
            result = await m_ocrProvider.DetectTextFromFile(file, cancellationToken);
        }
        catch (NotSupportedException exception)
        {
            skippedFiles.Add(new SkippedFile() { FilePath = file.FullName, Reason = exception.Message });
            return null;
        }

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            skippedFiles.Add(new SkippedFile() { FilePath = file.FullName, Reason = "No text was found." });
            return null;
        }

        DocumentSummary summary = await m_llmProvider.SummarizeDocument(result.File, result.Text, cancellationToken);
        SourceDocument source = new SourceDocument()
        {
            FilePath = result.File.FullName,
            Passages = PassageSplitter.Split(result.Text),
            PageStartPositions = result.PageStartPositions,
            Recognition = result.Recognition,
        };

        CachedDocument document = new CachedDocument() { ContentHash = contentHash ?? "", Summary = summary, Source = source };
        if (cache is not null)
        {
            await cache.Set(document, cancellationToken);
        }

        return document;
    }
}
