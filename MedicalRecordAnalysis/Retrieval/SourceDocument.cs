namespace MedicalRecordAnalysis.Retrieval;

/**
 * A paragraph-sized piece of a source file's extracted text. StartPosition is the offset of Text within
 * that extracted text, the same coordinate system citations use.
 */
public class Passage
{
    public required int StartPosition { get; init; }
    public required string Text { get; init; }
}

/**
 * A source file's extracted text, stored as passages so later steps can search it without re-running OCR.
 */
public class SourceDocument
{
    public required string FilePath { get; init; }
    public Passage[] Passages { get; init; } = [];
    /**
     * The offset in the extracted text where each page begins; empty for unpaged sources (a single page).
     */
    public int[] PageStartPositions { get; init; } = [];
}

/**
 * A passage selected for drafting, with the file it came from.
 */
public record RetrievedPassage(string FilePath, int StartPosition, string Text);
