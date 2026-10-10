using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Tests;

/**
 * Offline checks over the fictional Case_Ellis records (see Case_Ellis_AnswerKey.md).
 */
public class CaseEllisTests
{
    private const string ScannedFax = "09_Faxed_Referral_2026-02-20.pdf";

    private static readonly DirectoryInfo CaseDirectory = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "TestData", "Case_Ellis"));

    private static async Task<PICase> ExtractCase()
    {
        DirectTextProvider provider = new DirectTextProvider();
        List<SourceDocument> sources = [];
        foreach (FileInfo file in CaseDirectory.GetFiles().Where(file => file.Name != ScannedFax).OrderBy(file => file.Name))
        {
            OCRResult result = await provider.DetectTextFromFile(file);
            sources.Add(new SourceDocument() { FilePath = file.FullName, Passages = PassageSplitter.Split(result.Text) });
        }

        PICase piCase = new PICase(1);
        piCase.DocumentSummaries[0] = new DocumentSummary() { Summary = "summary" };
        piCase.Sources = sources.ToArray();
        return piCase;
    }

    [Fact]
    public async Task EveryRecordExceptTheScannedFaxHasReadableText()
    {
        DirectTextProvider provider = new DirectTextProvider();

        foreach (FileInfo file in CaseDirectory.GetFiles().Where(file => file.Name != ScannedFax))
        {
            OCRResult result = await provider.DetectTextFromFile(file);
            Assert.Contains("Ellis", result.Text);
        }

        await Assert.ThrowsAsync<NotSupportedException>(() => provider.DetectTextFromFile(new FileInfo(Path.Combine(CaseDirectory.FullName, ScannedFax))));
    }

    [Fact]
    public async Task LongPdfIsReadAcrossAllPages()
    {
        FileInfo notes = CaseDirectory.GetFiles("06_PT_Daily_Notes_*.pdf").Single();

        OCRResult result = await new DirectTextProvider().DetectTextFromFile(notes);

        Assert.Equal(33, result.Text.Split("DAILY NOTE - Visit").Length - 1);
        Assert.Contains("Visit 33", result.Text);
    }

    // PDF text wraps lines, so phrases are compared with whitespace collapsed.
    private static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

    [Fact]
    public async Task RetrievalSelectsWithinBudgetAndKeepsKeyEvidence()
    {
        PICase piCase = await ExtractCase();
        int totalLength = piCase.Sources.Sum(source => source.Passages.Sum(passage => passage.Text.Length));

        RetrievedPassage[] selected = PassageRetriever.Select(piCase);

        Assert.True(totalLength > PassageRetriever.DefaultCharacterBudget, "The case should be large enough to need selection.");
        Assert.True(selected.Sum(passage => passage.Text.Length) <= PassageRetriever.DefaultCharacterBudget);
        Assert.Contains(selected, passage => Normalize(passage.Text).Contains("reasonable degree of medical", StringComparison.Ordinal));
        Assert.Contains(selected, passage => Normalize(passage.Text).Contains("struck from behind by a box truck", StringComparison.Ordinal));
        Assert.Contains(selected, passage => Normalize(passage.Text).Contains("Neck Disability Index", StringComparison.Ordinal));
    }
}
