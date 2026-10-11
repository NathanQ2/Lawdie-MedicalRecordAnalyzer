namespace MedicalRecordAnalysis.OCR;

/**
 * An OCR service that reads the text on selected pages of a PDF or image. DirectTextProvider uses it only for pages
 * with no embedded text, so digital pages keep their exact text and only scans are sent for recognition.
 */
public interface IPageRecognizer
{
    /**
     * Returns the text of each requested 1-based page, or of every page when pageNumbers is null (for images).
     * A requested page missing from the result is treated as unreadable, never as blank.
     */
    public Task<RecognizedPage[]> RecognizePages(FileInfo file, IReadOnlyList<int>? pageNumbers, CancellationToken cancellationToken = default);
}

/**
 * The recognized text of one page. Span positions are offsets into Text.
 */
public class RecognizedPage
{
    public required int PageNumber { get; init; }
    public required string Text { get; init; }
    public TextSpan[] HandwrittenSpans { get; init; } = [];
    public TextSpan[] LowConfidenceSpans { get; init; } = [];
}
