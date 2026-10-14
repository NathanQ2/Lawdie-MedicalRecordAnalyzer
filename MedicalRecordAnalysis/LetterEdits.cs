using System.Text.RegularExpressions;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

/**
 * An edit the letter can't take: its text changed since the edit began, or the edit refers to a source the letter
 * doesn't cite or puts a source number where it supports nothing.
 */
public sealed class LetterEditException : Exception
{
    public LetterEditException(string message, bool conflict) : base(message)
    {
        Conflict = conflict;
    }

    /** Whether the letter changed since the edit began, rather than the edit itself being invalid. */
    public bool Conflict { get; }
}

/**
 * Applies an attorney's edit to one stretch of the letter, such as a paragraph, without loosening its grounding.
 * The edited text marks sources with their numbers in brackets ("[3]") right after the statements they support, as
 * the letter shows them. Only sources the letter already cites can be used, and a number supports only the sentence
 * it follows, so an edit can never cite a new source or extend one to text it wasn't attached to. Uncited sentences
 * are found again afterwards: any the attorney wrote, or left without a source, is listed for review, while fixed
 * text that was never checked (the letterhead, salutation and closing) stays unlisted unless it changes.
 */
public static partial class LetterEdits
{
    // Numbers that follow each other, even across spaces, mark one statement.
    [GeneratedRegex(@"(?:\[\d{1,4}\]\s*)*\[\d{1,4}\]")]
    private static partial Regex MarkerGroup();

    [GeneratedRegex(@"\[(\d{1,4})\]")]
    private static partial Regex Marker();

    /**
     * The letter with the text at start, which must still read original, replaced by edited. Sources maps each
     * number used in edited to the citation it stands for; each must be one the letter already cites.
     */
    public static DemandDraft Replace(DemandDraft draft, int start, string original, string edited, IReadOnlyDictionary<int, Citation> sources)
    {
        string narrative = draft.Narrative;
        if (start < 0 || start + original.Length > narrative.Length || string.CompareOrdinal(narrative, start, original, 0, original.Length) != 0)
        {
            throw new LetterEditException("The letter changed since you started editing. Reload it and make the edit again.", conflict: true);
        }

        // The letter's own copies are used, so an edit can't change what a source quotes or where it is.
        Dictionary<(string, int, string), Citation> cited = draft.Segments
            .SelectMany(segment => segment.Citations)
            .DistinctBy(Key)
            .ToDictionary(Key);
        Citation Resolve(int number)
        {
            if (!sources.TryGetValue(number, out Citation? source))
            {
                throw new LetterEditException($"The letter has no source [{number}]. Use a number the letter shows, or remove it.", conflict: false);
            }

            return cited.TryGetValue(Key(source), out Citation? citation)
                ? citation
                : throw new LetterEditException($"Source [{number}] isn't one of this letter's sources.", conflict: false);
        }

        List<DraftSegment> segments =
        [
            .. Slice(draft.Segments, 0, start),
            .. Parse(edited.ReplaceLineEndings("\n"), Resolve),
            .. Slice(draft.Segments, start + original.Length, narrative.Length),
        ];
        DraftSegment[] merged = Merge(segments);

        // Sentences that were uncited but not listed are fixed text no one checked; they stay unlisted while unchanged.
        HashSet<string> neverChecked = [.. GroundingCheck.FindUnsupportedSentences(Generated(draft.Segments)).Except(draft.UnsupportedStatements)];
        return new DemandDraft()
        {
            Segments = merged,
            UnsupportedStatements = GroundingCheck.FindUnsupportedSentences(Generated(merged)).Where(sentence => !neverChecked.Contains(sentence)).ToArray(),
        };
    }

    /**
     * Splits edited text into segments: each run of source numbers cites the sentence just before it, and
     * everything else is uncited.
     */
    private static List<DraftSegment> Parse(string text, Func<int, Citation> resolve)
    {
        List<DraftSegment> segments = [];
        int position = 0;
        foreach (Match group in MarkerGroup().Matches(text))
        {
            string before = text[position..group.Index];
            int claimStart = ClaimStart(before);
            if (claimStart < 0)
            {
                throw new LetterEditException("Put each source number right after the sentence it supports.", conflict: false);
            }

            string claim = before[claimStart..];
            Citation[] citations = Marker().Matches(group.Value)
                .Select(marker => resolve(int.Parse(marker.Groups[1].Value)))
                .DistinctBy(Key)
                .Select(citation => citation.WithClaim(claim.Trim()))
                .ToArray();

            segments.Add(new DraftSegment() { Text = before[..claimStart] });
            segments.Add(new DraftSegment() { Text = claim, Citations = citations });
            position = group.Index + group.Length;
        }

        segments.Add(new DraftSegment() { Text = text[position..] });
        return segments;
    }

    /**
     * Where the sentence a source number follows begins: just after the last sentence end or line break before
     * it. Returns -1 when there is no text for the number to support.
     */
    private static int ClaimStart(string before)
    {
        string trimmed = before.TrimEnd();
        if (trimmed.Trim().Length == 0)
        {
            return -1;
        }

        // The last character is the claim's own end, such as its period, so the search starts before it.
        for (int i = trimmed.Length - 2; i >= 0; i--)
        {
            if (trimmed[i] == '\n' || TextChunker.IsSentenceEnd(trimmed, i))
            {
                int claimStart = i + 1;
                while (char.IsWhiteSpace(trimmed[claimStart]))
                {
                    claimStart++;
                }

                return claimStart;
            }
        }

        return before.Length - before.TrimStart().Length;
    }

    /** The parts of the segments that fall between two positions of the narrative. */
    private static IEnumerable<DraftSegment> Slice(DraftSegment[] segments, int from, int to)
    {
        int position = 0;
        foreach (DraftSegment segment in segments)
        {
            int start = Math.Max(from, position);
            int end = Math.Min(to, position + segment.Text.Length);
            if (start < end)
            {
                string text = segment.Text[(start - position)..(end - position)];
                bool whole = text.Length == segment.Text.Length;
                yield return whole ? segment : new DraftSegment()
                {
                    Text = text,
                    Citations = segment.Citations.Select(citation => citation.WithClaim(text.Trim())).ToArray(),
                };
            }

            position += segment.Text.Length;
        }
    }

    /** Drops empty segments and joins neighboring uncited ones. */
    private static DraftSegment[] Merge(IEnumerable<DraftSegment> segments)
    {
        List<DraftSegment> merged = [];
        foreach (DraftSegment segment in segments.Where(segment => segment.Text.Length > 0))
        {
            if (segment.Citations.Length == 0 && merged.Count > 0 && merged[^1].Citations.Length == 0)
            {
                merged[^1] = new DraftSegment() { Text = merged[^1].Text + segment.Text };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged.ToArray();
    }

    private static List<GeneratedSegment> Generated(IEnumerable<DraftSegment> segments) =>
        segments.Select(segment => new GeneratedSegment(segment.Text, segment.Citations.Length > 0)).ToList();

    private static (string, int, string) Key(Citation citation) => (citation.FilePath, citation.StartPosition, citation.CitedText);
}
