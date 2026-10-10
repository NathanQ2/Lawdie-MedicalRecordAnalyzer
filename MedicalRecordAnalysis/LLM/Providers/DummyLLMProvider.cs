namespace MedicalRecordAnalysis.LLM.Providers;

public class DummyLLMProvider : ILLMProvider
{
    public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text)
    {
        return new DocumentSummary()
        {
            Citations = [new Citation() { FilePath = file.FullName, CitedText = "TestCitation", StartPosition = 0 }],
            Summary = "TestDocumentSummary"
        };
    }
    
    public async Task<CaseSummary> SummarizeCase(PICase piCase)
    {
        return new CaseSummary()
        {
            Summary = "TestCaseSummary"
        };
    }

    public async Task<DemandDraft> DraftDemand(PICase piCase)
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

    public async Task<VerificationResult> Verify(PICase piCase)
    {
        return new VerificationResult()
        {
            Contradictions = []
        };
    }
}
