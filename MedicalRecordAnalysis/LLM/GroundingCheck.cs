namespace MedicalRecordAnalysis.LLM;

/**
 * A piece of generated text and whether the provider cited a source for it.
 */
public readonly record struct GeneratedSegment(string Text, bool IsCited);

/**
 * Provider-agnostic checks that generated text is backed by citations.
 */
public static class GroundingCheck
{
    // A period after one of these words does not end a sentence.
    private static readonly HashSet<string> Abbreviations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dr", "mr", "mrs", "ms", "st", "vs", "no", "approx", "e.g", "i.e", "etc",
    };

    /**
     * Returns the sentences of the generated text that no cited segment overlaps, so they can be
     * reviewed before anyone relies on them. Sentences end at '.', '!' or '?' followed by whitespace,
     * or at a line break. Splitting errs toward smaller sentences, which only flags more for review.
     */
    public static string[] FindUnsupportedSentences(IReadOnlyList<GeneratedSegment> segments)
    {
        string text = string.Concat(segments.Select(segment => segment.Text));
        bool[] cited = new bool[text.Length];

        int offset = 0;
        foreach (GeneratedSegment segment in segments)
        {
            if (segment.IsCited)
            {
                Array.Fill(cited, true, offset, segment.Text.Length);
            }

            offset += segment.Text.Length;
        }

        List<string> unsupported = [];
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && !IsSentenceEnd(text, i))
            {
                continue;
            }

            int end = Math.Min(i + 1, text.Length);
            string sentence = text[start..end].Trim();
            if (sentence.Any(char.IsLetterOrDigit) && !cited.AsSpan(start, end - start).Contains(true))
            {
                unsupported.Add(sentence);
            }

            start = end;
        }

        return unsupported.ToArray();
    }

    private static bool IsSentenceEnd(string text, int index)
    {
        char c = text[index];
        if (c == '\n')
        {
            return true;
        }

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
