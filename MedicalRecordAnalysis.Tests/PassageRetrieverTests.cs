using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class PassageRetrieverTests
{
    private static readonly DraftTopic[] Topics =
    [
        new DraftTopic("Incident", ["collision", "rear-end"]),
        new DraftTopic("Pain", ["pain", "/10", "unable"]),
    ];

    private static PICase CreateCase(params string[] passageTexts)
    {
        int position = 0;
        List<Passage> passages = [];
        foreach (string text in passageTexts)
        {
            passages.Add(new Passage() { StartPosition = position, Text = text });
            position += text.Length + 2;
        }

        PICase piCase = new PICase([new DocumentSummary() { Summary = "summary" }]);
        piCase.Sources = [new SourceDocument() { FilePath = "/records/note.txt", Passages = passages.ToArray() }];
        return piCase;
    }

    [Fact]
    public void KeepsEveryPassageWhenTheCaseFitsTheBudget()
    {
        PICase piCase = CreateCase("Billing address on file.", "Rear-end collision.");

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 1000);

        Assert.Equal(2, selected.Length);
    }

    [Fact]
    public void SelectsTopicalPassagesAndDropsIrrelevantOnes()
    {
        PICase piCase = CreateCase(
            "Billing address and insurance card on file.",
            "Patient was in a rear-end collision at a red light.",
            "Fax cover sheet, page 1 of 3.",
            "Neck pain rated 7/10; unable to lift child.");

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 120);

        Assert.Equal(["Patient was in a rear-end collision at a red light.", "Neck pain rated 7/10; unable to lift child."],
            selected.Select(passage => passage.Text));
        Assert.All(selected, passage => Assert.Equal("/records/note.txt", passage.FilePath));
    }

    [Fact]
    public void CoversEveryTopicBeforeAddingMoreFromOne()
    {
        PICase piCase = CreateCase(
            "Rear-end collision, collision at speed.",
            "Second rear-end collision description.",
            "Pain 5/10.");

        // Budget fits two passages: the best incident passage and the only pain passage, not two incident ones.
        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 50);

        Assert.Equal(["Rear-end collision, collision at speed.", "Pain 5/10."], selected.Select(passage => passage.Text));
    }

    [Fact]
    public void SpreadsSelectionAcrossFilesInsteadOfOneLongRecord()
    {
        // The note's passages score higher than the ortho passage, but only two passages fit; the second pick
        // should come from another file rather than the same long record.
        PICase piCase = CreateCase("Pain 7/10. Pain. Pain.", "Pain 6/10. Pain. Pain.", "Pain 5/10. Pain. Pain.");
        piCase.Sources = [piCase.Sources[0], new SourceDocument()
        {
            FilePath = "/records/ortho.txt",
            Passages = [new Passage() { StartPosition = 0, Text = "Pain 4/10 noted here." }],
        }];

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 45);

        Assert.Equal(["/records/note.txt", "/records/ortho.txt"], selected.Select(passage => passage.FilePath));
    }

    [Fact]
    public void FillsTheBudgetBeyondTheTopPassagePerTopic()
    {
        PICase piCase = CreateCase("Pain one.", "Pain two.", "Pain three.", "Billing address.");

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 35);

        Assert.Equal(3, selected.Length);
        Assert.DoesNotContain(selected, passage => passage.Text.StartsWith("Billing", StringComparison.Ordinal));
    }

    [Fact]
    public void MatchesWholeWordsOnly()
    {
        PICase piCase = CreateCase("Painting supplies invoice.", "Back pain reported.");

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 30);

        Assert.Equal(["Back pain reported."], selected.Select(passage => passage.Text));
    }

    [Fact]
    public void BoostsPassagesContainingExtractedQuotes()
    {
        SourceDocument[] sources = CreateCase("Pain noted.", "Pain noted again.").Sources;
        int secondStart = sources[0].Passages[1].StartPosition;
        DocumentSummary summary = new DocumentSummary()
        {
            Summary = "summary",
            Citations = [new Citation() { FilePath = "/records/note.txt", CitedText = "Pain noted again.", StartPosition = secondStart }],
        };
        PICase piCase = new PICase([summary]) { Sources = sources };

        RetrievedPassage[] selected = PassageRetriever.Select(piCase, Topics, characterBudget: 20);

        Assert.Equal(["Pain noted again."], selected.Select(passage => passage.Text));
    }

    [Fact]
    public void ReturnsNothingForCasesWithoutStoredSources()
    {
        PICase piCase = new PICase([]);

        Assert.Empty(PassageRetriever.Select(piCase));
    }
}
