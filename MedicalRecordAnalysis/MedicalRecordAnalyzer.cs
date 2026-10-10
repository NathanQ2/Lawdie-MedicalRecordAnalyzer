using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using MedicalRecordAnalysis.Retrieval;

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
        List<SourceDocument> sources = [];
        
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

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                Console.Error.WriteLine($"SKIPPED {file.Name}: No text was found.");
                continue;
            }

            // 2. Summarize 
            documentSummaries.Add(await m_llmProvider.SummarizeDocument(result.File, result.Text));
            sources.Add(new SourceDocument()
            {
                FilePath = result.File.FullName,
                Passages = PassageSplitter.Split(result.Text),
                PageStartPositions = result.PageStartPositions,
            });
        }

        PICase piCase = new PICase(documentSummaries.Count);
        documentSummaries.CopyTo(piCase.DocumentSummaries);
        piCase.Sources = sources.ToArray();
        
        // 3. Find contradictions, so the case summary can present disputed facts as disputed
        VerificationResult verification = await m_llmProvider.Verify(piCase);
        piCase.Contradictions = verification.Contradictions;
        
        // 4. Summarize the case, grounded in the extracted quotes
        piCase.Summary = await m_llmProvider.SummarizeCase(piCase);
        PageLocator.AssignPages(
            piCase.DocumentSummaries.SelectMany(summary => summary.Citations)
                .Concat(piCase.Contradictions.SelectMany(contradiction => contradiction.Citations))
                .Concat(piCase.Summary.Citations),
            piCase.Sources);
        
        // 5. Export
        await using FileStream stream = output.Create();
        await JsonSerializer.SerializeAsync(stream, piCase, JsonSerializerOptions);
    }

    public async Task Draft(FileInfo file, FileInfo output)
    {
        // 1. Read the exported case
        PICase piCase;
        await using (FileStream input = file.OpenRead())
        {
            piCase = await JsonSerializer.DeserializeAsync<PICase>(input, JsonSerializerOptions)
                ?? throw new InvalidDataException($"'{file.Name}' does not contain a case.");
        }

        // 2. Draft the narrative from the source passages most relevant to each part of the letter
        RetrievedPassage[] passages = PassageRetriever.Select(piCase);
        DemandDraft draft = await m_llmProvider.DraftDemand(piCase, passages);
        PageLocator.AssignPages(draft.Segments.SelectMany(segment => segment.Citations), piCase.Sources);

        // 3. Export with source notes and an attorney review section
        string letter = DemandLetterRenderer.Render(draft, piCase, DemandLetterRenderer.FormatFor(output));
        await File.WriteAllTextAsync(output.FullName, letter);
    }
}
