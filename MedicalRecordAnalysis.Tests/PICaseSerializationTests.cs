using System.Text.Json;
using MedicalRecordAnalysis.LLM.Providers;

namespace MedicalRecordAnalysis.Tests;

public class PICaseSerializationTests
{
    [Fact]
    public async Task CaseWithCitationsSerializesToJson()
    {
        DummyLLMProvider provider = new DummyLLMProvider();
        FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", "ER_Visit_2026-03-14.txt"));

        PICase piCase = new PICase(1);
        piCase.DocumentSummaries[0] = await provider.SummarizeDocument(file, "text");
        piCase.Summary = await provider.SummarizeCase(piCase);
        piCase.Contradictions = [new Contradiction() { Summary = "test", Citations = piCase.DocumentSummaries[0].Citations }];

        string json = JsonSerializer.Serialize(piCase);

        Assert.Contains(JsonSerializer.Serialize(file.FullName), json);
    }
}
