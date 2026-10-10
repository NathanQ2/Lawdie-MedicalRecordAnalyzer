using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM;

public interface ILLMProvider
{
    /**
     * Prompts LLM to summarize the provided docuemnt. The file's name and type is provided as context.
     */
    public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text);
    /**
     * Prompts LLM to summarize the case, using the citations from each individual document. Every statement
     * should cite those quotes; unsupported statements are reported. piCase.Contradictions is populated
     * first so disputed facts are presented as disputed rather than resolved.
     */
    public Task<CaseSummary> SummarizeCase(PICase piCase);
    /**
     * Prompts the LLM to check the document summaries and quotes for contradictions, including within a single
     * document. Any contradiction will be saved. Runs before SummarizeCase.
     */
    public Task<VerificationResult> Verify(PICase piCase);
    /**
     * Prompts the LLM to draft the narrative section of a demand letter from the given source passages, or from
     * the case's extracted quotes when no passages are given. Every factual statement should cite that text,
     * disputed facts are attributed to each record, and facts the records do not contain are left as bracketed
     * placeholders.
     */
    public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages);
}
