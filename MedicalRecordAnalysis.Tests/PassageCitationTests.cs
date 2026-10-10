using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class PassageCitationTests
{
    private static readonly RetrievedPassage[] Passages =
    [
        new RetrievedPassage("/records/er.txt", 600, "Denies numbness. Denies loss of consciousness."),
        new RetrievedPassage("/records/pt.txt", 300, "Patient states they briefly lost consciousness."),
    ];

    [Fact]
    public void OffsetsPassageCitationsIntoTheSourceFile()
    {
        Citation? citation = AnthropicProvider.ResolvePassageCitation(Passages, 0, "Denies loss of consciousness.", 17, "denied LOC");

        Assert.NotNull(citation);
        Assert.Equal("/records/er.txt", citation.FilePath);
        Assert.Equal(617, citation.StartPosition);
        Assert.Equal("denied LOC", citation.Claim);
    }

    [Fact]
    public void FindsTheTextWhenTheIndexIsOff()
    {
        Citation? citation = AnthropicProvider.ResolvePassageCitation(Passages, 1, "briefly lost consciousness.", 0, "claim");

        Assert.Equal(300 + "Patient states they ".Length, citation?.StartPosition);
    }

    [Theory]
    [InlineData(-1, "Denies numbness.")]
    [InlineData(2, "Denies numbness.")]
    [InlineData(0, "Text that is not in the passage.")]
    public void DropsCitationsThatCannotBeGrounded(int documentIndex, string citedText)
    {
        Assert.Null(AnthropicProvider.ResolvePassageCitation(Passages, documentIndex, citedText, 0, "claim"));
    }
}
