using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using Xunit.Abstractions;

namespace MedicalRecordAnalysis.Tests;

public class AzureDocumentIntelligenceLiveTests
{
    private readonly ITestOutputHelper m_output;

    public AzureDocumentIntelligenceLiveTests(ITestOutputHelper output)
    {
        m_output = output;
    }

    [LiveFact(AzureDocumentIntelligenceRecognizer.KeyVariable)]
    public async Task ReadsTheImageOnlyFaxedReferral()
    {
        AzureDocumentIntelligenceRecognizer? recognizer = AzureDocumentIntelligenceRecognizer.FromEnvironment();
        Assert.True(recognizer is not null, $"Set {AzureDocumentIntelligenceRecognizer.EndpointVariable} as well as the key.");
        FileInfo fax = new FileInfo(Path.Combine(AppContext.BaseDirectory, "TestData", "Case_Ellis", "09_Faxed_Referral_2026-02-20.pdf"));

        OCRResult result = await new DirectTextProvider(recognizer).DetectTextFromFile(fax);

        m_output.WriteLine(result.Text);
        m_output.WriteLine($"\nPages: {result.PageStartPositions.Length}; handwritten spans: {result.Recognition.Handwritten.Length}; " +
            $"low-confidence spans: {result.Recognition.LowConfidence.Length}");

        Assert.Contains("foraminal narrowing", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.PageStartPositions.Length, result.Recognition.Recognized.Length);
    }
}
