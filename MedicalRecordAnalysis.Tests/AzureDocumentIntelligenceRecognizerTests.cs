using Azure.AI.DocumentIntelligence;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis.Tests;

public class AzureDocumentIntelligenceRecognizerTests
{
    // The service returns one text for the request, with each page's span of it.
    private const string Content = "REFERRAL\nNeck pain since 01/08\nSigned J. Ellis";

    private static AnalyzeResult CreateResult()
    {
        DocumentPage first = DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: 2,
            spans: [DocumentIntelligenceModelFactory.DocumentSpan(0, 30)],
            words:
            [
                DocumentIntelligenceModelFactory.DocumentWord("REFERRAL", [], DocumentIntelligenceModelFactory.DocumentSpan(0, 8), 0.99f),
                DocumentIntelligenceModelFactory.DocumentWord("01/08", [], DocumentIntelligenceModelFactory.DocumentSpan(25, 5), 0.42f),
            ]);
        DocumentPage second = DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: 5,
            spans: [DocumentIntelligenceModelFactory.DocumentSpan(31, 15)]);
        DocumentStyle handwriting = DocumentIntelligenceModelFactory.DocumentStyle(
            isHandwritten: true, spans: [DocumentIntelligenceModelFactory.DocumentSpan(38, 8)], confidence: 0.9f);

        return DocumentIntelligenceModelFactory.AnalyzeResult(content: Content, pages: [first, second], styles: [handwriting]);
    }

    [Fact]
    public void SplitsTheResultIntoPagesWithTheirNumbers()
    {
        RecognizedPage[] pages = AzureDocumentIntelligenceRecognizer.ToPages(CreateResult());

        Assert.Equal([2, 5], pages.Select(page => page.PageNumber));
        Assert.Equal("REFERRAL\nNeck pain since 01/08", pages[0].Text);
        Assert.Equal("Signed J. Ellis", pages[1].Text);
    }

    [Fact]
    public void ReportsHandwritingAndUncertainWordsInPagePositions()
    {
        RecognizedPage[] pages = AzureDocumentIntelligenceRecognizer.ToPages(CreateResult());

        TextSpan uncertain = Assert.Single(pages[0].LowConfidenceSpans);
        Assert.Equal("01/08", pages[0].Text.Substring(uncertain.StartPosition, uncertain.Length));
        Assert.Empty(pages[0].HandwrittenSpans);

        TextSpan handwritten = Assert.Single(pages[1].HandwrittenSpans);
        Assert.Equal("J. Ellis", pages[1].Text.Substring(handwritten.StartPosition, handwritten.Length));
    }

    [Fact]
    public void JoinsAPageSplitAcrossSpansWithANewline()
    {
        DocumentPage page = DocumentIntelligenceModelFactory.DocumentPage(
            pageNumber: 1,
            spans: [DocumentIntelligenceModelFactory.DocumentSpan(31, 15), DocumentIntelligenceModelFactory.DocumentSpan(0, 8)]);
        DocumentStyle handwriting = DocumentIntelligenceModelFactory.DocumentStyle(
            isHandwritten: true, spans: [DocumentIntelligenceModelFactory.DocumentSpan(38, 8)], confidence: 0.9f);

        RecognizedPage recognized = Assert.Single(AzureDocumentIntelligenceRecognizer.ToPages(
            DocumentIntelligenceModelFactory.AnalyzeResult(content: Content, pages: [page], styles: [handwriting])));

        Assert.Equal("REFERRAL\nSigned J. Ellis", recognized.Text);
        TextSpan span = Assert.Single(recognized.HandwrittenSpans);
        Assert.Equal("J. Ellis", recognized.Text.Substring(span.StartPosition, span.Length));
    }

    [Theory]
    [InlineData(null, "key")]
    [InlineData("https://example.cognitiveservices.azure.com/", null)]
    [InlineData("", "")]
    public void IsNotConfiguredWithoutBothEndpointAndKey(string? endpoint, string? key)
    {
        Assert.Null(AzureDocumentIntelligenceRecognizer.Create(endpoint, key));
    }

    [Fact]
    public void RejectsEndpointsThatAreNotHttps()
    {
        Assert.Throws<InvalidOperationException>(() => AzureDocumentIntelligenceRecognizer.Create("http://example.com/", "key"));
    }
}
