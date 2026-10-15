using System.Text;
using System.Text.RegularExpressions;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

public enum DraftFormat
{
    Markdown,
    PlainText,
    Docx,
}

/**
 * Renders a demand draft with a numbered source note for every cited claim, followed by an attorney review
 * section listing intake details the documents contradict, contradictions, uncited statements, placeholders, and
 * quotes read by OCR that must be resolved or checked before sending.
 */
public static partial class DemandLetterRenderer
{
    internal const string ReviewTitle = "Attorney Review - Remove Before Sending";
    internal const string RecognizedQuotesHeading = "Quotes read by OCR (check against the original page)";
    internal const string IntakeMismatchesHeading = "Intake details the documents contradict";

    [GeneratedRegex(@"\[[A-Z][A-Z0-9 &/'.,-]*\]")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    public static DraftFormat FormatFor(FileInfo output)
    {
        return output.Extension.ToLowerInvariant() switch
        {
            ".txt" => DraftFormat.PlainText,
            ".docx" => DraftFormat.Docx,
            _ => DraftFormat.Markdown,
        };
    }

    /**
     * The rendered draft as file contents in any format: UTF-8 text for Markdown and plain text, or a Word document.
     * Given the attorney's intake details, the review section also lists those the documents contradict.
     */
    public static byte[] RenderBytes(DemandDraft draft, PICase piCase, DraftFormat format, MatterDetails? matter = null)
    {
        return format == DraftFormat.Docx
            ? DemandLetterDocxRenderer.Render(draft, piCase, matter)
            : Encoding.UTF8.GetBytes(Render(draft, piCase, format, matter));
    }

    /**
     * The rendered draft as Markdown or plain text. Use RenderBytes for Word documents. Without intake details (the
     * CLI has none) there is nothing to compare, so the intake section is left out rather than reporting no conflicts.
     */
    public static string Render(DemandDraft draft, PICase piCase, DraftFormat format, MatterDetails? matter = null)
    {
        if (format == DraftFormat.Docx)
        {
            throw new ArgumentException("Word documents are binary; use RenderBytes.", nameof(format));
        }

        bool markdown = format == DraftFormat.Markdown;
        StringBuilder builder = new StringBuilder();

        List<Citation> sources = [];
        string narrative = RenderNarrative(draft, sources, markdown);

        builder.AppendLine(FormatLetter(narrative.Trim(), markdown)).AppendLine();

        if (!markdown && sources.Count > 0)
        {
            builder.AppendLine("SOURCES");
        }

        for (int i = 0; i < sources.Count; i++)
        {
            builder.AppendLine(markdown ? $"[^{i + 1}]: {DescribeSource(sources[i])}" : $"[{i + 1}] {DescribeSource(sources[i])}");
        }

        builder.AppendLine();
        builder.AppendLine(markdown ? "---" : new string('=', 60)).AppendLine();
        builder.AppendLine(markdown ? $"## {ReviewTitle}" : ReviewTitle.ToUpperInvariant()).AppendLine();

        if (matter is not null)
        {
            AppendSection(builder, markdown, IntakeMismatchesHeading, IntakeMismatch.Find(piCase, matter).Select(mismatch =>
                mismatch.Summary + string.Concat(mismatch.Citations.Select(citation => $"\n    - {DescribeSource(citation)}"))));
        }

        AppendSection(builder, markdown, "Contradictions between records", piCase.Contradictions.Select(contradiction =>
            DescribeContradiction(contradiction) + string.Concat(contradiction.Citations.Select(citation => $"\n    - {DescribeSource(citation)}"))));
        AppendSection(builder, markdown, "Statements without a cited source", draft.UnsupportedStatements);
        AppendSection(builder, markdown, "Placeholders to fill in", FindPlaceholders(draft.Narrative));
        AppendSection(builder, markdown, RecognizedQuotesHeading, FindRecognizedQuotes(draft).Select(DescribeRecognizedQuote));

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    /**
     * Lays out the letter's lines for the format. In plain text, "## " section headings become capitalized lines. In
     * Markdown, consecutive lines inside a block (an address, the "Re:" block, a signature) end with two spaces so
     * they stay on separate lines instead of being joined into one paragraph.
     */
    private static string FormatLetter(string letter, bool markdown)
    {
        string[] lines = letter.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool heading = lines[i].StartsWith("## ", StringComparison.Ordinal);
            if (!markdown)
            {
                lines[i] = heading ? lines[i][3..].ToUpperInvariant() : lines[i];
                continue;
            }

            bool continuesBlock = i + 1 < lines.Length && lines[i].Trim().Length > 0 && lines[i + 1].Trim().Length > 0 &&
                !heading && !lines[i + 1].StartsWith("## ", StringComparison.Ordinal) && !lines[i + 1].StartsWith("- ", StringComparison.Ordinal);
            if (continuesBlock)
            {
                lines[i] = lines[i].TrimEnd() + "  ";
            }
        }

        return string.Join('\n', lines);
    }

    /**
     * The distinct bracketed placeholders in capitals (such as [CLAIM NUMBER]) in the text, in order.
     */
    public static IReadOnlyList<string> FindPlaceholders(string text)
    {
        return PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct().ToList();
    }

    /**
     * The distinct quotes cited in the draft that came from OCR, in order of first use. OCR can misread characters,
     * so each must be checked against the original page before the letter is sent.
     */
    public static IReadOnlyList<Citation> FindRecognizedQuotes(DemandDraft draft)
    {
        return draft.Segments
            .SelectMany(segment => segment.Citations)
            .Where(citation => citation.Recognized)
            .DistinctBy(citation => (citation.FilePath, citation.StartPosition, citation.CitedText))
            .ToList();
    }

    /**
     * The quote's source, with what makes it uncertain: "er.pdf, p. 2: "..." (handwritten; some words uncertain)".
     */
    public static string DescribeRecognizedQuote(Citation citation)
    {
        List<string> notes = [];
        if (citation.Handwritten)
        {
            notes.Add("handwritten");
        }

        if (citation.LowConfidence)
        {
            notes.Add("some words uncertain");
        }

        return notes.Count > 0 ? $"{DescribeSource(citation)} ({string.Join("; ", notes)})" : DescribeSource(citation);
    }

    /**
     * Appends a source marker after each cited segment, numbering each distinct quote once in order of first use.
     */
    private static string RenderNarrative(DemandDraft draft, List<Citation> sources, bool markdown)
    {
        Dictionary<(string, int, string), int> sourceNumbers = [];
        StringBuilder narrative = new StringBuilder();

        foreach (DraftSegment segment in draft.Segments)
        {
            string text = segment.Text.TrimEnd();
            narrative.Append(text);

            HashSet<int> marked = [];
            foreach (Citation citation in segment.Citations)
            {
                (string, int, string) key = (citation.FilePath, citation.StartPosition, citation.CitedText);
                if (!sourceNumbers.TryGetValue(key, out int number))
                {
                    sources.Add(citation);
                    number = sources.Count;
                    sourceNumbers[key] = number;
                }

                if (marked.Add(number))
                {
                    narrative.Append(markdown ? $"[^{number}]" : $"[{number}]");
                }
            }

            narrative.Append(segment.Text[text.Length..]);
        }

        return narrative.ToString();
    }

    private static void AppendSection(StringBuilder builder, bool markdown, string heading, IEnumerable<string> items)
    {
        builder.AppendLine(markdown ? $"### {heading}" : $"{heading}:");

        bool any = false;
        foreach (string item in items)
        {
            builder.Append("- ").AppendLine(item);
            any = true;
        }

        if (!any)
        {
            builder.AppendLine("None.");
        }

        builder.AppendLine();
    }

    /**
     * The contradiction's summary, labeled when two different quotes don't support it, so the reviewer checks it
     * against the records.
     */
    internal static string DescribeContradiction(Contradiction contradiction)
    {
        return contradiction.IsVerified ? contradiction.Summary : $"{contradiction.Summary} (Unverified: not supported by two different quotes.)";
    }

    public static string DescribeSource(Citation citation)
    {
        string quote = WhitespacePattern().Replace(citation.CitedText, " ").Trim();
        return $"{Path.GetFileName(citation.FilePath)}, {DescribeLocation(citation)}: \"{quote}\"";
    }

    /**
     * "p. 3" or "pp. 3-4"; citations without page numbers (older exports) fall back to the character offset.
     */
    private static string DescribeLocation(Citation citation)
    {
        if (citation.PageNumber is null)
        {
            return $"character {citation.StartPosition}";
        }

        return citation.EndPageNumber is int endPage ? $"pp. {citation.PageNumber}-{endPage}" : $"p. {citation.PageNumber}";
    }
}
