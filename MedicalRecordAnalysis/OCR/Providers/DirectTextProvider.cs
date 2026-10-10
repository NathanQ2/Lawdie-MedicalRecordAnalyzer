using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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

    public async Task<OCRResult> DetectTextFromFile(FileInfo file)
    {
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The source file does not exist: {file.FullName}", file.FullName);
        }

        string text;
        if (PlainTextExtensions.Contains(file.Extension))
        {
            text = await File.ReadAllTextAsync(file.FullName);
        }
        else if (string.Equals(file.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            text = ExtractPdfText(file);
        }
        else
        {
            throw new NotSupportedException($"'{file.Extension}' files have no embedded text to read.");
        }

        return new OCRResult()
        {
            File = file,
            Text = text,
        };
    }

    private static string ExtractPdfText(FileInfo file)
    {
        using PdfDocument document = PdfDocument.Open(file.FullName);

        List<string> pages = [];
        foreach (Page page in document.GetPages())
        {
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

        return string.Join(PageSeparator, pages);
    }
}
