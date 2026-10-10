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
    
    public async Task Ingest(DirectoryInfo directory, FileInfo output)
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
                result = await m_ocrProvider.DetectTextFromFile(file);
            }
            catch (NotSupportedException exception)
            {
                Console.Error.WriteLine($"SKIPPED {file.Name}: {exception.Message}");
                continue;
            }

            // 2. Summarize 
            documentSummaries.Add(await m_llmProvider.SummarizeDocument(result.File, result.Text));
        }

        PICase piCase = new PICase(documentSummaries.Count);
        documentSummaries.CopyTo(piCase.DocumentSummaries);
        
        // 3. Summarize Case
        piCase.Summary = await m_llmProvider.SummarizeCase(piCase);
        
        // 4. Verify facts
        VerificationResult verification = await m_llmProvider.Verify(piCase);
        piCase.Contradictions = verification.Contradictions;
        
        // 5. Export
        await using FileStream stream = output.Create();
        await JsonSerializer.SerializeAsync(stream, piCase, JsonSerializerOptions);
    }

    public Task Draft(FileInfo file, FileInfo output)
    {
        return Task.CompletedTask;
    }
}
