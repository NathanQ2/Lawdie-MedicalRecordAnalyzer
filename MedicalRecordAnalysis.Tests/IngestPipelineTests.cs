using MedicalRecordAnalysis.Caching;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public sealed class IngestPipelineTests : IDisposable
{
    private readonly TestFiles m_files = new TestFiles();

    public void Dispose() => m_files.Dispose();

    private DirectoryInfo RecordsDirectory(string name) => m_files.Directory.CreateSubdirectory(name);

    private static FileInfo Write(DirectoryInfo directory, string name, string text)
    {
        string path = Path.Combine(directory.FullName, name);
        File.WriteAllText(path, text);
        return new FileInfo(path);
    }

    [Fact]
    public async Task ReportsSkippedFilesWithReasons()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "note.txt", "Neck pain after collision.");
        Write(records, "scan.png", "not really an image");
        Write(records, "blank.txt", "   \n  ");

        PICase piCase = await new MedicalRecordAnalyzer(new DummyLLMProvider()).Ingest(MedicalRecordAnalyzer.GetRecordFiles(records));

        Assert.Single(piCase.DocumentSummaries);
        Assert.Equal(["blank.txt", "scan.png"], piCase.SkippedFiles.Select(skipped => Path.GetFileName(skipped.FilePath)));
        Assert.Equal("No text was found.", piCase.SkippedFiles[0].Reason);
        Assert.Contains("DirectTextProvider", piCase.SkippedFiles[1].Reason);
    }

    [Fact]
    public async Task ReportsSkippedFilesWithoutCheckingOrSummarizingWhenNothingIsReadable()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "scan.png", "not really an image");
        Write(records, "blank.txt", "   \n  ");
        CountingProvider provider = new CountingProvider();

        PICase piCase = await new MedicalRecordAnalyzer(provider).Ingest(MedicalRecordAnalyzer.GetRecordFiles(records));

        Assert.Empty(piCase.DocumentSummaries);
        Assert.Null(piCase.Summary);
        Assert.Empty(piCase.Contradictions);
        Assert.Equal(["blank.txt", "scan.png"], piCase.SkippedFiles.Select(skipped => Path.GetFileName(skipped.FilePath)));
        Assert.Equal(0, provider.Verifications);
        Assert.Equal(0, provider.CaseSummaries);
        Assert.Equal(0, provider.Chronologies);
    }

    [Fact]
    public async Task ReportsProgressThroughEveryStage()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "a.txt", "Record A.");
        Write(records, "b.txt", "Record B.");
        ListProgress progress = new ListProgress();
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(new DummyLLMProvider());

        PICase piCase = await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(records), progress: progress);
        await analyzer.Draft(piCase, progress);

        Assert.Equal(
        [
            new PipelineProgress(PipelineStage.SummarizingDocuments, 0, 2, "a.txt"),
            new PipelineProgress(PipelineStage.SummarizingDocuments, 1, 2, "b.txt"),
            new PipelineProgress(PipelineStage.SummarizingDocuments, 2, 2),
            new PipelineProgress(PipelineStage.FindingContradictions, 0, 1),
            new PipelineProgress(PipelineStage.SummarizingCase, 0, 1),
            new PipelineProgress(PipelineStage.BuildingChronology, 0, 1),
            new PipelineProgress(PipelineStage.Drafting, 0, 1),
        ], progress.Reports);
    }

    [Fact]
    public async Task CacheOnlySummarizesNewOrChangedRecords()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "a.txt", "Record A.");
        FileInfo b = Write(records, "b.txt", "Record B.");
        FileDocumentCache cache = new FileDocumentCache(m_files.Directory.CreateSubdirectory("cache"));
        CountingProvider provider = new CountingProvider();
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(provider);

        await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(records), cache);
        Assert.Equal(2, provider.DocumentSummaries);

        PICase unchanged = await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(records), cache);
        Assert.Equal(2, provider.DocumentSummaries);
        Assert.Equal(2, unchanged.DocumentSummaries.Length);

        File.WriteAllText(b.FullName, "Record B, amended.");
        Write(records, "c.txt", "Record C.");
        await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(records), cache);
        Assert.Equal(4, provider.DocumentSummaries);

        // Contradictions and the case summary are always redone over the whole case.
        Assert.Equal(3, provider.Verifications);
        Assert.Equal(3, provider.CaseSummaries);
    }

    [Fact]
    public async Task CachedResultsFollowARecordToANewPath()
    {
        DirectoryInfo first = RecordsDirectory("first");
        DirectoryInfo second = RecordsDirectory("second");
        Write(first, "note.txt", "Neck pain after collision.");
        FileInfo moved = Write(second, "note.txt", "Neck pain after collision.");
        FileDocumentCache cache = new FileDocumentCache(m_files.Directory.CreateSubdirectory("cache"));
        CountingProvider provider = new CountingProvider();
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(provider);

        await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(first), cache);
        PICase piCase = await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(second), cache);

        Assert.Equal(1, provider.DocumentSummaries);
        Assert.Equal(moved.FullName, piCase.Sources[0].FilePath);
        Assert.All(piCase.DocumentSummaries[0].Citations, citation => Assert.Equal(moved.FullName, citation.FilePath));
    }

    [Fact]
    public async Task UnreadableCacheEntriesAreMisses()
    {
        DirectoryInfo cacheDirectory = m_files.Directory.CreateSubdirectory("cache");
        FileDocumentCache cache = new FileDocumentCache(cacheDirectory);
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory.FullName, "abc.json"), "{ not json");

        Assert.Null(await cache.Get("abc"));
        Assert.Null(await cache.Get("missing"));
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "note.txt", "Neck pain.");
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MedicalRecordAnalyzer(new DummyLLMProvider()).Ingest(MedicalRecordAnalyzer.GetRecordFiles(records), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task DraftsAreWrappedInTheDemandLetterTemplate()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "note.txt", "Neck pain after collision.");
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(new DummyLLMProvider());
        PICase piCase = await analyzer.Ingest(MedicalRecordAnalyzer.GetRecordFiles(records));

        DemandDraft draft = await analyzer.Draft(piCase);

        Assert.StartsWith(DemandLetterTemplate.Opening, draft.Narrative);
        Assert.EndsWith(DemandLetterTemplate.Closing, draft.Narrative);
        Assert.Contains("TestDraftNarrative", draft.Narrative);
        Assert.Contains("Dear [ADJUSTER NAME]:", draft.Narrative);
        Assert.Contains("[DEMAND AMOUNT]", draft.Narrative);
        // The incident type is a placeholder, never assumed to be a collision.
        Assert.Contains("[INCIDENT TYPE]", DemandLetterTemplate.Opening);
        Assert.DoesNotContain("collision", DemandLetterTemplate.Opening + DemandLetterTemplate.Closing, StringComparison.OrdinalIgnoreCase);
        // The template's fixed text is never reported as uncited.
        Assert.DoesNotContain(draft.UnsupportedStatements, statement => statement.Contains("This firm represents"));
    }

    [Fact]
    public async Task DraftJsonReadsBackWithSegmentsAndCitations()
    {
        Citation citation = new Citation() { FilePath = "/records/er.pdf", CitedText = "Denies LOC.", StartPosition = 40, PageNumber = 2 };
        DemandDraft draft = new DemandDraft()
        {
            Segments = [new DraftSegment() { Text = "She denied losing consciousness", Citations = [citation] }, new DraftSegment() { Text = "." }],
            UnsupportedStatements = ["Uncited."],
        };
        FileInfo file = new FileInfo(Path.Combine(m_files.Directory.FullName, "draft.json"));

        await MedicalRecordAnalyzer.SaveJson(draft, file);
        DemandDraft copy = await MedicalRecordAnalyzer.LoadDraft(file);

        Assert.Equal(draft.Narrative, copy.Narrative);
        Assert.Equal(2, Assert.Single(copy.Segments[0].Citations).PageNumber);
        Assert.Equal(["Uncited."], copy.UnsupportedStatements);
    }

    [Fact]
    public void RecordFilesAreSortedAndSkipHiddenFiles()
    {
        DirectoryInfo records = RecordsDirectory("records");
        Write(records, "b.txt", "B");
        Write(records, "a.txt", "A");
        Write(records, ".DS_Store", "hidden");

        Assert.Equal(["a.txt", "b.txt"], MedicalRecordAnalyzer.GetRecordFiles(records).Select(file => file.Name));
    }

    /**
     * Records progress synchronously (Progress<T> would post reports asynchronously).
     */
    private sealed class ListProgress : IProgress<PipelineProgress>
    {
        public List<PipelineProgress> Reports { get; } = [];
        public void Report(PipelineProgress value) => Reports.Add(value);
    }

    /**
     * The dummy provider, counting how often each step runs.
     */
    private sealed class CountingProvider : ILLMProvider
    {
        private readonly DummyLLMProvider m_inner = new DummyLLMProvider();

        public int DocumentSummaries { get; private set; }
        public int Verifications { get; private set; }
        public int CaseSummaries { get; private set; }
        public int Chronologies { get; private set; }

        public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default)
        {
            DocumentSummaries++;
            return m_inner.SummarizeDocument(file, text, cancellationToken);
        }

        public Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default)
        {
            CaseSummaries++;
            return m_inner.SummarizeCase(piCase, cancellationToken);
        }

        public Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default)
        {
            Verifications++;
            return m_inner.Verify(piCase, cancellationToken);
        }

        public Task<ChronologyResult> BuildChronology(PICase piCase, CancellationToken cancellationToken = default)
        {
            Chronologies++;
            return m_inner.BuildChronology(piCase, cancellationToken);
        }

        public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default)
        {
            return m_inner.DraftDemand(piCase, passages, cancellationToken);
        }
    }
}
