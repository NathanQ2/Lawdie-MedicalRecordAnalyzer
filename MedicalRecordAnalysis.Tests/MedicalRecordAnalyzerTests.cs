using System.Text.Json;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public sealed class MedicalRecordAnalyzerTests : IDisposable
{
    private readonly TestFiles m_files = new TestFiles();

    public void Dispose() => m_files.Dispose();

    [Fact]
    public void IngestSummarizesReadableFilesAndSkipsTheRest()
    {
        FileInfo note = m_files.WriteText("note.txt", "Patient reports neck pain after collision.");
        FileInfo record = m_files.WritePdf("record.pdf", "Acute cervical strain diagnosed at the emergency department.");
        m_files.WriteText("scan.png", "not really an image");
        m_files.WriteText(".DS_Store", "hidden");
        FileInfo output = new FileInfo(Path.Combine(m_files.Directory.FullName, "out", "case.json"));
        output.Directory!.Create();

        new MedicalRecordAnalyzer(new DummyLLMProvider()).Ingest(m_files.Directory, output);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(output.FullName));
        string[] citedFiles = json.RootElement.GetProperty("DocumentSummaries").EnumerateArray()
            .Select(summary => summary.GetProperty("Citations")[0].GetProperty("FilePath").GetString()!)
            .Order()
            .ToArray();
        Assert.Equal(new[] { note.FullName, record.FullName }.Order(), citedFiles);
    }
}
