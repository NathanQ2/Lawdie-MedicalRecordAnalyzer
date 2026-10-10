using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis.Tests;

public sealed class DirectTextProviderTests : IDisposable
{
    private readonly TestFiles m_files = new TestFiles();
    private readonly DirectTextProvider m_provider = new DirectTextProvider();

    public void Dispose() => m_files.Dispose();

    [Fact]
    public async Task ReadsPlainTextFiles()
    {
        FileInfo file = m_files.WriteText("note.txt", "Patient reports neck pain after collision.");

        OCRResult result = await m_provider.DetectTextFromFile(file);

        Assert.Equal("Patient reports neck pain after collision.", result.Text);
        Assert.Equal(file.FullName, result.File.FullName);
    }

    [Fact]
    public async Task ReadsEveryPageOfDigitalPdfInOrder()
    {
        FileInfo file = m_files.WritePdf("record.pdf", "Page one: acute cervical strain diagnosed.", "Page two: referred to physical therapy.");

        OCRResult result = await m_provider.DetectTextFromFile(file);

        int first = result.Text.IndexOf("acute cervical strain diagnosed", StringComparison.Ordinal);
        int second = result.Text.IndexOf("referred to physical therapy", StringComparison.Ordinal);
        Assert.True(first >= 0 && second > first, $"Unexpected text: {result.Text}");
    }

    [Fact]
    public async Task RejectsPdfWithAnyPageMissingText()
    {
        FileInfo file = m_files.WritePdf("partial-scan.pdf", "Page one: acute cervical strain diagnosed.", null);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(() => m_provider.DetectTextFromFile(file));

        Assert.Contains("Page 2", exception.Message);
    }

    [Theory]
    [InlineData("scan.png")]
    [InlineData("scan.jpg")]
    [InlineData("scan.tiff")]
    public async Task RejectsFilesWithoutEmbeddedText(string name)
    {
        FileInfo file = m_files.WriteText(name, "not really an image");

        await Assert.ThrowsAsync<NotSupportedException>(() => m_provider.DetectTextFromFile(file));
    }

    [Fact]
    public async Task ThrowsForMissingFiles()
    {
        FileInfo file = new FileInfo(Path.Combine(m_files.Directory.FullName, "missing.txt"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => m_provider.DetectTextFromFile(file));
    }
}
