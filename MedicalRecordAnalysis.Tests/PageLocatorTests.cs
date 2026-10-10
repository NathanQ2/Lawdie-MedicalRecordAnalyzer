using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class PageLocatorTests
{
    private static readonly int[] PageStarts = [0, 100, 250];

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(249, 2)]
    [InlineData(250, 3)]
    [InlineData(10_000, 3)]
    public void FindsThePageContainingAPosition(int position, int expectedPage)
    {
        Assert.Equal(expectedPage, PageLocator.PageAt(PageStarts, position));
    }

    [Fact]
    public void UnpagedSourcesAreOnePage()
    {
        Assert.Equal(1, PageLocator.PageAt([], 5_000));
    }

    [Fact]
    public void AssignsStartAndEndPagesToCitations()
    {
        Citation onePage = new Citation() { FilePath = "/records/a.pdf", CitedText = "Within page two.", StartPosition = 120 };
        Citation spanning = new Citation() { FilePath = "/records/a.pdf", CitedText = new string('x', 60), StartPosition = 220 };
        Citation unpaged = new Citation() { FilePath = "/records/b.txt", CitedText = "Text file.", StartPosition = 900 };
        Citation unknown = new Citation() { FilePath = "/records/missing.txt", CitedText = "Not ingested.", StartPosition = 0 };
        SourceDocument[] sources =
        [
            new SourceDocument() { FilePath = "/records/a.pdf", PageStartPositions = PageStarts },
            new SourceDocument() { FilePath = "/records/b.txt" },
        ];

        PageLocator.AssignPages([onePage, spanning, unpaged, unknown], sources);

        Assert.Equal((2, (int?)null), (onePage.PageNumber!.Value, onePage.EndPageNumber));
        Assert.Equal((2, (int?)3), (spanning.PageNumber!.Value, spanning.EndPageNumber));
        Assert.Equal(1, unpaged.PageNumber);
        Assert.Null(unknown.PageNumber);
    }
}
