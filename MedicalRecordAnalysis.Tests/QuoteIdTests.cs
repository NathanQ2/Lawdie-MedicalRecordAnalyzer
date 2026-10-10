using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class QuoteIdTests
{
    private static PICase CreateCase()
    {
        PICase piCase = new PICase(2);
        piCase.DocumentSummaries[0] = new DocumentSummary()
        {
            Summary = "ER visit.",
            Citations =
            [
                new Citation() { FilePath = "/er.txt", CitedText = "PHYSICAL EXAM:\nCervical tenderness.", StartPosition = 0 },
                new Citation() { FilePath = "/er.txt", CitedText = "Denies loss of consciousness.", StartPosition = 40 },
            ],
        };
        piCase.DocumentSummaries[1] = new DocumentSummary()
        {
            Summary = "PT intake.",
            Citations = [new Citation() { FilePath = "/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 10 }],
        };
        return piCase;
    }

    [Fact]
    public void ContextNumbersQuotesAcrossDocumentsOnOneLineEach()
    {
        string context = AnthropicProvider.BuildCaseContext(CreateCase());

        Assert.Contains("[Q1] \"PHYSICAL EXAM:\\nCervical tenderness.\"", context);
        Assert.Contains("[Q2] \"Denies loss of consciousness.\"", context);
        Assert.Contains("[Q3] \"Briefly lost consciousness.\"", context);
    }

    [Fact]
    public void QuoteIdsResolveToTheCitationsTheyWereRenderedFor()
    {
        PICase piCase = CreateCase();
        List<Citation> quotes = AnthropicProvider.NumberQuotes(piCase);

        Citation[] resolved = AnthropicProvider.ResolveQuoteIds(quotes, ["Q2", "Q3"]);

        Assert.Equal([piCase.DocumentSummaries[0].Citations[1], piCase.DocumentSummaries[1].Citations[0]], resolved);
    }

    [Fact]
    public void ResolveAcceptsLooseIdFormatting()
    {
        List<Citation> quotes = AnthropicProvider.NumberQuotes(CreateCase());

        Assert.Equal(3, AnthropicProvider.ResolveQuoteIds(quotes, ["[Q1]", " q2 ", "3"]).Length);
    }

    [Fact]
    public void ResolveDropsUnknownMalformedAndDuplicateIds()
    {
        List<Citation> quotes = AnthropicProvider.NumberQuotes(CreateCase());

        Citation[] resolved = AnthropicProvider.ResolveQuoteIds(quotes, ["Q0", "Q4", "Qx", "", "Q1", "Q1"]);

        Assert.Equal([quotes[0]], resolved);
    }
}
