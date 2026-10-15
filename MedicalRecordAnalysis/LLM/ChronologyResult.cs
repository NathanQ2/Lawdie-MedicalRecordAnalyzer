namespace MedicalRecordAnalysis.LLM;

public class ChronologyResult
{
    public ChronologyEntry[] Entries { get; init; } = [];
    /** The date of the incident, when the records state it. */
    public CitedDate? DateOfLoss { get; init; }
}
