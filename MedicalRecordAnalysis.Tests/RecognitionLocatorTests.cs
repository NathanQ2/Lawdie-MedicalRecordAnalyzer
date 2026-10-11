using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class RecognitionLocatorTests
{
    private static readonly SourceDocument Fax = new SourceDocument()
    {
        FilePath = "/records/fax.pdf",
        // Characters 0-99 are embedded text; 100-199 came from OCR, with handwriting at 150-159 and an uncertain word at 170-174.
        Recognition = new RecognitionSpans()
        {
            Recognized = [new TextSpan(100, 100)],
            Handwritten = [new TextSpan(150, 10)],
            LowConfidence = [new TextSpan(170, 5)],
        },
    };

    private static Citation Quote(int start, int length, string file = "/records/fax.pdf") =>
        new Citation() { FilePath = file, CitedText = new string('x', length), StartPosition = start };

    [Fact]
    public void FlagsQuotesByWhereTheirTextCameFrom()
    {
        Citation embedded = Quote(10, 20);
        Citation printed = Quote(110, 20);
        Citation handwritten = Quote(145, 10);
        Citation uncertain = Quote(165, 20);
        Citation spanning = Quote(90, 20);

        RecognitionLocator.AssignFlags([embedded, printed, handwritten, uncertain, spanning], [Fax]);

        Assert.False(embedded.Recognized);
        Assert.True(printed.Recognized);
        Assert.False(printed.Handwritten || printed.LowConfidence);
        Assert.True(handwritten.Recognized && handwritten.Handwritten);
        Assert.True(uncertain.LowConfidence);
        Assert.True(spanning.Recognized);
    }

    [Fact]
    public void LeavesCitationsFromOtherFilesUnchanged()
    {
        Citation other = Quote(110, 20, "/records/other.pdf");

        RecognitionLocator.AssignFlags([other], [Fax]);

        Assert.False(other.Recognized);
    }

    [Fact]
    public void RendersRecognizedQuotesForReviewOnce()
    {
        Citation handwritten = new Citation()
        {
            FilePath = "/records/fax.pdf", CitedText = "Signed J. Ellis", StartPosition = 150, PageNumber = 2,
            Recognized = true, Handwritten = true, LowConfidence = true,
        };
        DemandDraft draft = new DemandDraft()
        {
            Segments =
            [
                new DraftSegment() { Text = "The referral was signed", Citations = [handwritten] },
                new DraftSegment() { Text = " by the client", Citations = [handwritten, Quote(10, 5)] },
            ],
        };

        string letter = DemandLetterRenderer.Render(draft, new PICase([]), DraftFormat.Markdown);

        Assert.Single(DemandLetterRenderer.FindRecognizedQuotes(draft));
        Assert.Contains("### Quotes read by OCR (check against the original page)\n- fax.pdf, p. 2: \"Signed J. Ellis\" (handwritten; some words uncertain)",
            letter.ReplaceLineEndings("\n"));
    }
}
