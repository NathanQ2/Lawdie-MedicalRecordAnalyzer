using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class GroundingCheckTests
{
    [Fact]
    public void FullyCitedTextHasNoUnsupportedSentences()
    {
        GeneratedSegment[] segments =
        [
            new GeneratedSegment("The patient ", false),
            new GeneratedSegment("denied loss of consciousness", true),
            new GeneratedSegment(". The CT showed ", false),
            new GeneratedSegment("no acute fracture", true),
            new GeneratedSegment(".", false),
        ];

        Assert.Empty(GroundingCheck.FindUnsupportedSentences(segments));
    }

    [Fact]
    public void FlagsSentencesWithNoCitation()
    {
        GeneratedSegment[] segments =
        [
            new GeneratedSegment("The patient ", false),
            new GeneratedSegment("denied loss of consciousness", true),
            new GeneratedSegment(". The patient will need surgery. Recovery is expected!\nThe CT showed ", false),
            new GeneratedSegment("no acute fracture", true),
            new GeneratedSegment(".", false),
        ];

        Assert.Equal(["The patient will need surgery.", "Recovery is expected!"], GroundingCheck.FindUnsupportedSentences(segments));
    }

    [Fact]
    public void DoesNotSplitOnAbbreviationsOrDecimals()
    {
        GeneratedSegment[] segments =
        [
            new GeneratedSegment("The patient was seen by ", false),
            new GeneratedSegment("Dr. Priya Raman", true),
            new GeneratedSegment(" and prescribed 7.5 mg.", false),
        ];

        Assert.Empty(GroundingCheck.FindUnsupportedSentences(segments));
    }

    [Fact]
    public void IgnoresWhitespaceAndPunctuationOnlyText()
    {
        GeneratedSegment[] segments =
        [
            new GeneratedSegment("Neck pain was rated 7/10", true),
            new GeneratedSegment(".\n\n - ", false),
        ];

        Assert.Empty(GroundingCheck.FindUnsupportedSentences(segments));
    }

    [Fact]
    public void FlagsEverythingWhenNothingIsCited()
    {
        GeneratedSegment[] segments = [new GeneratedSegment("The collision occurred on 03/12/2026.", false)];

        Assert.Equal(["The collision occurred on 03/12/2026."], GroundingCheck.FindUnsupportedSentences(segments));
    }
}
