using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MedicalRecordAnalysis.OCR.Providers;

/**
 * Reads text that is already embedded in a file (plain text files and digital PDFs). With a page recognizer, PDF
 * pages that have no embedded text (scans, faxes, handwritten forms) and image files are sent to OCR page by page,
 * so digital pages keep their exact text and only scanned pages are recognized. Without one, files that need OCR
 * are rejected with a NotSupportedException so a ChainedOCRProvider can try another provider.
 */
public class DirectTextProvider : IOCRProvider
{
    private static readonly HashSet<string> PlainTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
    };

    /**
     * Image types the page recognizer can read. They have no embedded text, so they need a recognizer.
     */
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
        ".bmp",
        ".heif",
    };

    /**
     * A PDF page with fewer non-whitespace characters than this is treated as a scan and sent to OCR. Without a
     * recognizer the whole file is rejected, so pages are never silently left out of the extracted text.
     */
    private const int MinimumCharactersPerPage = 20;

    private const string PageSeparator = "\n\n";

    private readonly IPageRecognizer? m_pageRecognizer;

    public DirectTextProvider(IPageRecognizer? pageRecognizer = null)
    {
        m_pageRecognizer = pageRecognizer;
    }

    public async Task<OCRResult> DetectTextFromFile(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The source file does not exist: {file.FullName}", file.FullName);
        }

        if (PlainTextExtensions.Contains(file.Extension))
        {
            return new OCRResult()
            {
                File = file,
                Text = await File.ReadAllTextAsync(file.FullName, cancellationToken),
            };
        }

        if (string.Equals(file.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadPdf(file, cancellationToken);
        }

        if (ImageExtensions.Contains(file.Extension) && m_pageRecognizer is not null)
        {
            RecognizedPage[] pages = await m_pageRecognizer.RecognizePages(file, null, cancellationToken);
            if (pages.Length == 0)
            {
                throw new NotSupportedException("OCR found no pages in the image.");
            }

            return Assemble(file, pages.OrderBy(page => page.PageNumber).Select(page => new PageContent(page.Text, page)).ToList());
        }

        throw new NotSupportedException($"'{file.Extension}' files have no embedded text to read.");
    }

    /**
     * Reads the embedded text of each page and sends the pages without any to the recognizer in a single request.
     */
    private async Task<OCRResult> ReadPdf(FileInfo file, CancellationToken cancellationToken)
    {
        string?[] embedded = ReadEmbeddedPages(file, cancellationToken);
        int[] scannedPages = Enumerable.Range(1, embedded.Length).Where(pageNumber => embedded[pageNumber - 1] is null).ToArray();

        Dictionary<int, RecognizedPage> recognized = [];
        if (scannedPages.Length > 0)
        {
            if (m_pageRecognizer is null)
            {
                throw new NotSupportedException($"Page {scannedPages[0]} has no embedded text (likely a scan) and needs OCR.");
            }

            recognized = (await m_pageRecognizer.RecognizePages(file, scannedPages, cancellationToken))
                .GroupBy(page => page.PageNumber)
                .ToDictionary(group => group.Key, group => group.First());
            int[] missing = scannedPages.Where(pageNumber => !recognized.ContainsKey(pageNumber)).ToArray();
            if (missing.Length > 0)
            {
                throw new NotSupportedException($"OCR returned no result for page {string.Join(", ", missing)}.");
            }
        }

        List<PageContent> pages = [];
        for (int i = 0; i < embedded.Length; i++)
        {
            pages.Add(embedded[i] is string text ? new PageContent(text, null) : new PageContent(recognized[i + 1].Text, recognized[i + 1]));
        }

        return Assemble(file, pages);
    }

    /**
     * A damaged or password-protected PDF is a file this provider can't read, not a failure of the whole ingest, so
     * PdfPig's errors become NotSupportedException and the file is reported as skipped with the reason.
     */
    private static string?[] ReadEmbeddedPages(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            return ExtractEmbeddedPages(file, cancellationToken);
        }
        catch (PdfDocumentEncryptedException exception)
        {
            throw new NotSupportedException("The PDF is password-protected.", exception);
        }
        catch (PdfDocumentFormatException exception)
        {
            throw new NotSupportedException($"The PDF is damaged or not a valid PDF: {exception.Message}", exception);
        }
    }

    /**
     * Each page's embedded text, or null for a page with too little to be anything but a scan.
     */
    private static string?[] ExtractEmbeddedPages(FileInfo file, CancellationToken cancellationToken)
    {
        using PdfDocument document = PdfDocument.Open(file.FullName);

        List<string?> pages = [];
        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string pageText = ContentOrderTextExtractor.GetText(page);
            pages.Add(pageText.Count(c => !char.IsWhiteSpace(c)) < MinimumCharactersPerPage ? null : pageText);
        }

        if (pages.Count == 0)
        {
            throw new NotSupportedException("The PDF has no pages.");
        }

        return pages.ToArray();
    }

    /**
     * Joins the pages into one text, recording where each page starts and which ranges came from OCR. Each page is
     * trimmed, and its recognition spans are shifted to match.
     */
    private static OCRResult Assemble(FileInfo file, IReadOnlyList<PageContent> pages)
    {
        StringBuilder text = new StringBuilder();
        int[] pageStarts = new int[pages.Count];
        List<TextSpan> recognized = [];
        List<TextSpan> handwritten = [];
        List<TextSpan> lowConfidence = [];

        for (int i = 0; i < pages.Count; i++)
        {
            if (i > 0)
            {
                text.Append(PageSeparator);
            }

            pageStarts[i] = text.Length;
            string pageText = pages[i].Text.Trim();
            if (pages[i].Recognized is RecognizedPage page && pageText.Length > 0)
            {
                int shift = pageStarts[i] - (page.Text.Length - page.Text.TrimStart().Length);
                recognized.Add(new TextSpan(pageStarts[i], pageText.Length));
                handwritten.AddRange(ShiftSpans(page.HandwrittenSpans, shift, pageStarts[i], pageText.Length));
                lowConfidence.AddRange(ShiftSpans(page.LowConfidenceSpans, shift, pageStarts[i], pageText.Length));
            }

            text.Append(pageText);
        }

        return new OCRResult()
        {
            File = file,
            Text = text.ToString(),
            PageStartPositions = pageStarts,
            Recognition = new RecognitionSpans()
            {
                Recognized = recognized.ToArray(),
                Handwritten = handwritten.ToArray(),
                LowConfidence = lowConfidence.ToArray(),
            },
        };
    }

    /**
     * Moves page-relative spans into the document's text, clipped to the page's trimmed text.
     */
    private static IEnumerable<TextSpan> ShiftSpans(TextSpan[] spans, int shift, int pageStart, int pageLength)
    {
        foreach (TextSpan span in spans)
        {
            int start = Math.Max(span.StartPosition + shift, pageStart);
            int end = Math.Min(span.EndPosition + shift, pageStart + pageLength);
            if (end > start)
            {
                yield return new TextSpan(start, end - start);
            }
        }
    }

    private record PageContent(string Text, RecognizedPage? Recognized);
}
