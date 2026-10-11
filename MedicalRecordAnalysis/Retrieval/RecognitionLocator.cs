using MedicalRecordAnalysis.OCR;

namespace MedicalRecordAnalysis.Retrieval;

/**
 * Flags citations whose quotes overlap text that came from OCR, so the attorney checks them against the original
 * page. Citations whose file is not among the sources are left unchanged.
 */
public static class RecognitionLocator
{
    public static void AssignFlags(IEnumerable<Citation> citations, IReadOnlyList<SourceDocument> sources)
    {
        Dictionary<string, RecognitionSpans> recognition = sources
            .GroupBy(source => source.FilePath)
            .ToDictionary(group => group.Key, group => group.First().Recognition);

        foreach (Citation citation in citations)
        {
            if (!recognition.TryGetValue(citation.FilePath, out RecognitionSpans? spans))
            {
                continue;
            }

            int start = citation.StartPosition;
            int end = start + Math.Max(citation.CitationLength, 1);
            citation.Recognized = spans.Recognized.Any(span => span.Overlaps(start, end));
            citation.Handwritten = spans.Handwritten.Any(span => span.Overlaps(start, end));
            citation.LowConfidence = spans.LowConfidence.Any(span => span.Overlaps(start, end));
        }
    }
}
