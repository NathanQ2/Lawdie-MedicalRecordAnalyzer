namespace MedicalRecordAnalysis;

public class Contradiction
{
    public required string Summary { get; init; }
    public required Citation[] Citations { get; init; }

    /**
     * False when no quote supports the contradiction (the reviewer's quote IDs did not resolve). An unverified
     * contradiction is still shown for attorney review, but it is never given to later prompts as a fact.
     */
    public bool IsVerified => Citations.Length > 0;
}
