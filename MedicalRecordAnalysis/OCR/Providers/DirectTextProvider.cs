using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MedicalRecordAnalysis.OCR.Providers;

/**
 * Reads text that is already embedded in a file (plain text files and digital PDFs), so no OCR is
 * needed. Files without usable embedded text, such as images and scanned PDFs, are rejected with a
 * NotSupportedException so a ChainedOCRProvider can hand them to a real OCR provider.
 */
public class DirectTextProvider : IOCRProvider
{
    private static readonly HashSet<string> PlainTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".md",
    };

    /**
     * A PDF page with fewer non-whitespace characters than this is treated as a scan. Any such page sends
     * the whole file to OCR, so pages are never silently left out of the extracted text.
     */
    private const int MinimumCharactersPerPage = 20;

    private const string PageSeparator = "\n\n";

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
            (string text, int[] pageStarts) = ReadPdf(file, cancellationToken);
            return new OCRResult()
            {
                File = file,
                Text = text,
                PageStartPositions = pageStarts,
            };
        }

        throw new NotSupportedException($"'{file.Extension}' files have no embedded text to read.");
    }

    /**
     * A damaged or password-protected PDF is a file this provider can't read, not a failure of the whole ingest, so
     * PdfPig's errors become NotSupportedException and the file is reported as skipped with the reason.
     */
    private static (string Text, int[] PageStartPositions) ReadPdf(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            return ExtractPdfText(file, cancellationToken);
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

    private static (string Text, int[] PageStartPositions) ExtractPdfText(FileInfo file, CancellationToken cancellationToken)
    {
        using PdfDocument document = PdfDocument.Open(file.FullName);

        List<string> pages = [];
        foreach (Page page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string pageText = ContentOrderTextExtractor.GetText(page);
            if (pageText.Count(c => !char.IsWhiteSpace(c)) < MinimumCharactersPerPage)
            {
                throw new NotSupportedException($"Page {page.Number} has no embedded text (likely a scan) and needs OCR.");
            }

            pages.Add(pageText.Trim());
        }

        if (pages.Count == 0)
        {
            throw new NotSupportedException("The PDF has no pages.");
        }

        int[] pageStarts = new int[pages.Count];
        for (int i = 1; i < pages.Count; i++)
        {
            pageStarts[i] = pageStarts[i - 1] + pages[i - 1].Length + PageSeparator.Length;
        }

        return (string.Join(PageSeparator, pages), pageStarts);
    }
}
