namespace MedicalRecordAnalysis.LLM;

public class CaseSummary
{
    public required string Summary { get; init; }
    /**
     * Source quotes backing the summary; each citation's Claim is the summary text it supports.
     */
    public Citation[] Citations { get; init; } = [];
    /**
     * Sentences of the summary that no citation supports. These need review before they are relied on.
     */
    public string[] UnsupportedStatements { get; init; } = [];
}
