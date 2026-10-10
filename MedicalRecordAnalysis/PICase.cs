using System.Text.Json.Serialization;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis;

public class PICase
{
    [JsonInclude]
    public DocumentSummary[] DocumentSummaries { get; private init; }

    public CaseSummary? Summary { get; set; }

    public Contradiction[] Contradictions { get; set; } = [];

    /**
     * Each source file's extracted text as passages, so drafting can search the records without re-running OCR.
     */
    public SourceDocument[] Sources { get; set; } = [];

    public PICase(int numDocuments)
    {
        DocumentSummaries = new DocumentSummary[numDocuments];
    }

    [JsonConstructor]
    private PICase()
    {
        DocumentSummaries = [];
    }
}
