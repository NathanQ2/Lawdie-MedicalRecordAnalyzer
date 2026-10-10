namespace MedicalRecordAnalysis.LLM;

public class DocumentSummary
{
    public required string Summary { get; init; }
    public Citation[] Citations { get; init; } = [];
}
