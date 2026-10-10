using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class PassageSplitterTests
{
    [Fact]
    public void EveryPassageIsAnExactSubstringAtItsPosition()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ER_Visit_2026-03-14.txt"));

        Passage[] passages = PassageSplitter.Split(text, targetLength: 200, maxLength: 400);

        Assert.True(passages.Length > 1);
        Assert.All(passages, passage => Assert.Equal(passage.Text, text.Substring(passage.StartPosition, passage.Text.Length)));
        Assert.All(passages, passage => Assert.True(passage.Text.Length <= 400));
    }

    [Fact]
    public void PassagesCoverAllNonWhitespaceTextInOrder()
    {
        string text = "HISTORY:\nNeck pain.\n\n\nEXAM:\nTender C3-C6.\n\nPLAN:\nPhysical therapy.";

        Passage[] passages = PassageSplitter.Split(text, targetLength: 10, maxLength: 100);

        Assert.Equal(["HISTORY:\nNeck pain.", "EXAM:\nTender C3-C6.", "PLAN:\nPhysical therapy."], passages.Select(passage => passage.Text));
        Assert.Equal(passages.Select(passage => passage.StartPosition).Order(), passages.Select(passage => passage.StartPosition));
    }

    [Fact]
    public void MergesShortParagraphsUpToTheTargetLength()
    {
        string text = "One.\n\nTwo.\n\nThree.\n\nFour.";

        Passage passage = Assert.Single(PassageSplitter.Split(text, targetLength: 100, maxLength: 200));

        Assert.Equal(text, passage.Text);
    }

    [Fact]
    public void SplitsLongParagraphsByLineAndThenByLength()
    {
        string longLine = new string('x', 250);
        string text = $"line one\nline two\n{longLine}";

        Passage[] passages = PassageSplitter.Split(text, targetLength: 10, maxLength: 100);

        Assert.All(passages, passage => Assert.True(passage.Text.Length <= 100));
        Assert.Equal(text.Replace("\n", ""), string.Concat(passages.Select(passage => passage.Text.Replace("\n", ""))));
    }

    [Fact]
    public void EmptyTextHasNoPassages()
    {
        Assert.Empty(PassageSplitter.Split("  \n\n  "));
    }
}
