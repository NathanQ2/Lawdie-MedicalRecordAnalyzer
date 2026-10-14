using MedicalRecordAnalysis.Api.Cases;

namespace MedicalRecordAnalysis.Api.Endpoints;

public enum RecordStatus
{
    /** Uploaded since the last ingest, or no ingest has run yet. */
    NotIngested,
    /** Read and summarized by the last ingest. */
    Summarized,
    /** The last ingest could not read it (see Reason). */
    Skipped,
    /** Replaced since the last ingest. */
    Changed,
}

public record RecordDto(string Name, long Size, DateTimeOffset ModifiedAt, RecordStatus Status, string? Reason);

public record CaseListItemDto(string Id, string Name, DateOnly? DateOfLoss, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    int RecordCount, bool HasAnalysis, bool HasDraft, JobStatus? Job);

/**
 * A case with its records. The analysis is out of date when records changed after the last ingest, and the
 * draft is out of date when the analysis is newer than it. The analysis counts as reviewed once the attorney
 * has finished reviewing that analysis, not an earlier one.
 */
public record CaseDto(string Id, string Name, DateOnly? DateOfLoss, string? Jurisdiction, string? County, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, IReadOnlyList<RecordDto> Records, DateTimeOffset? AnalysisUpdatedAt, bool AnalysisOutOfDate,
    bool AnalysisReviewed, DateTimeOffset? DraftUpdatedAt, bool DraftOutOfDate, JobStatus? Job);

/**
 * A new case. The intake details are optional so a case can be opened before every detail is known.
 */
public record CreateCaseRequest(string Name, DateOnly? DateOfLoss = null, string? Jurisdiction = null, string? County = null);

/**
 * Replaces the case's name and intake details. Omitted details are cleared.
 */
public record UpdateMatterRequest(string Name, DateOnly? DateOfLoss, string? Jurisdiction, string? County);

public record RenameCaseRequest(string Name);

public record UpdateReviewRequest(ReviewStatus Status, string? Note);

/**
 * A placeholder in the letter and what fills it: the value entered for this case, else the firm's profile. The
 * date of loss comes from the intake details instead. Placeholders nothing fills stay in the letter for review.
 */
public record LetterPlaceholderDto(string Placeholder, string? Value, string? FirmValue, bool FromMatter);

/**
 * Sets values for the letter's placeholders, by placeholder. An empty value clears that placeholder's value;
 * placeholders not listed keep theirs.
 */
public record UpdatePlaceholdersRequest(Dictionary<string, string?> Values);

public record ErrorResponse(string Error);
