using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis;

public class MedicalRecordAnalyzer
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
    
    private ILLMProvider m_llmProvider = new AnthropicProvider();
    private IOCRProvider m_ocrProvider = new DummyOCRProvider();
    
    public void Ingest(DirectoryInfo directory, FileInfo output)
    {
        Console.WriteLine($"DIR: {directory.FullName}");
        
        FileInfo[] files = directory.GetFiles();
        PICase piCase = new PICase(files.Length);
        
        for (int i = 0; i < piCase.DocumentSummaries.Length; i++)
        {
            // 1. Run OCR on file
            Task<OCRResult> ocrTask = m_ocrProvider.DetectTextFromFile(files[i]);
            // TODO: Add proper async/await
            ocrTask.Wait();
            OCRResult result = ocrTask.Result;

            // 2. Summarize 
            Task<DocumentSummary> summarizeTask = m_llmProvider.SummarizeDocument(result.File, result.Text);
            summarizeTask.Wait();
            piCase.DocumentSummaries[i] = summarizeTask.Result;
        }
        
        // 3. Summarize Case
        Task<CaseSummary> caseSummarizeTask = m_llmProvider.SummarizeCase(piCase);
        caseSummarizeTask.Wait();
        piCase.Summary = caseSummarizeTask.Result;
        
        // 4. Verify facts
        Task<VerificationResult> verificationTask = m_llmProvider.Verify(piCase);
        verificationTask.Wait();
        piCase.Contradictions = verificationTask.Result.Contradictions;
        
        // 5. Export
        using StreamWriter sw = output.CreateText();
        string jsonString = JsonSerializer.Serialize(piCase, JsonSerializerOptions);
        sw.Write(jsonString);
        sw.Flush();
    }

    public void Draft(FileInfo file, FileInfo output)
    {
        
    }
}
