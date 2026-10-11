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
}

public record ReviewItem(string Id, ReviewItemKind Kind, string Text, Citation[] Citations, ReviewStatus Status, string? Note);

/**
 * The attorney review checklist: contradictions from the analysis, plus uncited statements, placeholders and
 * quotes read by OCR from the draft. An item's id comes from its kind and text, so its review state carries over when
 * the analysis or draft is regenerated with the same item.
 */
public static class ReviewItems
{
    public static IReadOnlyList<ReviewItem> Build(PICase? analysis, DemandDraft? draft, IReadOnlyDictionary<string, ReviewState> states)
    {
        List<(ReviewItemKind Kind, string Text, Citation[] Citations)> items = [];

        foreach (Contradiction contradiction in analysis?.Contradictions ?? [])
        {
            items.Add((ReviewItemKind.Contradiction, contradiction.Summary, contradiction.Citations));
        }

        if (draft is not null)
        {
            items.AddRange(draft.UnsupportedStatements.Select(statement => (ReviewItemKind.UnsupportedStatement, statement, Array.Empty<Citation>())));
            items.AddRange(DemandLetterRenderer.FindPlaceholders(draft.Narrative).Select(placeholder => (ReviewItemKind.Placeholder, placeholder, Array.Empty<Citation>())));
            items.AddRange(DemandLetterRenderer.FindRecognizedQuotes(draft).Select(citation =>
                (ReviewItemKind.RecognizedQuote, DemandLetterRenderer.DescribeRecognizedQuote(citation), new[] { citation })));
        }

        return items
            .Select(item => (Id: ItemId(item.Kind, item.Text), item.Kind, item.Text, item.Citations))
            .DistinctBy(item => item.Id)
            .Select(item =>
            {
                ReviewState? state = states.GetValueOrDefault(item.Id);
                return new ReviewItem(item.Id, item.Kind, item.Text, item.Citations, state?.Status ?? ReviewStatus.Open, state?.Note);
            })
            .ToList();
    }

    public static string ItemId(ReviewItemKind kind, string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}:{text}"));
        return $"{kind.ToString().ToLowerInvariant()}-{Convert.ToHexStringLower(hash)[..16]}";
    }
}
