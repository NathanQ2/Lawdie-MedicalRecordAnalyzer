using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class ChunkedTextTests
{
    private const string Text = "HISTORY:\nPatient denies LOC. Denies numbness.\nPLAN:\nPhysical therapy.";

    // Chunks: 0 "HISTORY:", 1 "Patient denies LOC.", 2 "Denies numbness.", 3 "PLAN:", 4 "Physical therapy."
    private static readonly AnthropicProvider.ChunkedText Chunked = new AnthropicProvider.ChunkedText("/records/er.txt", Text, 500);

    [Fact]
    public void ResolvesASingleChunkToItsExactSpanInTheFile()
    {
        Citation? citation = Chunked.Resolve(1, 2, "Patient denies LOC.", "denied LOC");

        Assert.NotNull(citation);
        Assert.Equal("Patient denies LOC.", citation.CitedText);
        Assert.Equal(500 + Text.IndexOf("Patient", StringComparison.Ordinal), citation.StartPosition);
        Assert.Equal("/records/er.txt", citation.FilePath);
        Assert.Equal("denied LOC", citation.Claim);
    }

    [Fact]
    public void ResolvesARangeAcrossLinesFromFirstChunkToLast()
    {
        Citation? citation = Chunked.Resolve(2, 4, "Denies numbness.PLAN:", "claim");

        Assert.Equal("Denies numbness.\nPLAN:", citation?.CitedText);
    }

    [Fact]
    public void ToleratesWhitespaceDifferencesInTheCitedText()
    {
        Assert.NotNull(Chunked.Resolve(2, 4, "Denies numbness.\nPLAN:", "claim"));
    }

    [Fact]
    public void TreatsAnEmptyRangeAsTheStartChunk()
    {
        Assert.Equal("PLAN:", Chunked.Resolve(3, 3, "PLAN:", "claim")?.CitedText);
    }

    [Theory]
    [InlineData(1, 2, "Patient reports loss of consciousness.")]
    [InlineData(-1, 0, "HISTORY:")]
    [InlineData(4, 6, "Physical therapy.")]
    public void DropsCitationsThatDoNotLineUpWithTheSource(int start, int end, string citedText)
    {
        Assert.Null(Chunked.Resolve(start, end, citedText, "claim"));
    }
}
