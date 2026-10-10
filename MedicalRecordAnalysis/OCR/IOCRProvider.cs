namespace MedicalRecordAnalysis.OCR;

public interface IOCRProvider
{
    public Task<OCRResult> DetectTextFromFile(FileInfo file);
}
