using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class LetterEditsTests
{
    private static readonly Citation DeniesLoc = new Citation() { FilePath = "/records/er.txt", CitedText = "Denies loss of consciousness.", StartPosition = 657, PageNumber = 2 };
    private static readonly Citation LostLoc = new Citation() { FilePath = "/records/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 319, PageNumber = 1 };

    private const string Salutation = "Dear [ADJUSTER NAME]:\n\n";
    private const string Body = "The ER record states she denied loss of consciousness. She drove herself home.\n\n";
    private const string Closing = "Sincerely,\n\n[ATTORNEY NAME]";

    // A salutation and closing never checked for citations, and a body paragraph with one cited and one uncited sentence.
    private static DemandDraft CreateLetter() => new DemandDraft()
    {
        Segments =
        [
            new DraftSegment() { Text = Salutation + "The ER record states she " },
            new DraftSegment() { Text = "denied loss of consciousness", Citations = [DeniesLoc.WithClaim("denied loss of consciousness")] },
            new DraftSegment() { Text = ". She drove herself home.\n\n" + Closing },
        ],
        UnsupportedStatements = ["She drove herself home."],
    };

    private static readonly Dictionary<int, Citation> Sources = new Dictionary<int, Citation>() { [1] = DeniesLoc, [2] = LostLoc };

    private static string BodyParagraph => Body.TrimEnd();

    private static DemandDraft EditBody(string edited, IReadOnlyDictionary<int, Citation>? sources = null) =>
        LetterEdits.Replace(CreateLetter(), Salutation.Length, BodyParagraph, edited, sources ?? Sources);

    [Fact]
    public void KeepsASourceAfterTheSentenceItSupports()
    {
        DemandDraft edited = EditBody("Per the ER record, she denied any loss of consciousness.[1] She drove herself home.");

        Assert.Equal(Salutation + "Per the ER record, she denied any loss of consciousness. She drove herself home.\n\n" + Closing, edited.Narrative);
        DraftSegment cited = edited.Segments.Single(segment => segment.Citations.Length > 0);
        Assert.Equal("Per the ER record, she denied any loss of consciousness.", cited.Text);
        // The letter's own copy of the source is used, linked to the sentence as it now reads.
        Citation source = cited.Citations.Single();
        Assert.Equal((DeniesLoc.FilePath, DeniesLoc.StartPosition, DeniesLoc.CitedText, 2), (source.FilePath, source.StartPosition, source.CitedText, source.PageNumber));
        Assert.Equal("Per the ER record, she denied any loss of consciousness.", source.Claim);
        Assert.Equal(["She drove herself home."], edited.UnsupportedStatements);
    }

    [Fact]
    public void ListsNewSentencesAndSentencesThatLostTheirSource()
    {
        DemandDraft added = EditBody("She was alert at the scene. The ER record states she denied loss of consciousness.[1] She drove herself home.");
        DemandDraft removed = EditBody("The ER record states she denied loss of consciousness. She drove herself home.");

        Assert.Equal(["She was alert at the scene.", "She drove herself home."], added.UnsupportedStatements);
        Assert.Equal(["The ER record states she denied loss of consciousness.", "She drove herself home."], removed.UnsupportedStatements);
    }

    [Fact]
    public void ASourceSupportsOnlyTheSentenceItFollows()
    {
        DemandDraft edited = EditBody("She lost consciousness for an hour. She denied loss of consciousness.[1]");

        Assert.Equal(["She lost consciousness for an hour."], edited.UnsupportedStatements);
        Assert.Equal("She denied loss of consciousness.", edited.Segments.Single(segment => segment.Citations.Length > 0).Text);
    }

    [Fact]
    public void CanUseAnotherOfTheLettersSources()
    {
        DemandDraft letter = CreateLetter();
        DemandDraft withBoth = new DemandDraft()
        {
            Segments = [.. letter.Segments, new DraftSegment() { Text = " Later notes differ.", Citations = [LostLoc] }],
            UnsupportedStatements = letter.UnsupportedStatements,
        };

        DemandDraft edited = LetterEdits.Replace(withBoth, Salutation.Length, BodyParagraph, "Accounts differ on consciousness.[1] [2]", Sources);

        Assert.Equal([DeniesLoc.CitedText, LostLoc.CitedText], edited.Segments.First(segment => segment.Citations.Length > 0).Citations.Select(citation => citation.CitedText));
    }

    [Fact]
    public void FixedTextStaysUnlistedUntilItIsEdited()
    {
        DemandDraft bodyEdited = EditBody("She denied loss of consciousness.[1]");
        DemandDraft salutationEdited = LetterEdits.Replace(CreateLetter(), 0, "Dear [ADJUSTER NAME]:", "Dear Dana Reyes:", Sources);

        Assert.Empty(bodyEdited.UnsupportedStatements);
        Assert.Equal(["Dear Dana Reyes:", "She drove herself home."], salutationEdited.UnsupportedStatements);
        Assert.EndsWith("Sincerely,\n\n[ATTORNEY NAME]", salutationEdited.Narrative);
    }

    [Fact]
    public void RejectsSourcesTheLetterDoesNotCite()
    {
        Citation other = new Citation() { FilePath = "/records/other.txt", CitedText = "Invented.", StartPosition = 0 };

        LetterEditException foreign = Assert.Throws<LetterEditException>(() => EditBody("A claim.[3]", new Dictionary<int, Citation>() { [3] = other }));
        LetterEditException unknown = Assert.Throws<LetterEditException>(() => EditBody("A claim.[7]"));
        LetterEditException nothingToSupport = Assert.Throws<LetterEditException>(() => EditBody("[1] A claim."));

        Assert.False(foreign.Conflict);
        Assert.Contains("[7]", unknown.Message);
        Assert.False(nothingToSupport.Conflict);
    }

    [Fact]
    public void RejectsAnEditOfTextThatChanged()
    {
        LetterEditException changed = Assert.Throws<LetterEditException>(() =>
            LetterEdits.Replace(CreateLetter(), Salutation.Length, "Something else entirely.", "New text.", Sources));
        LetterEditException outside = Assert.Throws<LetterEditException>(() =>
            LetterEdits.Replace(CreateLetter(), 10_000, BodyParagraph, "New text.", Sources));

        Assert.True(changed.Conflict);
        Assert.True(outside.Conflict);
    }
}
