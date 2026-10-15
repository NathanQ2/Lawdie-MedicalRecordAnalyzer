using System.Globalization;

namespace MedicalRecordAnalysis;

/**
 * An intake detail the documents contradict, such as a different date of loss. The letter uses the intake details,
 * so a contradicted one would reach the adjuster unless the attorney resolves it; the review checklist and the
 * attorney review section of every export list it, with the quotes that state the documents' version.
 */
public record IntakeMismatch(string Summary, Citation[] Citations, string IntakeText)
{
    /**
     * The intake details the analysis contradicts. Details the attorney left empty, or the documents don't state,
     * have nothing to compare and are never reported.
     */
    public static IReadOnlyList<IntakeMismatch> Find(PICase? piCase, MatterDetails? matter)
    {
        if (matter?.DateOfLoss is DateOnly intakeDate && piCase?.RecordedDateOfLoss is CitedDate recorded && recorded.Date != intakeDate)
        {
            string intakeText = LongDate(intakeDate);
            return
            [
                new IntakeMismatch($"The matter details give the date of loss as {intakeText}, but the documents give {LongDate(recorded.Date)}.",
                    recorded.Citations, intakeText),
            ];
        }

        return [];
    }

    /** A date as the letter writes it, such as "January 9, 2026". */
    public static string LongDate(DateOnly date) => date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
}
