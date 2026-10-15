using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM.Providers;

public class DummyLLMProvider : ILLMProvider
{
    public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default)
    {
        return new DocumentSummary()
        {
            Citations = [new Citation() { FilePath = file.FullName, CitedText = "TestCitation", StartPosition = 0 }],
            Summary = "TestDocumentSummary"
        };
    }
    
    public async Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default)
    {
        return new CaseSummary()
        {
            Summary = "TestCaseSummary"
        };
    }

    public async Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default)
    {
        return new DemandDraft()
        {
            Segments =
            [
                new DraftSegment() { Text = "TestDraftNarrative", Citations = piCase.DocumentSummaries.SelectMany(summary => summary.Citations).Take(1).ToArray() },
                new DraftSegment() { Text = ". Claim [CLAIM NUMBER]." },
            ],
        };
    }

    public async Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default)
    {
        return new VerificationResult()
        {
            Contradictions = []
        };
    }

    public Task<ChronologyResult> BuildChronology(PICase piCase, CancellationToken cancellationToken = default)
    {
        Citation[] citations = piCase.DocumentSummaries.SelectMany(summary => summary.Citations).Take(1).ToArray();
        ChronologyEntry entry = new ChronologyEntry()
        {
            Date = new DateOnly(2026, 1, 9),
            Provider = "TestProvider",
            VisitType = "TestVisit",
            Finding = "TestFinding",
            Citations = citations,
        };
        return Task.FromResult(new ChronologyResult() { Entries = citations.Length > 0 ? [entry] : [] });
    }
}
