using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

public class ChronologyTests
{
    private const string PcpText = "NORTHGATE FAMILY MEDICINE - OFFICE VISIT\nDate of Visit: 01/13/2026    Provider: Alan Brooks, MD\n" +
        "Patient was rear-ended by a box truck on 01/09/2026 while stopped on I-5.\nNeck pain rated 7/10.";

    private static readonly SourceDocument Pcp = new SourceDocument()
    {
        FilePath = "/r/pcp.txt",
        Passages = [new Passage() { StartPosition = 0, Text = PcpText }],
    };

    private static Citation Quote(string text) => new Citation() { FilePath = Pcp.FilePath, CitedText = text, StartPosition = PcpText.IndexOf(text, StringComparison.Ordinal) };

    private static ChronologyEntry Entry(DateOnly date, string provider, params Citation[] citations) => new ChronologyEntry()
    {
        Date = date,
        Provider = provider,
        VisitType = "PCP visit",
        Finding = "Neck pain 7/10",
        Citations = citations,
    };

    [Theory]
    [InlineData("Date of Visit: 01/13/2026")]
    [InlineData("seen on 1/13/2026 for follow-up")]
    [InlineData("seen on 1/13/26")]
    [InlineData("January 13, 2026")]
    [InlineData("Jan 13, 2026")]
    [InlineData("13 January 2026")]
    [InlineData("DOS 2026-01-13.")]
    [InlineData("January\n13, 2026")]
    public void FindsADateInItsCommonWrittenForms(string text)
    {
        Assert.True(ChronologyCheck.ContainsDate(text, new DateOnly(2026, 1, 13)));
    }

    [Theory]
    [InlineData("seen on 11/13/2026")]
    [InlineData("seen on 1/13/20261")]
    [InlineData("seen on 01/14/2026")]
    public void DoesNotFindADateInsideOtherNumbers(string text)
    {
        Assert.False(ChronologyCheck.ContainsDate(text, new DateOnly(2026, 1, 13)));
    }

    [Fact]
    public void ConfirmsANameByItsSignificantWords()
    {
        const string text = "Arrived at the Mercy General Hospital Emergency Department by ambulance.";

        Assert.True(ChronologyCheck.ContainsName(text, "Mercy General ED"));
        Assert.True(ChronologyCheck.ContainsName(text, "mercy general hospital"));
        Assert.False(ChronologyCheck.ContainsName(text, "Summit Imaging Center"));
        Assert.False(ChronologyCheck.ContainsName(text, "ED"));
    }

    [Fact]
    public void ConfirmsDatesAndProvidersAgainstTheCitedDocument()
    {
        Citation pain = Quote("Neck pain rated 7/10.");
        ChronologyResult checkedResult = ChronologyCheck.Apply(new ChronologyResult()
        {
            Entries =
            [
                Entry(new DateOnly(2026, 1, 13), "Northgate Family Medicine", pain),
                Entry(new DateOnly(2026, 1, 14), "Riverbend Pain Specialists", pain),
            ],
        }, [Pcp]);

        // The quote has no date, but the document states 01/13/2026, so the date is confirmed from the document.
        ChronologyEntry visit = checkedResult.Entries[0];
        Assert.True(visit.DateConfirmed);
        Assert.True(visit.ProviderConfirmed);

        ChronologyEntry wrong = checkedResult.Entries[1];
        Assert.False(wrong.DateConfirmed);
        Assert.False(wrong.ProviderConfirmed);
    }

    [Fact]
    public void DropsUncitedEntriesAndDuplicatesAndSortsByDate()
    {
        Citation pain = Quote("Neck pain rated 7/10.");
        ChronologyResult checkedResult = ChronologyCheck.Apply(new ChronologyResult()
        {
            Entries =
            [
                Entry(new DateOnly(2026, 2, 27), "Northgate Family Medicine", pain),
                Entry(new DateOnly(2026, 1, 13), "Northgate Family Medicine", pain),
                Entry(new DateOnly(2026, 1, 13), "northgate family medicine", pain),
                Entry(new DateOnly(2026, 1, 20), "Valley Rehab"),
            ],
        }, [Pcp]);

        Assert.Equal([new DateOnly(2026, 1, 13), new DateOnly(2026, 2, 27)], checkedResult.Entries.Select(entry => entry.Date));
    }

    [Fact]
    public void KeepsTheDateOfLossOnlyWhenItsQuotesStateIt()
    {
        Citation collision = Quote("Patient was rear-ended by a box truck on 01/09/2026 while stopped on I-5.");
        Citation pain = Quote("Neck pain rated 7/10.");

        CitedDate? stated = ChronologyCheck.Apply(new ChronologyResult()
        {
            DateOfLoss = new CitedDate() { Date = new DateOnly(2026, 1, 9), Citations = [collision] },
        }, [Pcp]).DateOfLoss;
        CitedDate? unstated = ChronologyCheck.Apply(new ChronologyResult()
        {
            DateOfLoss = new CitedDate() { Date = new DateOnly(2026, 1, 9), Citations = [pain] },
        }, [Pcp]).DateOfLoss;

        Assert.Equal(new DateOnly(2026, 1, 9), stated?.Date);
        Assert.Null(unstated);
    }

    [Fact]
    public void ParsesEntriesAndTheDateOfLossByQuoteId()
    {
        Citation[] quotes = [Quote("Date of Visit: 01/13/2026"), Quote("Patient was rear-ended by a box truck on 01/09/2026 while stopped on I-5.")];
        const string response = "Here is the chronology: {\"dateOfLoss\":{\"date\":\"2026-01-09\",\"quoteIds\":[\"Q2\"]},\"entries\":[" +
            "{\"date\":\"2026-01-13\",\"provider\":\"Northgate Family Medicine\",\"visitType\":\"PCP visit\",\"finding\":\"Neck pain 7/10\",\"quoteIds\":[\"Q1\"]}," +
            "{\"date\":\"01/20/2026\",\"provider\":\"Valley Rehab\",\"visitType\":\"PT evaluation\",\"finding\":\"\",\"quoteIds\":[\"Q1\"]}," +
            "{\"date\":\"2026-02-10\",\"provider\":\"Summit Imaging\",\"visitType\":\"MRI\",\"finding\":\"\",\"quoteIds\":[\"Q9\"]}]}";

        ChronologyResult result = AnthropicProvider.ParseChronology(response, quotes);

        ChronologyEntry entry = Assert.Single(result.Entries);
        Assert.Equal(new DateOnly(2026, 1, 13), entry.Date);
        Assert.Equal("Northgate Family Medicine", entry.Provider);
        Assert.Equal([quotes[0]], entry.Citations);
        Assert.Equal(new DateOnly(2026, 1, 9), result.DateOfLoss?.Date);
        Assert.Equal([quotes[1]], result.DateOfLoss?.Citations);
    }

    [Fact]
    public void ParsesANullDateOfLoss()
    {
        ChronologyResult result = AnthropicProvider.ParseChronology("{\"dateOfLoss\":null,\"entries\":[]}", []);

        Assert.Null(result.DateOfLoss);
        Assert.Empty(result.Entries);
    }
}
