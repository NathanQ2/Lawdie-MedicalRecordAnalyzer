namespace MedicalRecordAnalysis.Retrieval;

/**
 * Sets each citation's page numbers from its source's page start positions. Sources without pages count as a
 * single page; citations whose file is not among the sources are left unchanged.
 */
public static class PageLocator
{
    public static void AssignPages(IEnumerable<Citation> citations, IReadOnlyList<SourceDocument> sources)
    {
        Dictionary<string, int[]> pageStarts = sources
            .GroupBy(source => source.FilePath)
            .ToDictionary(group => group.Key, group => group.First().PageStartPositions);

        foreach (Citation citation in citations)
        {
            if (!pageStarts.TryGetValue(citation.FilePath, out int[]? starts))
            {
                continue;
            }

            citation.PageNumber = PageAt(starts, citation.StartPosition);
            int endPage = PageAt(starts, citation.StartPosition + Math.Max(citation.CitationLength - 1, 0));
            citation.EndPageNumber = endPage != citation.PageNumber ? endPage : null;
        }
    }

    /**
     * The 1-based page containing the given offset: the last page whose start is at or before it.
     */
    public static int PageAt(int[] pageStartPositions, int position)
    {
        int index = Array.BinarySearch(pageStartPositions, position);
        int page = index >= 0 ? index + 1 : ~index;
        return Math.Max(page, 1);
    }
}
