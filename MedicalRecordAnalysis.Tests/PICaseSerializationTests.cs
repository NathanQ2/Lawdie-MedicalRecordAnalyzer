using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class PICaseSerializationTests
{
    [Fact]
    public async Task CaseWithCitationsSerializesToJson()
    {
        DummyLLMProvider provider = new DummyLLMProvider();
        FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", "ER_Visit_2026-03-14.txt"));

        PICase piCase = new PICase([await provider.SummarizeDocument(file, "text")]);
        piCase.Summary = await provider.SummarizeCase(piCase);
        piCase.Contradictions = [new Contradiction() { Summary = "test", Citations = piCase.DocumentSummaries[0].Citations }];

        string json = JsonSerializer.Serialize(piCase);

        Assert.Contains(JsonSerializer.Serialize(file.FullName), json);
    }

    [Fact]
    public void ExportedCaseReadsBackWithAllFields()
    {
        Citation quote = new Citation() { FilePath = "/er.txt", CitedText = "Denies loss of consciousness.", StartPosition = 657, Claim = "denied LOC" };
        PICase original = new PICase([new DocumentSummary() { Summary = "ER visit.", Citations = [quote] }]);
        original.Summary = new CaseSummary() { Summary = "Case.", Citations = [quote], UnsupportedStatements = ["Uncited."] };
        original.Contradictions = [new Contradiction() { Summary = "LOC differs.", Citations = [quote] }];

        PICase? copy = JsonSerializer.Deserialize<PICase>(JsonSerializer.Serialize(original));

        Assert.NotNull(copy);
        DocumentSummary document = Assert.Single(copy.DocumentSummaries);
        Assert.Equal("ER visit.", document.Summary);
        Citation citation = Assert.Single(document.Citations);
        Assert.Equal(("/er.txt", "Denies loss of consciousness.", 657, "denied LOC"), (citation.FilePath, citation.CitedText, citation.StartPosition, citation.Claim));
        Assert.Equal("Case.", copy.Summary!.Summary);
        Assert.Equal(["Uncited."], copy.Summary.UnsupportedStatements);
        Assert.Single(copy.Summary.Citations);
        Assert.Equal("LOC differs.", Assert.Single(copy.Contradictions).Summary);
    }
}
