using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM;

/**
 * Cites sentences that the model copied from the records word for word but left uncited. Each uncited sentence is
 * looked up in the case's source passages, ignoring whitespace (PDF text wraps lines differently) and letter case;
 * if it occurs exactly once, a citation to that exact span is attached. A sentence that wraps record wording in a
 * short lead-in ("The ED physician wrote that ...") is cited when the verbatim remainder is most of the sentence.
 * Text found in several places stays uncited, since a common phrase could otherwise be credited to the wrong
 * record. The draft's text is never changed.
 */
public static class VerbatimCitations
{
    // Shorter sentences ("Pain 6/10.") are too generic to attribute to one place.
    private const int MinimumSignificantCharacters = 12;
    // A verbatim remainder after a lead-in must be this long and cover this share of the sentence.
    private const int MinimumRemainderCharacters = 20;
    private const double MinimumRemainderShare = 0.6;

    public static DemandDraft Apply(DemandDraft draft, IReadOnlyList<SourceDocument> sources)
    {
        List<IndexedPassage> passages = sources
            .SelectMany(source => source.Passages.Select(passage => new IndexedPassage(source.FilePath, passage)))
            .ToList();
        if (passages.Count == 0)
        {
            return draft;
        }

        List<DraftSegment> segments = [];
        foreach (DraftSegment segment in draft.Segments)
        {
            if (segment.Citations.Length > 0)
            {
                segments.Add(segment);
            }
            else
            {
                segments.AddRange(CiteSentences(segment.Text, passages));
            }
        }

        return new DemandDraft()
        {
            Segments = segments.ToArray(),
            UnsupportedStatements = GroundingCheck.FindUnsupportedSentences(
                segments.Select(segment => new GeneratedSegment(segment.Text, segment.Citations.Length > 0)).ToList()),
        };
    }

    /**
     * Splits uncited text so each sentence found verbatim in exactly one passage becomes its own cited segment.
     */
    private static IEnumerable<DraftSegment> CiteSentences(string text, List<IndexedPassage> passages)
    {
        int position = 0;
        foreach ((int start, int end) in TextChunker.Chunk(text))
        {
            string sentence = text[start..end];
            if (sentence.StartsWith('#') || sentence.Count(char.IsLetterOrDigit) < MinimumSignificantCharacters)
            {
                continue;
            }

            Citation? citation = FindForSentence(sentence, passages);
            if (citation is null)
            {
                continue;
            }

            if (start > position)
            {
                yield return new DraftSegment() { Text = text[position..start] };
            }

            yield return new DraftSegment() { Text = sentence, Citations = [citation] };
            position = end;
        }

        if (position < text.Length)
        {
            yield return new DraftSegment() { Text = text[position..] };
        }
    }

    /**
     * The whole sentence's unique source span, or failing that, the unique span of its longest remainder after
     * dropping leading words that still covers most of the sentence.
     */
    private static Citation? FindForSentence(string sentence, List<IndexedPassage> passages)
    {
        int significant = sentence.Count(char.IsLetterOrDigit);
        string remainder = sentence;
        while (true)
        {
            Citation? citation = FindUnique(remainder, sentence, passages);
            if (citation is not null)
            {
                return citation;
            }

            int nextWord = remainder.IndexOf(' ', StringComparison.Ordinal);
            if (nextWord < 0)
            {
                return null;
            }

            remainder = remainder[(nextWord + 1)..].TrimStart();
            int remaining = remainder.Count(char.IsLetterOrDigit);
            if (remaining < MinimumRemainderCharacters || remaining < significant * MinimumRemainderShare)
            {
                return null;
            }
        }
    }

    private static Citation? FindUnique(string text, string sentence, List<IndexedPassage> passages)
    {
        string target = Normalize(text);
        Citation? found = null;
        foreach (IndexedPassage passage in passages)
        {
            int index = passage.Stripped.IndexOf(target, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (found is not null)
                {
                    return null;
                }

                int start = passage.Offsets[index];
                int end = passage.Offsets[index + target.Length - 1] + 1;
                found = new Citation()
                {
                    FilePath = passage.FilePath,
                    CitedText = passage.Passage.Text[start..end],
                    StartPosition = passage.Passage.StartPosition + start,
                    Claim = sentence.Trim(),
                };
                index = passage.Stripped.IndexOf(target, index + 1, StringComparison.Ordinal);
            }
        }

        return found;
    }

    /**
     * Drops whitespace and lowercases each character in place (keeping one character per input character, so
     * positions map back to the original text).
     */
    private static string Normalize(string text) =>
        string.Concat(text.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant));

    /**
     * A passage with its whitespace removed, and where each remaining character sits in the original text.
     */
    private sealed class IndexedPassage
    {
        public IndexedPassage(string filePath, Passage passage)
        {
            FilePath = filePath;
            Passage = passage;
            List<int> offsets = [];
            for (int i = 0; i < passage.Text.Length; i++)
            {
                if (!char.IsWhiteSpace(passage.Text[i]))
                {
                    offsets.Add(i);
                }
            }

            Offsets = offsets.ToArray();
            Stripped = Normalize(passage.Text);
        }

        public string FilePath { get; }
        public Passage Passage { get; }
        public string Stripped { get; }
        public int[] Offsets { get; }
    }
}
