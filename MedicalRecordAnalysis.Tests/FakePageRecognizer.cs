using MedicalRecordAnalysis.OCR;

namespace MedicalRecordAnalysis.Tests;

/**
 * Returns preset page texts instead of calling an OCR service, and records which pages were requested.
 */
public sealed class FakePageRecognizer : IPageRecognizer
{
    private readonly Dictionary<int, RecognizedPage> m_pages;

    public FakePageRecognizer(params RecognizedPage[] pages)
    {
        m_pages = pages.ToDictionary(page => page.PageNumber);
    }

    public List<IReadOnlyList<int>?> Requests { get; } = [];

    public Task<RecognizedPage[]> RecognizePages(FileInfo file, IReadOnlyList<int>? pageNumbers, CancellationToken cancellationToken = default)
    {
        Requests.Add(pageNumbers);
        IEnumerable<RecognizedPage> pages = pageNumbers is null
            ? m_pages.Values
            : pageNumbers.Where(m_pages.ContainsKey).Select(pageNumber => m_pages[pageNumber]);
        return Task.FromResult(pages.ToArray());
    }
}
