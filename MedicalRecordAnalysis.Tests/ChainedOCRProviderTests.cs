using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis.Tests;

public sealed class ChainedOCRProviderTests : IDisposable
{
    private readonly TestFiles m_files = new TestFiles();

    public void Dispose() => m_files.Dispose();

    [Fact]
    public async Task UsesFirstProviderThatCanReadTheFile()
    {
        FileInfo file = m_files.WriteText("note.txt", "Embedded text.");
        ChainedOCRProvider provider = new ChainedOCRProvider([new DirectTextProvider(), new DummyOCRProvider()]);

        OCRResult result = await provider.DetectTextFromFile(file);

        Assert.Equal("Embedded text.", result.Text);
    }

    [Fact]
    public async Task FallsBackWhenAProviderCannotReadTheFile()
    {
        FileInfo file = m_files.WriteText("scan.png", "not really an image");
        ChainedOCRProvider provider = new ChainedOCRProvider([new DirectTextProvider(), new DummyOCRProvider()]);

        OCRResult result = await provider.DetectTextFromFile(file);

        Assert.Equal("Test OCR Text", result.Text);
    }

    [Fact]
    public async Task ThrowsWithReasonsWhenNoProviderCanReadTheFile()
    {
        FileInfo file = m_files.WriteText("scan.png", "not really an image");
        ChainedOCRProvider provider = new ChainedOCRProvider([new DirectTextProvider()]);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() => provider.DetectTextFromFile(file));

        Assert.Contains("scan.png", exception.Message);
        Assert.Contains(nameof(DirectTextProvider), exception.Message);
    }
}
