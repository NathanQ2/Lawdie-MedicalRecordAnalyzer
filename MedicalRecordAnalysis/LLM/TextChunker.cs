namespace MedicalRecordAnalysis.LLM;

/**
 * Splits source text into small citable chunks: each line, further split into sentences. Citing chunks
 * rather than a provider's own sentence splitting keeps quotes tight (a header line is its own chunk) and
 * makes every citation an exact span of the source text.
 */
public static class TextChunker
{
    // A period after one of these words does not end a sentence.
    private static readonly HashSet<string> Abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "mr", "mrs", "ms", "st", "vs", "no", "approx", "e.g", "i.e", "etc",
    };

    /**
     * Returns the (Start, End) span of each chunk, end-exclusive and trimmed of surrounding whitespace.
     * Chunks end at line breaks and at sentence ends; whitespace-only chunks are skipped.
     */
    public static List<(int Start, int End)> Chunk(string text)
    {
        List<(int Start, int End)> chunks = [];
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            int end;
            if (i == text.Length || text[i] == '\n')
            {
                end = i;
            }
            else if (IsSentenceEnd(text, i))
            {
                end = i + 1;
            }
            else
            {
                continue;
            }

            int chunkStart = start;
            int chunkEnd = end;
            while (chunkStart < chunkEnd && char.IsWhiteSpace(text[chunkStart]))
            {
                chunkStart++;
            }

            while (chunkEnd > chunkStart && char.IsWhiteSpace(text[chunkEnd - 1]))
            {
                chunkEnd--;
            }

            if (chunkEnd > chunkStart)
            {
                chunks.Add((chunkStart, chunkEnd));
            }

            start = end;
        }

        return chunks;
    }

    /**
     * True when the character at index is '.', '!' or '?' followed by whitespace or the end of the text,
     * and is not the period of a known abbreviation such as "Dr.".
     */
    public static bool IsSentenceEnd(string text, int index)
    {
        char c = text[index];
        if (c is not ('.' or '!' or '?') || (index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1])))
        {
            return false;
        }

        if (c == '.')
        {
            int wordStart = index;
            while (wordStart > 0 && !char.IsWhiteSpace(text[wordStart - 1]))
            {
                wordStart--;
            }

            if (Abbreviations.Contains(text[wordStart..index]))
            {
                return false;
            }
        }

        return true;
    }
}
