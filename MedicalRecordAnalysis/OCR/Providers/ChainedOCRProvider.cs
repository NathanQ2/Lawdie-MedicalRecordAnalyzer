namespace MedicalRecordAnalysis.OCR.Providers;

/**
 * Tries each provider in order, moving to the next when one throws NotSupportedException. Put cheap,
 * exact providers (DirectTextProvider) first and OCR services after them.
 */
public class ChainedOCRProvider : IOCRProvider
{
    private readonly IReadOnlyList<IOCRProvider> m_providers;

    public ChainedOCRProvider(IReadOnlyList<IOCRProvider> providers)
    {
        if (providers.Count == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }

        m_providers = providers;
    }

    public async Task<OCRResult> DetectTextFromFile(FileInfo file)
    {
        List<string> reasons = [];

        foreach (IOCRProvider provider in m_providers)
        {
            try
            {
                return await provider.DetectTextFromFile(file);
            }
            catch (NotSupportedException exception)
            {
                reasons.Add($"{provider.GetType().Name}: {exception.Message}");
            }
        }

        throw new NotSupportedException($"No provider could read '{file.Name}'. {string.Join(" ", reasons)}");
    }
}
