using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;

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
    public async Task IngestFindsContradictionsBeforeSummarizingTheCase()
    {
        m_files.WriteText("note.txt", "Patient reports neck pain after collision.");
        FileInfo output = new FileInfo(Path.Combine(m_files.Directory.FullName, "out", "case.json"));
        output.Directory!.Create();
        OrderRecordingProvider provider = new OrderRecordingProvider();

        await new MedicalRecordAnalyzer(provider).Ingest(m_files.Directory, output);

        Assert.Equal(["SummarizeDocument", "Verify", "SummarizeCase"], provider.Calls);
        Assert.Equal(1, provider.ContradictionsSeenBySummarizeCase);
    }

    private sealed class OrderRecordingProvider : ILLMProvider
    {
        public List<string> Calls { get; } = [];
        public int ContradictionsSeenBySummarizeCase { get; private set; } = -1;

        public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text)
        {
            Calls.Add(nameof(SummarizeDocument));
            return Task.FromResult(new DocumentSummary() { Summary = "summary" });
        }

        public Task<CaseSummary> SummarizeCase(PICase piCase)
        {
            Calls.Add(nameof(SummarizeCase));
            ContradictionsSeenBySummarizeCase = piCase.Contradictions.Length;
            return Task.FromResult(new CaseSummary() { Summary = "case" });
        }

        public Task<VerificationResult> Verify(PICase piCase)
        {
            Calls.Add(nameof(Verify));
            return Task.FromResult(new VerificationResult() { Contradictions = [new Contradiction() { Summary = "dates differ", Citations = [] }] });
        }
    }
}
