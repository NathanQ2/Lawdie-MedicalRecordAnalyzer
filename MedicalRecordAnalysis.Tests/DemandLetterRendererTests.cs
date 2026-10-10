using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class DemandLetterRendererTests
{
    private static readonly Citation DeniesLoc = new Citation() { FilePath = "/records/er.txt", CitedText = "Denies loss of\nconsciousness.", StartPosition = 657 };
    private static readonly Citation LostLoc = new Citation() { FilePath = "/records/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 319 };

    private static DemandDraft CreateDraft() => new DemandDraft()
    {
        Segments =
        [
            new DraftSegment() { Text = "The ER record states the client " },
            new DraftSegment() { Text = "denied loss of consciousness", Citations = [DeniesLoc, DeniesLoc] },
            new DraftSegment() { Text = ", while the PT record states the client " },
            new DraftSegment() { Text = "briefly lost consciousness ", Citations = [LostLoc] },
            new DraftSegment() { Text = "at the scene. Claim [CLAIM NUMBER]; the ER " },
            new DraftSegment() { Text = "denial is repeated", Citations = [DeniesLoc] },
            new DraftSegment() { Text = ". Demand: [AMOUNT DEMANDED] for [CLAIM NUMBER]." },
        ],
        UnsupportedStatements = ["Claim [CLAIM NUMBER]; the ER denial is repeated."],
    };

    private static PICase CreateCase()
    {
        PICase piCase = new PICase(0);
        piCase.Contradictions = [new Contradiction() { Summary = "Loss of consciousness conflicts.", Citations = [DeniesLoc, LostLoc] }];
        return piCase;
    }

    [Fact]
    public void MarkdownNumbersEachQuoteOnceAndPlacesMarkersAfterClaims()
    {
        string letter = DemandLetterRenderer.Render(CreateDraft(), CreateCase(), DraftFormat.Markdown);

        Assert.Contains("client denied loss of consciousness[^1], while the PT record states the client briefly lost consciousness[^2] at the scene.", letter);
        Assert.Contains("the ER denial is repeated[^1].", letter);
        Assert.Contains("[^1]: er.txt, character 657: \"Denies loss of consciousness.\"", letter);
        Assert.Contains("[^2]: pt.txt, character 319: \"Briefly lost consciousness.\"", letter);
        Assert.DoesNotContain("[^3]", letter);
    }

    [Fact]
    public void ReviewSectionListsContradictionsUnsupportedStatementsAndPlaceholders()
    {
        string letter = DemandLetterRenderer.Render(CreateDraft(), CreateCase(), DraftFormat.Markdown);
        string review = letter[letter.IndexOf("## Attorney Review", StringComparison.Ordinal)..];

        Assert.Contains("- Loss of consciousness conflicts.\n    - er.txt, character 657: \"Denies loss of consciousness.\"\n    - pt.txt, character 319", review.ReplaceLineEndings("\n"));
        Assert.Contains("- Claim [CLAIM NUMBER]; the ER denial is repeated.", review);
        Assert.Contains("- [CLAIM NUMBER]", review);
        Assert.Contains("- [AMOUNT DEMANDED]", review);
        Assert.Single(review.Split('\n'), line => line.Trim() == "- [CLAIM NUMBER]");
    }

    [Fact]
    public void EmptyReviewSectionsSayNone()
    {
        DemandDraft draft = new DemandDraft() { Segments = [new DraftSegment() { Text = "Cited fact", Citations = [LostLoc] }] };

        string letter = DemandLetterRenderer.Render(draft, new PICase(0), DraftFormat.Markdown);

        Assert.Equal(3, letter.Split('\n').Count(line => line.Trim() == "None."));
    }

    [Fact]
    public void PlainTextUsesBracketMarkersAndASourcesList()
    {
        string letter = DemandLetterRenderer.Render(CreateDraft(), CreateCase(), DraftFormat.PlainText);

        Assert.Contains("denied loss of consciousness[1]", letter);
        Assert.Contains("SOURCES", letter);
        Assert.Contains("[2] pt.txt, character 319: \"Briefly lost consciousness.\"", letter);
        Assert.Contains("ATTORNEY REVIEW - REMOVE BEFORE SENDING", letter);
        Assert.DoesNotContain("[^", letter);
        Assert.DoesNotContain("#", letter);
    }

    [Theory]
    [InlineData("Draft.txt", DraftFormat.PlainText)]
    [InlineData("Draft.TXT", DraftFormat.PlainText)]
    [InlineData("Draft.md", DraftFormat.Markdown)]
    [InlineData("Draft", DraftFormat.Markdown)]
    public void FormatFollowsTheOutputExtension(string fileName, DraftFormat expected)
    {
        Assert.Equal(expected, DemandLetterRenderer.FormatFor(new FileInfo(fileName)));
    }
}
