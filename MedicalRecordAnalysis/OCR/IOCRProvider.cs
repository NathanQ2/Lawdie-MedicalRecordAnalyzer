namespace MedicalRecordAnalysis.OCR;

public interface IOCRProvider
{
    /**
     * Returns the file's text. Throws NotSupportedException when this provider cannot read the file
     * (e.g. unsupported type, no embedded text), so callers can fall back to another provider.
     */
    public Task<OCRResult> DetectTextFromFile(FileInfo file);
}
