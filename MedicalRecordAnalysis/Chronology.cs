namespace MedicalRecordAnalysis;

/**
 * One visit, test, procedure or other encounter in the records, for the case chronology. The provider writes the
 * date, provider name and wording from the quotes it cites, so ChronologyCheck confirms the date and provider name
 * against the cited document's own text. An entry it can't confirm is flagged for review rather than trusted.
 */
public class ChronologyEntry
{
    public required DateOnly Date { get; init; }
    public required string Provider { get; init; }
    /** What kind of encounter it was, in a few words, such as "ED visit" or "MRI cervical spine". */
    public required string VisitType { get; init; }
    /** The main finding, in one short phrase. */
    public required string Finding { get; init; }
    public required Citation[] Citations { get; init; }
    /** Whether the date appears, in a common written form, in the cited quotes or their document. */
    public bool DateConfirmed { get; set; }
    /** Whether every significant word of the provider's name appears in the cited document. */
    public bool ProviderConfirmed { get; set; }
}

/**
 * A date the records state, with the quotes that state it.
 */
public class CitedDate
{
    public required DateOnly Date { get; init; }
    public required Citation[] Citations { get; init; }
}
