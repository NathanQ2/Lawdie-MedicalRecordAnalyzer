namespace MedicalRecordAnalysis;

public enum PipelineStage
{
    SummarizingDocuments,
    FindingContradictions,
    SummarizingCase,
    BuildingChronology,
    Drafting,
}

/**
 * A progress update from ingest or draft. Completed of Total items are done in the current stage; CurrentFile
 * is the record being worked on while documents are summarized.
 */
public record PipelineProgress(PipelineStage Stage, int Completed, int Total, string? CurrentFile = null);

/**
 * A record ingest could not read, and why (e.g. a scan that needs OCR when no OCR provider is configured).
 */
public class SkippedFile
{
    public required string FilePath { get; init; }
    public required string Reason { get; init; }
}
