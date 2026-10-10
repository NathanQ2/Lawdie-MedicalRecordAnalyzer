namespace MedicalRecordAnalysis.OCR.Providers;

public class TextractProvider : IOCRProvider
{
    public async Task<OCRResult> DetectTextFromFile(FileInfo file)
    {
        return new OCRResult() { File = file, Text = "Hello" };
    }
}
