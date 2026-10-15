namespace MedicalRecordAnalysis;

/**
 * The law firm's details for the letterhead and signature, entered once for every case. They fill the letter's
 * firm placeholders when it is shown or exported, unless a case gives its own value for one. Details left empty
 * stay placeholders for review.
 */
public record FirmProfile(string? FirmName, string? Address, string? Phone, string? Email, string? AttorneyName)
{
    public static readonly FirmProfile Empty = new FirmProfile(null, null, null, null, null);

    /** The placeholder each detail that is set fills, such as "[LAW FIRM NAME]". */
    public IReadOnlyDictionary<string, string> PlaceholderValues()
    {
        Dictionary<string, string> values = [];
        foreach ((string placeholder, string? value) in new[]
        {
            (DemandLetterTemplate.FirmNamePlaceholder, FirmName),
            (DemandLetterTemplate.FirmAddressPlaceholder, Address),
            (DemandLetterTemplate.FirmPhonePlaceholder, Phone),
            (DemandLetterTemplate.FirmEmailPlaceholder, Email),
            (DemandLetterTemplate.AttorneyNamePlaceholder, AttorneyName),
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[placeholder] = value.Trim();
            }
        }

        return values;
    }
}
