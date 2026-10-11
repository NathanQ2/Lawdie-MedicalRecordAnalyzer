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
        PICase piCase = new PICase([]);
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
    public void ReviewSectionLabelsUnverifiedContradictions()
    {
        PICase piCase = CreateCase();
        piCase.Contradictions = [.. piCase.Contradictions, new Contradiction() { Summary = "Dates conflict.", Citations = [] }];

        string letter = DemandLetterRenderer.Render(CreateDraft(), piCase, DraftFormat.Markdown);

        Assert.Contains("- Dates conflict. (Unverified: not supported by two different quotes.)", letter);
        Assert.DoesNotContain("Loss of consciousness conflicts. (Unverified", letter);
    }

    [Fact]
    public void EmptyReviewSectionsSayNone()
    {
        DemandDraft draft = new DemandDraft() { Segments = [new DraftSegment() { Text = "Cited fact", Citations = [LostLoc] }] };

        string letter = DemandLetterRenderer.Render(draft, new PICase([]), DraftFormat.Markdown);

        Assert.Equal(4, letter.Split('\n').Count(line => line.Trim() == "None."));
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

    [Fact]
    public void SourceNotesShowPagesWhenKnown()
    {
        Citation onePage = new Citation() { FilePath = "/records/ed.pdf", CitedText = "Denies LOC.", StartPosition = 900, PageNumber = 2 };
        Citation twoPages = new Citation() { FilePath = "/records/pt.pdf", CitedText = "NDI 44%.", StartPosition = 4000, PageNumber = 3, EndPageNumber = 4 };
        DemandDraft draft = new DemandDraft()
        {
            Segments = [new DraftSegment() { Text = "Denied LOC", Citations = [onePage] }, new DraftSegment() { Text = " and NDI 44%", Citations = [twoPages] }],
        };

        string letter = DemandLetterRenderer.Render(draft, new PICase([]), DraftFormat.Markdown);

        Assert.Contains("[^1]: ed.pdf, p. 2: \"Denies LOC.\"", letter);
        Assert.Contains("[^2]: pt.pdf, pp. 3-4: \"NDI 44%.\"", letter);
    }

    [Fact]
    public void LaysOutLetterBlocksAndHeadingsForEachFormat()
    {
        DemandDraft draft = new DemandDraft()
        {
            Segments = [new DraftSegment() { Text = "[LAW FIRM NAME]\n[FIRM ADDRESS]\n\n## Medical Expenses\n\n- ED: $6,912.40\n- PT: $9,860.00" }],
        };

        string markdown = DemandLetterRenderer.Render(draft, new PICase([]), DraftFormat.Markdown).ReplaceLineEndings("\n");
        string text = DemandLetterRenderer.Render(draft, new PICase([]), DraftFormat.PlainText).ReplaceLineEndings("\n");

        // Markdown keeps address lines apart with hard line breaks, but not before headings or list items.
        Assert.StartsWith("[LAW FIRM NAME]  \n[FIRM ADDRESS]\n\n## Medical Expenses\n\n- ED: $6,912.40\n- PT: $9,860.00", markdown);
        Assert.StartsWith("[LAW FIRM NAME]\n[FIRM ADDRESS]\n\nMEDICAL EXPENSES\n\n- ED: $6,912.40", text);
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
