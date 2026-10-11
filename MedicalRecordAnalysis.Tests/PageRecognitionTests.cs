using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis.Tests;

public sealed class PageRecognitionTests : IDisposable
{
    private const string PrintedPage = "Page one: acute cervical strain diagnosed.";
    private const string ThirdPage = "Page three: referred to physical therapy.";

    private readonly TestFiles m_files = new TestFiles();

    public void Dispose() => m_files.Dispose();

    [Fact]
    public async Task SendsOnlyPagesWithoutEmbeddedTextToOcr()
    {
        FakePageRecognizer recognizer = new FakePageRecognizer(new RecognizedPage() { PageNumber = 2, Text = "Referral: neck pain since 01/08." });
        FileInfo file = m_files.WritePdf("fax.pdf", PrintedPage, null, ThirdPage);

        OCRResult result = await new DirectTextProvider(recognizer).DetectTextFromFile(file);

        IReadOnlyList<int>? request = Assert.Single(recognizer.Requests);
        Assert.Equal([2], request);
        Assert.Equal(3, result.PageStartPositions.Length);
        Assert.StartsWith("Referral: neck pain", result.Text[result.PageStartPositions[1]..]);
        Assert.StartsWith("Page three", result.Text[result.PageStartPositions[2]..]);

        // Only the second page is marked as recognized.
        TextSpan recognized = Assert.Single(result.Recognition.Recognized);
        Assert.Equal("Referral: neck pain since 01/08.", result.Text.Substring(recognized.StartPosition, recognized.Length));
    }

    [Fact]
    public async Task DigitalPdfsNeverCallTheRecognizer()
    {
        FakePageRecognizer recognizer = new FakePageRecognizer();
        FileInfo file = m_files.WritePdf("record.pdf", PrintedPage, ThirdPage);

        OCRResult result = await new DirectTextProvider(recognizer).DetectTextFromFile(file);

        Assert.Empty(recognizer.Requests);
        Assert.Empty(result.Recognition.Recognized);
    }

    [Fact]
    public async Task RejectsThePdfWhenOcrReturnsNoResultForAPage()
    {
        FakePageRecognizer recognizer = new FakePageRecognizer(new RecognizedPage() { PageNumber = 2, Text = "Second page." });
        FileInfo file = m_files.WritePdf("fax.pdf", null, null);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => new DirectTextProvider(recognizer).DetectTextFromFile(file));

        Assert.Contains("page 1", exception.Message);
    }

    [Fact]
    public async Task KeepsBlankRecognizedPagesSoPageNumbersStayAligned()
    {
        FakePageRecognizer recognizer = new FakePageRecognizer(new RecognizedPage() { PageNumber = 2, Text = "" });
        FileInfo file = m_files.WritePdf("fax.pdf", PrintedPage, null, ThirdPage);

        OCRResult result = await new DirectTextProvider(recognizer).DetectTextFromFile(file);

        Assert.Equal(3, result.PageStartPositions.Length);
        Assert.StartsWith("Page three", result.Text[result.PageStartPositions[2]..]);
        Assert.Empty(result.Recognition.Recognized);
    }

    [Fact]
    public async Task ReadsImagesThroughTheRecognizer()
    {
        FakePageRecognizer recognizer = new FakePageRecognizer(
            new RecognizedPage() { PageNumber = 2, Text = "Second scanned page." },
            new RecognizedPage() { PageNumber = 1, Text = "First scanned page." });
        FileInfo file = m_files.WriteText("scan.tiff", "image bytes");

        OCRResult result = await new DirectTextProvider(recognizer).DetectTextFromFile(file);

        Assert.Null(Assert.Single(recognizer.Requests));
        Assert.Equal("First scanned page.\n\nSecond scanned page.", result.Text);
        Assert.Equal([0, 21], result.PageStartPositions);
        Assert.Equal(2, result.Recognition.Recognized.Length);
    }

    [Fact]
    public async Task MovesHandwritingAndLowConfidenceSpansIntoDocumentPositions()
    {
        // Leading whitespace is trimmed from the page, so the spans shift left with it.
        RecognizedPage page = new RecognizedPage()
        {
            PageNumber = 2,
            Text = "  Signed: J. Ellis 02/20",
            HandwrittenSpans = [new TextSpan(10, 8)],
            LowConfidenceSpans = [new TextSpan(19, 5)],
        };
        FileInfo file = m_files.WritePdf("fax.pdf", PrintedPage, null);

        OCRResult result = await new DirectTextProvider(new FakePageRecognizer(page)).DetectTextFromFile(file);

        TextSpan handwritten = Assert.Single(result.Recognition.Handwritten);
        TextSpan uncertain = Assert.Single(result.Recognition.LowConfidence);
        Assert.Equal("J. Ellis", result.Text.Substring(handwritten.StartPosition, handwritten.Length));
        Assert.Equal("02/20", result.Text.Substring(uncertain.StartPosition, uncertain.Length));
    }

    [Fact]
    public async Task WithoutARecognizerImagesAreStillRejected()
    {
        FileInfo file = m_files.WriteText("scan.png", "image bytes");

        await Assert.ThrowsAsync<NotSupportedException>(() => new DirectTextProvider().DetectTextFromFile(file));
    }
}
