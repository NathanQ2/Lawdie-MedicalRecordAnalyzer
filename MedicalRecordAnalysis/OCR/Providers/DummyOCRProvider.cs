namespace MedicalRecordAnalysis.OCR.Providers;

public class DummyOCRProvider : IOCRProvider
{
    public async Task<OCRResult> DetectTextFromFile(FileInfo file)
    {
        return new OCRResult()
        {
            File = file,
            Text = "Test OCR Text"
        };
    }
}
