namespace MedicalRecordAnalysis.LLM;

public interface ILLMProvider
{
    /**
     * Prompts LLM to summarize the provided docuemnt. The file's name and type is provided as context.
     */
    public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text);
    /**
     * Prompts LLM to summarize the case, using the summaries and citations from each individual document.
     */
    public Task<CaseSummary> SummarizeCase(PICase piCase);
    /**
     * Prompts the LLM to verify the facts in its case summary and documen summaries. Any contradiction will be saved.
     */
    public Task<VerificationResult> Verify(PICase piCase);
}
