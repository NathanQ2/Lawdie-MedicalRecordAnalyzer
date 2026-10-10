using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class TextChunkerTests
{
    private static string[] Chunks(string text) => TextChunker.Chunk(text).Select(chunk => text[chunk.Start..chunk.End]).ToArray();

    [Fact]
    public void SplitsLinesSoHeadersAreTheirOwnChunks()
    {
        Assert.Equal(["PHYSICAL EXAM:", "Cervical spine: tenderness C3-C6."], Chunks("PHYSICAL EXAM:\nCervical spine: tenderness C3-C6."));
    }

    [Fact]
    public void SplitsSentencesWithinALine()
    {
        Assert.Equal(["Denies LOC.", "Denies numbness.", "Ambulatory?", "Yes!"], Chunks("Denies LOC. Denies numbness. Ambulatory? Yes!"));
    }

    [Fact]
    public void KeepsAbbreviationsDecimalsAndDatesTogether()
    {
        Assert.Equal(["Seen by Dr. Raman for 7.5 mg dosing on 03/14/2026."], Chunks("Seen by Dr. Raman for 7.5 mg dosing on 03/14/2026."));
    }

    [Fact]
    public void TrimsWhitespaceAndSkipsBlankLines()
    {
        string text = "  HISTORY:  \r\n\n\t\n  Neck pain.  ";

        Assert.Equal(["HISTORY:", "Neck pain."], Chunks(text));
    }

    [Fact]
    public void ChunkSpansAreExactAndInOrder()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ER_Visit_2026-03-14.txt"));

        List<(int Start, int End)> chunks = TextChunker.Chunk(text);

        Assert.True(chunks.Count > 20);
        Assert.All(chunks, chunk => Assert.Equal(text[chunk.Start..chunk.End], text[chunk.Start..chunk.End].Trim()));
        Assert.Equal(chunks.OrderBy(chunk => chunk.Start), chunks);
        Assert.All(chunks.Zip(chunks.Skip(1)), pair => Assert.True(pair.First.End <= pair.Second.Start));
    }
}
