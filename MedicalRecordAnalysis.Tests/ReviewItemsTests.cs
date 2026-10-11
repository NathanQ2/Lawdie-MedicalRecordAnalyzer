using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class ReviewItemsTests
{
    // Analysis quotes and draft citations come from the same files but are not the same spans.
    private static readonly Citation EmsQuote = new Citation() { FilePath = "/r/ems.pdf", CitedText = "may have blacked out", StartPosition = 100 };
    private static readonly Citation EdQuote = new Citation() { FilePath = "/r/ed.pdf", CitedText = "Denies loss of consciousness.", StartPosition = 500 };
    private static readonly Citation EmsPassage = new Citation() { FilePath = "/r/ems.pdf", CitedText = "Patient states she may have blacked out for a second.", StartPosition = 80 };
    private static readonly Citation OtherPassage = new Citation() { FilePath = "/r/ems.pdf", CitedText = "BP 132/84.", StartPosition = 900 };

    private static readonly DemandDraft Draft = new DemandDraft()
    {
        Segments =
        [
            new DraftSegment() { Text = "## Facts\n\n" },
            new DraftSegment() { Text = "She briefly lost consciousness at the scene", Citations = [EmsPassage] },
            new DraftSegment() { Text = ". Her blood pressure was elevated", Citations = [OtherPassage] },
            new DraftSegment() { Text = ". Claim [CLAIM NUMBER] is open; see [CLAIM NUMBER]." },
        ],
        UnsupportedStatements = ["Claim [CLAIM NUMBER] is open; see [CLAIM NUMBER]."],
    };

    private static PICase Analysis()
    {
        PICase piCase = new PICase([]);
        piCase.Contradictions =
        [
            new Contradiction() { Summary = "Loss of consciousness conflicts.", Citations = [EmsQuote, EdQuote] },
            new Contradiction() { Summary = "Dental record is unrelated.", Citations = [EdQuote, new Citation() { FilePath = "/r/dental.txt", CitedText = "Cleaning.", StartPosition = 0 }] },
        ];
        return piCase;
    }

    private static string Text(DraftRange range) => Draft.Narrative.Substring(range.Start, range.Length);

    [Fact]
    public void ContradictionsPointToTheDraftSentencesCitingTheirQuotes()
    {
        IReadOnlyList<ReviewItem> items = ReviewItems.Build(Analysis(), Draft, new Dictionary<string, ReviewState>());

        ReviewItem loc = items.Single(item => item.Text == "Loss of consciousness conflicts.");
        DraftRange range = Assert.Single(loc.DraftRanges);
        Assert.Equal("She briefly lost consciousness at the scene", Text(range));

        // The letter doesn't cite either quote of this one.
        Assert.Empty(items.Single(item => item.Text == "Dental record is unrelated.").DraftRanges);
    }

    [Fact]
    public void DraftItemsPointToTheirOwnText()
    {
        IReadOnlyList<ReviewItem> items = ReviewItems.Build(Analysis(), Draft, new Dictionary<string, ReviewState>());

        ReviewItem placeholder = items.Single(item => item.Kind == ReviewItemKind.Placeholder);
        Assert.Equal(2, placeholder.DraftRanges.Length);
        Assert.All(placeholder.DraftRanges, range => Assert.Equal("[CLAIM NUMBER]", Text(range)));

        ReviewItem unsupported = items.Single(item => item.Kind == ReviewItemKind.UnsupportedStatement);
        Assert.Equal("Claim [CLAIM NUMBER] is open; see [CLAIM NUMBER].", Text(Assert.Single(unsupported.DraftRanges)));
    }

    [Fact]
    public void ContradictionsHaveNoDraftRangesBeforeADraftExists()
    {
        IReadOnlyList<ReviewItem> items = ReviewItems.Build(Analysis(), null, new Dictionary<string, ReviewState>());

        Assert.All(items, item => Assert.Empty(item.DraftRanges));
    }
}
