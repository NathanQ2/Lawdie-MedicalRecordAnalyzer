namespace MedicalRecordAnalysis;

public class Contradiction
{
    public required string Summary { get; init; }
    public required Citation[] Citations { get; init; }

    /**
     * True when at least two different quotes support the contradiction. A conflict needs two statements, and the
     * reviewer sometimes cites a single quote, or the same quote twice, for a conflict the records don't contain.
     * An unverified contradiction is still shown for attorney review, but it is never given to later prompts as a fact.
     */
    public bool IsVerified => Citations.DistinctBy(citation => (citation.FilePath, citation.StartPosition, citation.CitationLength)).Count() >= 2;
}
