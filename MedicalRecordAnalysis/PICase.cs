using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

public class PICase
{
    public DocumentSummary[] DocumentSummaries { get; private init; }
    
    public CaseSummary? Summary { get; set; }

    public Contradiction[] Contradictions { get; set; } = [];

    public PICase(int numDocuments)
    {
        DocumentSummaries = new DocumentSummary[numDocuments];
    }
}
