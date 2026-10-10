using System.Text.Json.Serialization;

namespace MedicalRecordAnalysis.LLM;

/**
 * A piece of the drafted narrative and the source quotes that support it (empty if uncited).
 */
public class DraftSegment
{
    public required string Text { get; init; }
    public Citation[] Citations { get; init; } = [];
}

/**
 * The demand-letter narrative section, kept as segments so each claim stays linked to its sources.
 */
public class DemandDraft
{
    public required DraftSegment[] Segments { get; init; }
    /**
     * Sentences of the narrative that no citation supports. These need review before the letter is sent.
     */
    public string[] UnsupportedStatements { get; init; } = [];

    [JsonIgnore]
    public string Narrative => string.Concat(Segments.Select(segment => segment.Text));
}
