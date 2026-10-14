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

    [Fact]
    public void FillsPlaceholdersFromEnteredValues()
    {
        Citation source = new Citation() { FilePath = "/records/er.txt", CitedText = "Neck pain.", StartPosition = 0, Claim = "[CLIENT NAME] reported neck pain" };
        DemandDraft letter = new DemandDraft()
        {
            Segments =
            [
                new DraftSegment() { Text = "[LAW FIRM NAME]\n\n" },
                new DraftSegment() { Text = "[CLIENT NAME] reported neck pain", Citations = [source] },
                new DraftSegment() { Text = ". Claim [CLAIM NUMBER] is open." },
            ],
            UnsupportedStatements = ["Claim [CLAIM NUMBER] is open."],
        };

        DemandDraft filled = DemandLetterTemplate.Fill(letter, MatterDetails.Empty,
            new Dictionary<string, string>() { ["[LAW FIRM NAME]"] = "Reyes Law", ["[CLIENT NAME]"] = "Morgan Ellis" });

        Assert.Equal("Reyes Law\n\nMorgan Ellis reported neck pain. Claim [CLAIM NUMBER] is open.", filled.Narrative);
        Assert.Equal(["Claim [CLAIM NUMBER] is open."], filled.UnsupportedStatements);
        // The source stays attached to the sentence as it now reads, and still quotes the same text.
        Citation filledSource = filled.Segments[1].Citations.Single();
        Assert.Equal("Morgan Ellis reported neck pain", filledSource.Claim);
        Assert.Equal((source.FilePath, source.CitedText, source.StartPosition), (filledSource.FilePath, filledSource.CitedText, filledSource.StartPosition));
    }

    [Fact]
    public void OnlyTheIntakeDetailsSetTheDateOfLoss()
    {
        Dictionary<string, string> values = new Dictionary<string, string>() { [DemandLetterTemplate.DateOfLossPlaceholder] = "last spring" };

        DemandDraft withIntake = DemandLetterTemplate.Fill(CreateLetter(), new MatterDetails(new DateOnly(2026, 1, 9), null, null), values);
        DemandDraft withoutIntake = DemandLetterTemplate.Fill(CreateLetter(), MatterDetails.Empty, values);

        Assert.Contains("Date of Loss: January 9, 2026", withIntake.Narrative);
        Assert.DoesNotContain("last spring", withIntake.Narrative + withoutIntake.Narrative);
        Assert.Contains(DemandLetterTemplate.DateOfLossPlaceholder, withoutIntake.Narrative);
    }

    [Fact]
    public void FirmProfileFillsTheLetterheadAndSignature()
    {
        FirmProfile firm = new FirmProfile(" Reyes Law ", "1 Main St\nSeattle, WA 98101", null, "  ", "Dana Reyes");

        DemandDraft filled = DemandLetterTemplate.Fill(CreateLetter(), MatterDetails.Empty, firm.PlaceholderValues());

        Assert.StartsWith("Reyes Law\n1 Main St\nSeattle, WA 98101\n[FIRM PHONE] | [FIRM EMAIL]\n", filled.Narrative);
        Assert.Contains("Sincerely,\n\nDana Reyes\nReyes Law\n", filled.Narrative);
    }
}
