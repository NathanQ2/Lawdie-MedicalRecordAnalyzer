using System.Text.Json.Serialization;

namespace MedicalRecordAnalysis.Api.Cases;

public enum JobKind
{
    Ingest,
    Draft,
}

public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/**
 * The status of a case's ingest or draft job. Progress fields are filled in from live progress while it runs.
 */
public class JobStatus
{
    public required string Id { get; set; }
    public required JobKind Kind { get; set; }
    public JobState State { get; set; }
    public PipelineStage? Stage { get; set; }
    public int Completed { get; set; }
    public int Total { get; set; }
    public string? CurrentFile { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    [JsonIgnore]
    public bool IsActive => State is JobState.Queued or JobState.Running;
}

public enum ReviewStatus
{
    Open,
    Resolved,
    Dismissed,
}

/**
 * The attorney's decision on one review item.
 */
public class ReviewState
{
    public ReviewStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/**
 * Everything about a case except its records and results, stored as meta.json in the case folder.
 */
public class CaseMeta
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? RecordsChangedAt { get; set; }
    public DateTimeOffset? AnalysisUpdatedAt { get; set; }
    public DateTimeOffset? DraftUpdatedAt { get; set; }
    public JobStatus? CurrentJob { get; set; }
    public Dictionary<string, ReviewState> Review { get; set; } = [];
}
