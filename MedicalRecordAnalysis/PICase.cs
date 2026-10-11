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

    /**
     * Records that could not be read, so they can be shown to the user rather than silently left out.
     */
    public SkippedFile[] SkippedFiles { get; set; } = [];

    /**
     * A case from its documents' summaries, one per readable record, in the same order as Sources.
     */
    public PICase(IReadOnlyList<DocumentSummary> documentSummaries)
    {
        DocumentSummaries = [.. documentSummaries];
    }

    [JsonConstructor]
    private PICase()
    {
        DocumentSummaries = [];
    }
}
