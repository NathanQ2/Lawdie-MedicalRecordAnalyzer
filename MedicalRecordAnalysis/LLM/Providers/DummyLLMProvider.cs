namespace MedicalRecordAnalysis.LLM.Providers;

public class DummyLLMProvider : ILLMProvider
{
    public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text)
    {
        return new DocumentSummary()
        {
            Citations = [new Citation() { File = file, CitedText = "TestCitation", StartPosition = 0 }],
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

    public async Task<VerificationResult> Verify(PICase piCase)
    {
        return new VerificationResult()
        {
            Contradictions = []
        };
    }
}
