using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MedicalRecordAnalysis.Tests;

/**
 * A temporary directory for test input files, deleted on dispose.
 */
public sealed class TestFiles : IDisposable
{
    public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory("MedicalRecordAnalysisTests");

    public FileInfo WriteText(string name, string text)
    {
        string path = Path.Combine(Directory.FullName, name);
        File.WriteAllText(path, text);
        return new FileInfo(path);
    }

    /**
     * Writes a digital PDF with one page per entry. A null entry produces a page with no text, like a scan.
     */
    public FileInfo WritePdf(string name, params string?[] pageTexts)
    {
        PdfDocumentBuilder builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (string? pageText in pageTexts)
        {
            PdfPageBuilder page = builder.AddPage(PageSize.Letter);
            if (pageText is not null)
            {
                page.AddText(pageText, 12, new PdfPoint(50, 700), font);
            }
        }

        string path = Path.Combine(Directory.FullName, name);
        File.WriteAllBytes(path, builder.Build());
        return new FileInfo(path);
    }

    public void Dispose()
    {
        Directory.Delete(recursive: true);
    }
}
