namespace MedicalRecordAnalysis;

/**
 * Facts about the matter that the attorney enters when opening it, rather than facts read from the documents.
 * They fill the letter's placeholders for those facts; anything left empty stays a placeholder for review.
 */
public record MatterDetails(DateOnly? DateOfLoss, string? Jurisdiction, string? County)
{
    public static readonly MatterDetails Empty = new MatterDetails(null, null, null);
}
