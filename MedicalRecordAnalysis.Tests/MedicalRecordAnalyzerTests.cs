using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public sealed class MedicalRecordAnalyzerTests : IDisposable
{
    private readonly TestFiles m_files = new TestFiles();

    public void Dispose() => m_files.Dispose();

    [Fact]
    public async Task IngestSummarizesReadableFilesAndSkipsTheRest()
    {
        FileInfo note = m_files.WriteText("note.txt", "Patient reports neck pain after collision.");
        FileInfo record = m_files.WritePdf("record.pdf", "Acute cervical strain diagnosed at the emergency department.");
        m_files.WriteText("scan.png", "not really an image");
        m_files.WriteText(".DS_Store", "hidden");
        FileInfo output = new FileInfo(Path.Combine(m_files.Directory.FullName, "out", "case.json"));
        output.Directory!.Create();

        await new MedicalRecordAnalyzer(new DummyLLMProvider()).Ingest(m_files.Directory, output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(output.FullName));
        string[] citedFiles = json.RootElement.GetProperty("DocumentSummaries").EnumerateArray()
            .Select(summary => summary.GetProperty("Citations")[0].GetProperty("FilePath").GetString()!)
            .Order()
            .ToArray();
        Assert.Equal(new[] { note.FullName, record.FullName }.Order(), citedFiles);
    }

    [Fact]
    public async Task DraftReadsTheIngestedCaseAndWritesALetterWithSources()
    {
        FileInfo note = m_files.WriteText("note.txt", "Patient reports neck pain after collision.");
        DirectoryInfo outputDirectory = m_files.Directory.CreateSubdirectory("out");
        FileInfo caseFile = new FileInfo(Path.Combine(outputDirectory.FullName, "case.json"));
        FileInfo letterFile = new FileInfo(Path.Combine(outputDirectory.FullName, "Draft.md"));
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(new DummyLLMProvider());

        await analyzer.Ingest(m_files.Directory, caseFile);
        await analyzer.Draft(caseFile, letterFile);

        string letter = await File.ReadAllTextAsync(letterFile.FullName);
        Assert.Contains("TestDraftNarrative[^1]", letter);
        Assert.Contains($"[^1]: {note.Name}, p. 1: \"TestCitation\"", letter);
        Assert.Contains("## Attorney Review - Remove Before Sending", letter);
        Assert.Contains("- [CLAIM NUMBER]", letter);
    }

    [Fact]
    public async Task IngestStoresPassagesThatDraftRetrievesFromTheExport()
    {
        FileInfo note = m_files.WriteText("note.txt", "Patient reports neck pain after a rear-end collision.\n\nPlan: physical therapy.");
        DirectoryInfo outputDirectory = m_files.Directory.CreateSubdirectory("out");
        FileInfo caseFile = new FileInfo(Path.Combine(outputDirectory.FullName, "case.json"));
        OrderRecordingProvider provider = new OrderRecordingProvider();
        MedicalRecordAnalyzer analyzer = new MedicalRecordAnalyzer(provider);

        await analyzer.Ingest(m_files.Directory, caseFile);
        await analyzer.Draft(caseFile, new FileInfo(Path.Combine(outputDirectory.FullName, "Draft.md")));

        RetrievedPassage passage = Assert.Single(provider.PassagesSeenByDraftDemand);
        Assert.Equal(note.FullName, passage.FilePath);
        Assert.Equal(0, passage.StartPosition);
        Assert.Equal(await File.ReadAllTextAsync(note.FullName), passage.Text);
    }

    [Fact]
    public async Task DraftRejectsAFileWithoutACase()
    {
        FileInfo caseFile = m_files.WriteText("case.json", "null");
        FileInfo letterFile = new FileInfo(Path.Combine(m_files.Directory.FullName, "Draft.md"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new MedicalRecordAnalyzer(new DummyLLMProvider()).Draft(caseFile, letterFile));
    }

    [Fact]
    public async Task IngestFindsContradictionsBeforeSummarizingTheCase()
    {
        m_files.WriteText("note.txt", "Patient reports neck pain after collision.");
        FileInfo output = new FileInfo(Path.Combine(m_files.Directory.FullName, "out", "case.json"));
        output.Directory!.Create();
        OrderRecordingProvider provider = new OrderRecordingProvider();

        await new MedicalRecordAnalyzer(provider).Ingest(m_files.Directory, output);

        Assert.Equal(["SummarizeDocument", "Verify", "SummarizeCase", "BuildChronology"], provider.Calls);
        Assert.Equal(1, provider.ContradictionsSeenBySummarizeCase);
    }

    private sealed class OrderRecordingProvider : ILLMProvider
    {
        public List<string> Calls { get; } = [];
        public int ContradictionsSeenBySummarizeCase { get; private set; } = -1;
        public IReadOnlyList<RetrievedPassage> PassagesSeenByDraftDemand { get; private set; } = [];

        public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(SummarizeDocument));
            Citation quote = new Citation() { FilePath = file.FullName, CitedText = text, StartPosition = 0 };
            return Task.FromResult(new DocumentSummary() { Summary = "summary", Citations = [quote] });
        }

        public Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(SummarizeCase));
            ContradictionsSeenBySummarizeCase = piCase.Contradictions.Length;
            return Task.FromResult(new CaseSummary() { Summary = "case" });
        }

        public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(DraftDemand));
            PassagesSeenByDraftDemand = passages;
            return Task.FromResult(new DemandDraft() { Segments = [] });
        }

        public Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(Verify));
            return Task.FromResult(new VerificationResult() { Contradictions = [new Contradiction() { Summary = "dates differ", Citations = [] }] });
        }

        public Task<ChronologyResult> BuildChronology(PICase piCase, CancellationToken cancellationToken = default)
        {
            Calls.Add(nameof(BuildChronology));
            return Task.FromResult(new ChronologyResult());
        }
    }
}
