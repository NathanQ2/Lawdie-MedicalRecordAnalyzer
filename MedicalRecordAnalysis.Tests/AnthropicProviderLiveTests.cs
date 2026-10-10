using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using Xunit.Abstractions;

namespace MedicalRecordAnalysis.Tests;

public class AnthropicProviderLiveTests
{
    private const string ApiKeyVariable = "ANTHROPIC_API_KEY";

    private readonly ITestOutputHelper m_output;

    public AnthropicProviderLiveTests(ITestOutputHelper output)
    {
        m_output = output;
    }

    [LiveFact(ApiKeyVariable)]
    public async Task SummarizeDocumentReturnsGroundedCitations()
    {
        FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", "ER_Visit_2026-03-14.txt"));
        string text = await File.ReadAllTextAsync(file.FullName);

        DocumentSummary summary = await new AnthropicProvider().SummarizeDocument(file, text);

        m_output.WriteLine($"SUMMARY:\n{summary.Summary}\n");
        foreach (Citation citation in summary.Citations)
        {
            m_output.WriteLine($"[{citation.StartPosition}] \"{citation.CitedText.Trim()}\"\n    supports: {citation.Claim}\n");
        }

        Assert.False(string.IsNullOrWhiteSpace(summary.Summary));
        Assert.NotEmpty(summary.Citations);
        foreach (Citation citation in summary.Citations)
        {
            Assert.Equal(file.FullName, citation.FilePath);
            Assert.Equal(citation.CitedText, text.Substring(citation.StartPosition, citation.CitationLength));
            Assert.False(string.IsNullOrWhiteSpace(citation.Claim));
        }
    }

    [LiveFact(ApiKeyVariable)]
    public async Task VerifyCitesQuotesForContradictionsBetweenRecords()
    {
        // The PT intake contradicts the ER record on the collision date and loss of consciousness.
        string[] fileNames = ["ER_Visit_2026-03-14.txt", "PT_Intake_2026-03-20.txt"];
        AnthropicProvider provider = new AnthropicProvider();

        PICase piCase = new PICase(fileNames.Length);
        for (int i = 0; i < fileNames.Length; i++)
        {
            FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", fileNames[i]));
            piCase.DocumentSummaries[i] = await provider.SummarizeDocument(file, await File.ReadAllTextAsync(file.FullName));
        }

        VerificationResult result = await provider.Verify(piCase);

        foreach (Contradiction contradiction in result.Contradictions)
        {
            m_output.WriteLine($"CONTRADICTION: {contradiction.Summary}");
            foreach (Citation citation in contradiction.Citations)
            {
                m_output.WriteLine($"    {Path.GetFileName(citation.FilePath)} [{citation.StartPosition}] \"{citation.CitedText.Trim()}\"");
            }
            m_output.WriteLine("");
        }

        Assert.NotEmpty(result.Contradictions);
        Assert.All(result.Contradictions, contradiction => Assert.NotEmpty(contradiction.Citations));
    }
}
