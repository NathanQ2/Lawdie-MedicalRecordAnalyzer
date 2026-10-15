using System.Globalization;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

/**
 * The standard parts of a personal injury demand letter around the generated body: letterhead, addressee, the
 * "Re:" block, salutation, and opening paragraph before it; the demand and signature after it. Details the
 * records cannot supply are bracketed placeholders, which appear in the attorney review checklist. These parts
 * are fixed text, not generated, so they are not checked for citations.
 */
public static class DemandLetterTemplate
{
    public const string Opening =
        "[LAW FIRM NAME]\n" +
        "[FIRM ADDRESS]\n" +
        "[FIRM PHONE] | [FIRM EMAIL]\n" +
        "\n" +
        "[DATE]\n" +
        "\n" +
        "VIA [DELIVERY METHOD]\n" +
        "\n" +
        "[ADJUSTER NAME]\n" +
        "[INSURANCE COMPANY]\n" +
        "[INSURER ADDRESS]\n" +
        "\n" +
        "Re: Our Client: [CLIENT NAME]\n" +
        "Your Insured: [INSURED NAME]\n" +
        "Claim No.: [CLAIM NUMBER]\n" +
        "Date of Loss: [DATE OF LOSS]\n" +
        "\n" +
        "FOR SETTLEMENT PURPOSES ONLY\n" +
        "\n" +
        "Dear [ADJUSTER NAME]:\n" +
        "\n" +
        "This firm represents [CLIENT NAME] for injuries sustained on [DATE OF LOSS] in [INCIDENT TYPE] involving " +
        "your insured, [INSURED NAME]. We submit this letter and the enclosed medical documentation to set out " +
        "our client's injuries and damages and to resolve this claim without litigation.\n" +
        "\n";

    public const string Closing =
        "\n" +
        "\n" +
        "## Demand\n" +
        "\n" +
        "Based on the injuries, treatment, and losses described above, our client demands [DEMAND AMOUNT] in " +
        "full settlement of this claim. This offer remains open until [RESPONSE DEADLINE]. Please direct all " +
        "communication regarding this claim to our office.\n" +
        "\n" +
        "Sincerely,\n" +
        "\n" +
        "[ATTORNEY NAME]\n" +
        "[LAW FIRM NAME]\n" +
        "\n" +
        "Enclosures: [LIST OF ENCLOSURES]";

    public const string DateOfLossPlaceholder = "[DATE OF LOSS]";
    public const string FirmNamePlaceholder = "[LAW FIRM NAME]";
    public const string FirmAddressPlaceholder = "[FIRM ADDRESS]";
    public const string FirmPhonePlaceholder = "[FIRM PHONE]";
    public const string FirmEmailPlaceholder = "[FIRM EMAIL]";
    public const string AttorneyNamePlaceholder = "[ATTORNEY NAME]";

    /**
     * Replaces the placeholders the attorney has answered: the date of loss from the intake details, and any others
     * from values the attorney filled in, such as the firm's details or the claim number. This runs each time the
     * letter is shown or exported rather than when it is drafted, so correcting a detail never requires drafting
     * again. Unanswered placeholders are left in place for review.
     */
    public static DemandDraft Fill(DemandDraft draft, MatterDetails matter, IReadOnlyDictionary<string, string>? values = null)
    {
        Dictionary<string, string> replacements = new Dictionary<string, string>(values ?? new Dictionary<string, string>());
        // The intake details are the one place the date of loss is entered, so they decide it.
        replacements.Remove(DateOfLossPlaceholder);
        if (matter.DateOfLoss is DateOnly dateOfLoss)
        {
            replacements[DateOfLossPlaceholder] = dateOfLoss.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        }

        if (replacements.Count == 0)
        {
            return draft;
        }

        string FillText(string text) => replacements.Aggregate(text, (filled, value) => filled.Replace(value.Key, value.Value));

        return new DemandDraft()
        {
            Segments = draft.Segments.Select(segment => new DraftSegment()
            {
                Text = FillText(segment.Text),
                // The page places each source after the statement it supports by finding that statement's text.
                Citations = segment.Citations.Select(citation => citation.Claim is string claim && FillText(claim) != claim
                    ? citation.WithClaim(FillText(claim))
                    : citation).ToArray(),
            }).ToArray(),
            // Uncited statements are matched against the letter's text, so they must be filled the same way.
            UnsupportedStatements = draft.UnsupportedStatements.Select(FillText).ToArray(),
        };
    }

    /**
     * The full letter: the opening, the generated body, and the closing. Uncited statements are those of the
     * generated body only.
     */
    public static DemandDraft Wrap(DemandDraft body)
    {
        return new DemandDraft()
        {
            Segments = [new DraftSegment() { Text = Opening }, .. body.Segments, new DraftSegment() { Text = Closing }],
            UnsupportedStatements = body.UnsupportedStatements,
        };
    }
}
