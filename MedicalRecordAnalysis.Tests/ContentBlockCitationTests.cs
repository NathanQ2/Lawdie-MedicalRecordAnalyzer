using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class ContentBlockCitationTests
{
    private static readonly IReadOnlyList<IReadOnlyList<Citation>> DocumentQuotes =
    [
        [
            new Citation() { FilePath = "/er.txt", CitedText = "Denies loss of consciousness.", StartPosition = 657 },
            new Citation() { FilePath = "/er.txt", CitedText = "No acute fracture.", StartPosition = 1064 },
        ],
        [
            new Citation() { FilePath = "/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 319 },
        ],
    ];

    [Fact]
    public void ResolvesBlockRangeToQuotesWithTheClaim()
    {
        Citation[] resolved = Resolve(documentIndex: 0, start: 0, end: 2, claim: "ED findings");

        Assert.Equal(["Denies loss of consciousness.", "No acute fracture."], resolved.Select(citation => citation.CitedText));
        Assert.All(resolved, citation => Assert.Equal("ED findings", citation.Claim));
        Assert.Equal(1064, resolved[1].StartPosition);
        Assert.Equal("/er.txt", resolved[1].FilePath);
    }

    [Fact]
    public void ResolvesQuotesFromTheRightDocument()
    {
        Citation[] resolved = Resolve(documentIndex: 1, start: 0, end: 1, claim: "PT history");

        Assert.Equal("/pt.txt", Assert.Single(resolved).FilePath);
    }

    [Fact]
    public void TreatsEmptyRangeAsTheStartBlock()
    {
        Assert.Single(Resolve(documentIndex: 0, start: 1, end: 1, claim: "claim"));
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(1, 5, 6)]
    public void IgnoresOutOfRangeLocations(int documentIndex, int start, int end)
    {
        Assert.Empty(Resolve(documentIndex, start, end, "claim"));
    }

    private static Citation[] Resolve(int documentIndex, int start, int end, string claim) =>
        AnthropicProvider.ResolveContentBlockCitation(DocumentQuotes, documentIndex, start, end, claim);
}
