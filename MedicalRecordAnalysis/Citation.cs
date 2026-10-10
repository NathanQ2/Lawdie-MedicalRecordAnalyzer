namespace MedicalRecordAnalysis;

public class Citation
{
    public required string FilePath { get; init; }
    public required string CitedText { get; init; }
    public required int StartPosition { get; init; }
    /**
     * The generated statement this citation supports, if the provider links claims to sources.
     */
    public string? Claim { get; init; }
    /**
     * The 1-based page where the cited text starts, and where it ends if that is a later page. Unset for
     * citations whose source pages are unknown, such as exports made before pages were tracked.
     */
    public int? PageNumber { get; set; }
    public int? EndPageNumber { get; set; }
    public int CitationLength => CitedText.Length;
}
