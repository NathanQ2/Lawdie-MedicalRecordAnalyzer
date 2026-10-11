namespace MedicalRecordAnalysis.OCR;

/**
 * A range of characters in a document's extracted text, in the same coordinates citations use.
 */
public record TextSpan(int StartPosition, int Length)
{
    public int EndPosition => StartPosition + Length;

    public bool Overlaps(int startPosition, int endPosition) => StartPosition < endPosition && startPosition < EndPosition;
}

/**
 * Where a document's text came from OCR rather than from text embedded in the file. OCR can misread characters,
 * so quotes from these ranges are flagged for the attorney to check against the original page; handwriting and
 * words the OCR service was unsure of are tracked separately because they are the most likely to be misread.
 */
public class RecognitionSpans
{
    public TextSpan[] Recognized { get; init; } = [];
    public TextSpan[] Handwritten { get; init; } = [];
    public TextSpan[] LowConfidence { get; init; } = [];
}
