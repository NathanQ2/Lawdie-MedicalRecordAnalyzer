using System.Text;
using System.Text.RegularExpressions;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

public enum DraftFormat
{
    Markdown,
    PlainText,
}

/**
 * Renders a demand draft with a numbered source note for every cited claim, followed by an attorney review
 * section listing contradictions, uncited statements, and placeholders that must be resolved before sending.
 */
public static partial class DemandLetterRenderer
{
    private const string Title = "Demand Letter Narrative (Draft)";
    private const string ReviewTitle = "Attorney Review - Remove Before Sending";

    [GeneratedRegex(@"\[[A-Z][A-Z0-9 &/'.,-]*\]")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    public static DraftFormat FormatFor(FileInfo output)
    {
        return string.Equals(output.Extension, ".txt", StringComparison.OrdinalIgnoreCase) ? DraftFormat.PlainText : DraftFormat.Markdown;
    }

    public static string Render(DemandDraft draft, PICase piCase, DraftFormat format)
    {
        bool markdown = format == DraftFormat.Markdown;
        StringBuilder builder = new StringBuilder();

        List<Citation> sources = [];
        string narrative = RenderNarrative(draft, sources, markdown);

        builder.AppendLine(markdown ? $"# {Title}" : Title.ToUpperInvariant()).AppendLine();
        builder.AppendLine(narrative.Trim()).AppendLine();

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

        AppendSection(builder, markdown, "Contradictions between records", piCase.Contradictions.Select(contradiction =>
            contradiction.Summary + string.Concat(contradiction.Citations.Select(citation => $"\n    - {DescribeSource(citation)}"))));
        AppendSection(builder, markdown, "Statements without a cited source", draft.UnsupportedStatements);
        AppendSection(builder, markdown, "Placeholders to fill in",
            PlaceholderPattern().Matches(draft.Narrative).Select(match => match.Value).Distinct());

        return builder.ToString().TrimEnd() + Environment.NewLine;
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

    private static string DescribeSource(Citation citation)
    {
        string quote = WhitespacePattern().Replace(citation.CitedText, " ").Trim();
        return $"{Path.GetFileName(citation.FilePath)}, character {citation.StartPosition}: \"{quote}\"";
    }
}
