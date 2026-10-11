using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;
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

        List<DocumentSummary> summaries = [];
        foreach (string fileName in fileNames)
        {
            FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
            summaries.Add(await provider.SummarizeDocument(file, await File.ReadAllTextAsync(file.FullName)));
        }

        PICase piCase = new PICase(summaries);

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

    [LiveFact(ApiKeyVariable)]
    public async Task DraftDemandCitesRecordsAndUsesPlaceholders()
    {
        string[] fileNames = ["ER_Visit_2026-03-14.txt", "PT_Intake_2026-03-20.txt"];
        AnthropicProvider provider = new AnthropicProvider();

        Dictionary<string, string> sources = [];
        List<DocumentSummary> summaries = [];
        foreach (string fileName in fileNames)
        {
            FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
            sources[file.FullName] = await File.ReadAllTextAsync(file.FullName);
            summaries.Add(await provider.SummarizeDocument(file, sources[file.FullName]));
        }

        PICase piCase = new PICase(summaries);

        piCase.Contradictions = (await provider.Verify(piCase)).Contradictions;
        piCase.Sources = sources.Select(source => new SourceDocument() { FilePath = source.Key, Passages = PassageSplitter.Split(source.Value) }).ToArray();
        DemandDraft draft = await provider.DraftDemand(piCase, PassageRetriever.Select(piCase));

        m_output.WriteLine(DemandLetterRenderer.Render(draft, piCase, DraftFormat.Markdown));

        Citation[] citations = draft.Segments.SelectMany(segment => segment.Citations).ToArray();
        Assert.NotEmpty(citations);
        foreach (Citation citation in citations)
        {
            Assert.Equal(citation.CitedText, sources[citation.FilePath].Substring(citation.StartPosition, citation.CitationLength));
        }

        // The records contain no claim or insurer details, so the draft should leave placeholders.
        Assert.Matches(@"\[[A-Z][A-Z0-9 &/'.,-]*\]", draft.Narrative);
    }

    [LiveFact(ApiKeyVariable)]
    public async Task SummarizeCaseCitesQuotesAndPresentsDisputedFacts()
    {
        // The records disagree on the collision date (ER implies 03/14/2026, PT intake says 03/12/2026).
        string[] fileNames = ["ER_Visit_2026-03-14.txt", "PT_Intake_2026-03-20.txt"];
        AnthropicProvider provider = new AnthropicProvider();

        Dictionary<string, string> sources = [];
        List<DocumentSummary> summaries = [];
        foreach (string fileName in fileNames)
        {
            FileInfo file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", fileName));
            sources[file.FullName] = await File.ReadAllTextAsync(file.FullName);
            summaries.Add(await provider.SummarizeDocument(file, sources[file.FullName]));
        }

        PICase piCase = new PICase(summaries);

        piCase.Contradictions = (await provider.Verify(piCase)).Contradictions;
        CaseSummary summary = await provider.SummarizeCase(piCase);

        m_output.WriteLine($"CASE SUMMARY:\n{summary.Summary}\n");
        m_output.WriteLine($"CITATIONS: {summary.Citations.Length}");
        m_output.WriteLine($"UNSUPPORTED STATEMENTS ({summary.UnsupportedStatements.Length}):");
        foreach (string statement in summary.UnsupportedStatements)
        {
            m_output.WriteLine($"  - {statement}");
        }

        Assert.NotEmpty(summary.Citations);
        foreach (Citation citation in summary.Citations)
        {
            Assert.Equal(citation.CitedText, sources[citation.FilePath].Substring(citation.StartPosition, citation.CitationLength));
            Assert.False(string.IsNullOrWhiteSpace(citation.Claim));
        }

        // Both versions of the disputed date should appear rather than one being stated as fact.
        Assert.Contains("03/12/2026", summary.Summary);
        Assert.Contains("03/14/2026", summary.Summary);
    }
}
