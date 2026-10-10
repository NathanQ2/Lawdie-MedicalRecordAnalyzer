using System.Text.RegularExpressions;

namespace MedicalRecordAnalysis.Retrieval;

/**
 * Splits extracted text into paragraph-sized passages. Paragraphs (separated by blank lines) are merged
 * until a passage reaches the target length; paragraphs longer than the maximum are split by line. Every
 * passage's text is an exact substring of the input at its StartPosition.
 */
public static partial class PassageSplitter
{
    public const int DefaultTargetLength = 800;
    public const int DefaultMaxLength = 1500;

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex ParagraphBreak();

    public static Passage[] Split(string text, int targetLength = DefaultTargetLength, int maxLength = DefaultMaxLength)
    {
        List<(int Start, int End)> pieces = [];
        foreach ((int start, int end) in Paragraphs(text))
        {
            if (end - start <= maxLength)
            {
                pieces.Add((start, end));
            }
            else
            {
                pieces.AddRange(SplitLongParagraph(text, start, end, maxLength));
            }
        }

        List<Passage> passages = [];
        int passageStart = -1;
        int passageEnd = -1;
        foreach ((int start, int end) in pieces)
        {
            if (passageStart >= 0 && (passageEnd - passageStart >= targetLength || end - passageStart > maxLength))
            {
                passages.Add(Create(text, passageStart, passageEnd));
                passageStart = -1;
            }

            if (passageStart < 0)
            {
                passageStart = start;
            }

            passageEnd = end;
        }

        if (passageStart >= 0)
        {
            passages.Add(Create(text, passageStart, passageEnd));
        }

        return passages.ToArray();
    }

    private static IEnumerable<(int Start, int End)> Paragraphs(string text)
    {
        int start = 0;
        foreach (Match separator in ParagraphBreak().Matches(text))
        {
            (int Start, int End)? paragraph = Trimmed(text, start, separator.Index);
            if (paragraph is not null)
            {
                yield return paragraph.Value;
            }

            start = separator.Index + separator.Length;
        }

        (int Start, int End)? last = Trimmed(text, start, text.Length);
        if (last is not null)
        {
            yield return last.Value;
        }
    }

    private static IEnumerable<(int Start, int End)> SplitLongParagraph(string text, int start, int end, int maxLength)
    {
        int pieceStart = start;
        while (end - pieceStart > maxLength)
        {
            // Break after the last line ending that fits; with no line ending, break at the maximum length.
            int lineBreak = text.LastIndexOf('\n', pieceStart + maxLength - 1, maxLength);
            int pieceEnd = lineBreak > pieceStart ? lineBreak : pieceStart + maxLength;

            (int Start, int End)? piece = Trimmed(text, pieceStart, pieceEnd);
            if (piece is not null)
            {
                yield return piece.Value;
            }

            pieceStart = pieceEnd;
        }

        (int Start, int End)? rest = Trimmed(text, pieceStart, end);
        if (rest is not null)
        {
            yield return rest.Value;
        }
    }

    private static (int Start, int End)? Trimmed(string text, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return end > start ? (start, end) : null;
    }

    private static Passage Create(string text, int start, int end)
    {
        return new Passage() { StartPosition = start, Text = text[start..end] };
    }
}
