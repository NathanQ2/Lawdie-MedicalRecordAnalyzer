namespace MedicalRecordAnalysis.OCR;

public class OCRResult
{
    public required FileInfo File { get; init; }
    public required string Text { get; init; }
    /**
     * The offset in Text where each page begins (page 1 first). Empty for sources without pages, such as
     * plain text files, which are treated as a single page.
     */
    public int[] PageStartPositions { get; init; } = [];
}
