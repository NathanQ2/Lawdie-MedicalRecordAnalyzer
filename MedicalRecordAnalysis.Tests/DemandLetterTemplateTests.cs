using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class DemandLetterTemplateTests
{
    private static readonly Citation Ems = new Citation() { FilePath = "/records/ems.txt", CitedText = "Rear-ended at a stop.", StartPosition = 10 };

    private static DemandDraft CreateLetter() => DemandLetterTemplate.Wrap(new DemandDraft()
    {
        Segments =
        [
            new DraftSegment() { Text = "On [DATE OF LOSS] the client was " },
            new DraftSegment() { Text = "rear-ended at a stop", Citations = [Ems] },
            new DraftSegment() { Text = "." },
        ],
        UnsupportedStatements = ["On [DATE OF LOSS] the client was rear-ended at a stop."],
    });

    [Fact]
    public void FillsTheDateOfLossFromIntake()
    {
        DemandDraft filled = DemandLetterTemplate.Fill(CreateLetter(), new MatterDetails(new DateOnly(2026, 1, 9), "Washington", "King"));

        Assert.DoesNotContain(DemandLetterTemplate.DateOfLossPlaceholder, filled.Narrative);
        Assert.Contains("Date of Loss: January 9, 2026", filled.Narrative);
        Assert.Contains("injuries sustained on January 9, 2026", filled.Narrative);
        Assert.Equal(["On January 9, 2026 the client was rear-ended at a stop."], filled.UnsupportedStatements);
        Assert.Equal([Ems], filled.Segments.Single(segment => segment.Citations.Length > 0).Citations);
    }

    [Fact]
    public void LeavesThePlaceholderWithoutADateOfLoss()
    {
        DemandDraft letter = CreateLetter();

        Assert.Same(letter, DemandLetterTemplate.Fill(letter, MatterDetails.Empty));
        Assert.Same(letter, DemandLetterTemplate.Fill(letter, new MatterDetails(null, "Washington", "King")));
    }

    [Fact]
    public void LeavesOtherPlaceholdersForReview()
    {
        DemandDraft filled = DemandLetterTemplate.Fill(CreateLetter(), new MatterDetails(new DateOnly(2026, 1, 9), null, null));

        Assert.Contains("[CLAIM NUMBER]", filled.Narrative);
        Assert.Contains("[CLIENT NAME]", filled.Narrative);
    }
}
