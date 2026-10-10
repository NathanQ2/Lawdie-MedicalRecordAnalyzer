using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class LocateCitedTextTests
{
    private const string Source = "Patient reports neck pain. CT shows no fracture. Patient reports neck pain.";

    [Fact]
    public void UsesProvidedIndexWhenItMatches()
    {
        int secondOccurrence = Source.LastIndexOf("Patient reports neck pain.", StringComparison.Ordinal);

        Assert.Equal(secondOccurrence, AnthropicProvider.LocateCitedText(Source, "Patient reports neck pain.", secondOccurrence));
    }

    [Fact]
    public void FallsBackToSearchWhenIndexIsOff()
    {
        Assert.Equal(Source.IndexOf("CT shows", StringComparison.Ordinal), AnthropicProvider.LocateCitedText(Source, "CT shows no fracture.", 3));
    }

    [Fact]
    public void FallsBackToSearchWhenIndexIsOutOfRange()
    {
        Assert.Equal(Source.IndexOf("CT shows", StringComparison.Ordinal), AnthropicProvider.LocateCitedText(Source, "CT shows no fracture.", Source.Length + 10));
    }

    [Fact]
    public void ReturnsNullWhenTextIsNotInSource()
    {
        Assert.Null(AnthropicProvider.LocateCitedText(Source, "Patient has a fractured femur.", 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNullForBlankCitedText(string citedText)
    {
        Assert.Null(AnthropicProvider.LocateCitedText(Source, citedText, 0));
    }
}
