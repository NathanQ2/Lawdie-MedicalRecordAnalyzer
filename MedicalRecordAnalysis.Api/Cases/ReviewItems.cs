using System.Security.Cryptography;
using System.Text;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Api.Cases;

public enum ReviewItemKind
{
    Contradiction,
    UnsupportedStatement,
    Placeholder,
    RecognizedQuote,
    /** The intake details disagree with what the documents state, such as a different date of loss. */
    IntakeMismatch,
}

/**
 * A range of the draft's narrative (its segments' text joined), which the review UI highlights in the letter.
 */
public record DraftRange(int Start, int Length);

public record ReviewItem(string Id, ReviewItemKind Kind, string Text, Citation[] Citations, ReviewStatus Status, string? Note,
    DraftRange[] DraftRanges);

/**
 * The attorney review checklist: contradictions from the analysis and intake details the documents disagree with,
 * plus uncited statements, placeholders and quotes read by OCR from the draft. An item's id comes from its kind and
 * text, so its review state carries over when the analysis or draft is regenerated with the same item. Each item
 * also lists where it appears in the draft, so the attorney can see which sentences to revise once it is resolved.
 */
public static class ReviewItems
{
    public static IReadOnlyList<ReviewItem> Build(PICase? analysis, DemandDraft? draft, IReadOnlyDictionary<string, ReviewState> states,
        MatterDetails? matter = null)
    {
        List<(ReviewItemKind Kind, string Text, Citation[] Citations, DraftRange[] Ranges)> items = [];
        string narrative = draft?.Narrative ?? "";

        // The letter uses the intake details, so one the documents contradict would reach the adjuster.
        items.AddRange(IntakeMismatch.Find(analysis, matter).Select(mismatch =>
            (ReviewItemKind.IntakeMismatch, mismatch.Summary, mismatch.Citations, Occurrences(narrative, mismatch.IntakeText, firstOnly: false))));

        foreach (Contradiction contradiction in analysis?.Contradictions ?? [])
        {
            items.Add((ReviewItemKind.Contradiction, contradiction.Summary, contradiction.Citations, CitingRanges(draft, contradiction.Citations)));
        }

        if (draft is not null)
        {
            items.AddRange(draft.UnsupportedStatements.Select(statement =>
                (ReviewItemKind.UnsupportedStatement, statement, Array.Empty<Citation>(), Occurrences(narrative, statement.Trim(), firstOnly: true))));
            items.AddRange(DemandLetterRenderer.FindPlaceholders(narrative).Select(placeholder =>
                (ReviewItemKind.Placeholder, placeholder, Array.Empty<Citation>(), Occurrences(narrative, placeholder, firstOnly: false))));
            items.AddRange(DemandLetterRenderer.FindRecognizedQuotes(draft).Select(citation =>
                (ReviewItemKind.RecognizedQuote, DemandLetterRenderer.DescribeRecognizedQuote(citation), new[] { citation }, CitingRanges(draft, [citation]))));
        }

        return items
            .Select(item => (Id: ItemId(item.Kind, item.Text), item.Kind, item.Text, item.Citations, item.Ranges))
            .DistinctBy(item => item.Id)
            .Select(item =>
            {
                ReviewState? state = states.GetValueOrDefault(item.Id);
                return new ReviewItem(item.Id, item.Kind, item.Text, item.Citations, state?.Status ?? ReviewStatus.Open, state?.Note, item.Ranges);
            })
            .ToList();
    }

    public static string ItemId(ReviewItemKind kind, string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{text}"));
        return $"{kind.ToString().ToLowerInvariant()}-{Convert.ToHexStringLower(hash)[..16]}";
    }

    /**
     * The draft's cited sentences that rely on any of the quotes. Draft citations come from source passages rather
     * than the analysis quotes, so a sentence counts when one of its citations overlaps a quote in the same file.
     * Sentences that state a disputed fact without citing it can't be found this way; they are listed as uncited.
     */
    private static DraftRange[] CitingRanges(DemandDraft? draft, IReadOnlyCollection<Citation> quotes)
    {
        if (draft is null || quotes.Count == 0)
        {
            return [];
        }

        List<DraftRange> ranges = [];
        int position = 0;
        foreach (DraftSegment segment in draft.Segments)
        {
            if (segment.Citations.Any(citation => quotes.Any(quote => Overlaps(citation, quote))))
            {
                int leading = segment.Text.Length - segment.Text.TrimStart().Length;
                int length = segment.Text.Trim().Length;
                if (length > 0)
                {
                    ranges.Add(new DraftRange(position + leading, length));
                }
            }

            position += segment.Text.Length;
        }

        return ranges.ToArray();
    }

    private static bool Overlaps(Citation a, Citation b)
    {
        return a.FilePath == b.FilePath &&
            a.StartPosition < b.StartPosition + Math.Max(b.CitationLength, 1) &&
            b.StartPosition < a.StartPosition + Math.Max(a.CitationLength, 1);
    }

    private static DraftRange[] Occurrences(string narrative, string text, bool firstOnly)
    {
        List<DraftRange> ranges = [];
        if (text.Length == 0)
        {
            return [];
        }

        for (int index = narrative.IndexOf(text, StringComparison.Ordinal); index >= 0; index = narrative.IndexOf(text, index + text.Length, StringComparison.Ordinal))
        {
            ranges.Add(new DraftRange(index, text.Length));
            if (firstOnly)
            {
                break;
            }
        }

        return ranges.ToArray();
    }
}
