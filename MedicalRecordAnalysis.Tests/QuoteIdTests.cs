using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class QuoteIdTests
{
    private static PICase CreateCase()
    {
        DocumentSummary er = new DocumentSummary()
        {
            Summary = "ER visit.",
            Citations =
            [
                new Citation() { FilePath = "/er.txt", CitedText = "PHYSICAL EXAM:\nCervical tenderness.", StartPosition = 0 },
                new Citation() { FilePath = "/er.txt", CitedText = "Denies loss of consciousness.", StartPosition = 40 },
            ],
        };
        DocumentSummary pt = new DocumentSummary()
        {
            Summary = "PT intake.",
            Citations = [new Citation() { FilePath = "/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 10 }],
        };
        return new PICase([er, pt]);
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
    public void ContextLabelsDocumentsWithTheirFileNames()
    {
        string context = AnthropicProvider.BuildCaseContext(CreateCase());

        Assert.Contains("DOCUMENT 1: er.txt", context);
        Assert.Contains("DOCUMENT 2: pt.txt", context);
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

    [Fact]
    public void GroundedInstructionLeavesOutUnverifiedContradictions()
    {
        PICase piCase = CreateCase();
        List<Citation> quotes = AnthropicProvider.NumberQuotes(piCase);
        piCase.Contradictions =
        [
            new Contradiction() { Summary = "LOC conflicts.", Citations = [quotes[1], quotes[2]] },
            new Contradiction() { Summary = "Dates conflict.", Citations = [] },
        ];

        string instruction = AnthropicProvider.BuildGroundedInstruction(piCase, "Write the summary.");

        Assert.False(piCase.Contradictions[1].IsVerified);
        Assert.Contains("LOC conflicts.", instruction);
        Assert.DoesNotContain("Dates conflict.", instruction);
    }

    [Fact]
    public void GroundedInstructionReportsNoContradictionsWhenNoneAreVerified()
    {
        PICase piCase = CreateCase();
        piCase.Contradictions = [new Contradiction() { Summary = "Dates conflict.", Citations = [] }];

        string instruction = AnthropicProvider.BuildGroundedInstruction(piCase, "Write the summary.");

        Assert.StartsWith("A separate review found no contradictions", instruction);
        Assert.DoesNotContain("Dates conflict.", instruction);
    }
}
