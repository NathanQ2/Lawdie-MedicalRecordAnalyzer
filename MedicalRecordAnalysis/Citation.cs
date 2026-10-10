namespace MedicalRecordAnalysis;

public class Citation
{
    public required string FilePath { get; init; }
    public required string CitedText { get; init; }
    public required int StartPosition { get; init; }
    public int CitationLength => CitedText.Length;
}
