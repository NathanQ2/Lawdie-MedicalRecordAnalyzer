namespace MedicalRecordAnalysis.LLM.Providers;

public class AnthropicProvider : ILLMProvider
{
    public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text) => throw new NotImplementedException();
    public Task<CaseSummary> SummarizeCase(PICase piCase) => throw new NotImplementedException();
    public Task<VerificationResult> Verify(PICase piCase) => throw new NotImplementedException();
}
