namespace MedicalRecordAnalysis.OCR;

public class OCRResult
{
    public required FileInfo File { get; init; }
    public required string Text { get; init; }
}
