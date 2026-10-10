using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;

namespace MedicalRecordAnalysis;

public class MedicalRecordAnalyzer
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
    
    private readonly ILLMProvider m_llmProvider;
    private readonly IOCRProvider m_ocrProvider;

    public MedicalRecordAnalyzer(ILLMProvider? llmProvider = null, IOCRProvider? ocrProvider = null)
    {
        m_llmProvider = llmProvider ?? new AnthropicProvider();
        // Embedded text is read directly. Add an OCR provider after it to also handle scans and images.
        m_ocrProvider = ocrProvider ?? new ChainedOCRProvider([new DirectTextProvider()]);
    }
    
    public void Ingest(DirectoryInfo directory, FileInfo output)
    {
        Console.WriteLine($"DIR: {directory.FullName}");
        
        // Skip hidden files such as .DS_Store.
        FileInfo[] files = directory.GetFiles().Where(file => !file.Attributes.HasFlag(FileAttributes.Hidden)).ToArray();
        List<DocumentSummary> documentSummaries = [];
        
        foreach (FileInfo file in files)
        {
            // 1. Extract text, running OCR if needed
            OCRResult result;
            try
            {
                // TODO: Add proper async/await. GetResult() rethrows the original exception, unlike Wait().
                result = m_ocrProvider.DetectTextFromFile(file).GetAwaiter().GetResult();
            }
            catch (NotSupportedException exception)
            {
                Console.Error.WriteLine($"SKIPPED {file.Name}: {exception.Message}");
                continue;
            }

            // 2. Summarize 
            Task<DocumentSummary> summarizeTask = m_llmProvider.SummarizeDocument(result.File, result.Text);
            summarizeTask.Wait();
            documentSummaries.Add(summarizeTask.Result);
        }

        PICase piCase = new PICase(documentSummaries.Count);
        documentSummaries.CopyTo(piCase.DocumentSummaries);
        
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
