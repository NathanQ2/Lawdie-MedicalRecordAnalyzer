using System.Globalization;
using System.Text.RegularExpressions;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.LLM;

/**
 * Provider-agnostic checks on a chronology. The provider reads each entry's date and provider name from quotes, but
 * it can still misread a date or shorten a name, so both are looked up in the cited document's own text and flagged
 * when they can't be found. An entry with no citation is dropped, because nothing ties it to a record. The date of
 * loss is kept only when one of its own quotes states that date, since a wrong one would raise a false alarm about
 * the intake details.
 */
public static partial class ChronologyCheck
{
    // The common ways records write a date. Matches must not run into neighboring digits or letters, so 1/9/2026 is
    // not found inside 11/9/2026.
    private static readonly string[] DateFormats =
    [
        "MM/dd/yyyy", "M/d/yyyy", "MM/dd/yy", "M/d/yy", "yyyy-MM-dd", "MM-dd-yyyy", "M-d-yyyy",
        "MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy", "MMM. d, yyyy", "d MMMM yyyy", "d MMM yyyy",
    ];

    // Words this short ("ED", "PT", "MD") are too generic to show that a name comes from the document.
    private const int MinimumNameWordLength = 3;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex WordBreak();

    public static ChronologyResult Apply(ChronologyResult result, IReadOnlyList<SourceDocument> sources)
    {
        Dictionary<string, string> documentTexts = sources
            .GroupBy(source => source.FilePath)
            .ToDictionary(group => group.Key, group => string.Join("\n", group.First().Passages.Select(passage => passage.Text)));

        List<ChronologyEntry> entries = [];
        foreach (ChronologyEntry entry in result.Entries)
        {
            if (entry.Citations.Length == 0)
            {
                continue;
            }

            string quotes = string.Join("\n", entry.Citations.Select(citation => citation.CitedText));
            string documents = string.Join("\n", entry.Citations
                .Select(citation => citation.FilePath)
                .Distinct()
                .Select(filePath => documentTexts.GetValueOrDefault(filePath, "")));
            entry.DateConfirmed = ContainsDate(quotes, entry.Date) || ContainsDate(documents, entry.Date);
            entry.ProviderConfirmed = ContainsName(quotes + "\n" + documents, entry.Provider);
            entries.Add(entry);
        }

        CitedDate? dateOfLoss = result.DateOfLoss is CitedDate stated &&
            ContainsDate(string.Join("\n", stated.Citations.Select(citation => citation.CitedText)), stated.Date)
                ? stated
                : null;

        return new ChronologyResult()
        {
            Entries = entries
                .OrderBy(entry => entry.Date)
                .ThenBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(entry => (entry.Date, entry.Provider.ToLowerInvariant(), entry.VisitType.ToLowerInvariant()))
                .ToArray(),
            DateOfLoss = dateOfLoss,
        };
    }

    /**
     * Whether the text states the date in any of the common written forms, ignoring letter case and line breaks.
     */
    public static bool ContainsDate(string text, DateOnly date)
    {
        string normalized = Whitespace().Replace(text, " ");
        foreach (string format in DateFormats)
        {
            string written = date.ToString(format, CultureInfo.InvariantCulture);
            for (int index = normalized.IndexOf(written, StringComparison.OrdinalIgnoreCase); index >= 0;
                 index = normalized.IndexOf(written, index + 1, StringComparison.OrdinalIgnoreCase))
            {
                if (!IsWordCharacter(normalized, index - 1) && !IsWordCharacter(normalized, index + written.Length))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /**
     * Whether every significant word of the name appears as a whole word in the text, ignoring letter case. A name
     * with no significant words can't be confirmed.
     */
    public static bool ContainsName(string text, string name)
    {
        string[] nameWords = WordBreak().Split(name).Where(word => word.Length >= MinimumNameWordLength).ToArray();
        if (nameWords.Length == 0)
        {
            return false;
        }

        HashSet<string> textWords = new HashSet<string>(WordBreak().Split(text), StringComparer.OrdinalIgnoreCase);
        return nameWords.All(textWords.Contains);
    }

    private static bool IsWordCharacter(string text, int index)
    {
        return index >= 0 && index < text.Length && char.IsLetterOrDigit(text[index]);
    }
}
