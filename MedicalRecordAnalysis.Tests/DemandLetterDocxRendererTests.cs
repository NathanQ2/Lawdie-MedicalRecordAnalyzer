using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Tests;

public class DemandLetterDocxRendererTests
{
    private static readonly Citation DeniesLoc = new Citation() { FilePath = "/records/er.pdf", CitedText = "Denies loss of\nconsciousness.", StartPosition = 657, PageNumber = 2 };
    private static readonly Citation LostLoc = new Citation() { FilePath = "/records/pt.txt", CitedText = "Briefly lost consciousness.", StartPosition = 319, PageNumber = 1 };

    private static DemandDraft CreateDraft() => new DemandDraft()
    {
        Segments =
        [
            new DraftSegment() { Text = "The ER record states she " },
            new DraftSegment() { Text = "denied loss of consciousness", Citations = [DeniesLoc, DeniesLoc] },
            new DraftSegment() { Text = ", while the PT record says she " },
            new DraftSegment() { Text = "briefly lost consciousness", Citations = [LostLoc] },
            new DraftSegment() { Text = ".\n\nClaim [CLAIM NUMBER] demands [DEMAND AMOUNT]." },
        ],
        UnsupportedStatements = ["Claim [CLAIM NUMBER] demands [DEMAND AMOUNT]."],
    };

    private static PICase CreateCase()
    {
        PICase piCase = new PICase([]);
        piCase.Contradictions = [new Contradiction() { Summary = "Loss of consciousness conflicts.", Citations = [DeniesLoc, LostLoc] }];
        return piCase;
    }

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    [Fact]
    public void ProducesASchemaValidWordDocument()
    {
        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase()));

        string[] errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(document)
            .Select(error => $"{error.Path?.XPath}: {error.Description}").ToArray();

        Assert.Empty(errors);
    }

    [Fact]
    public void UsesRelativeRelationshipTargets()
    {
        using System.IO.Compression.ZipArchive archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase())));
        Dictionary<string, string> rels = archive.Entries.Where(entry => entry.FullName.EndsWith(".rels"))
            .ToDictionary(entry => entry.FullName, entry => new StreamReader(entry.Open()).ReadToEnd());

        Assert.Contains("Target=\"word/document.xml\"", rels["_rels/.rels"]);
        Assert.Contains("Target=\"footnotes.xml\"", rels["word/_rels/document.xml.rels"]);
        Assert.All(rels.Values, xml => Assert.DoesNotContain("Target=\"/", xml));
    }

    [Fact]
    public void WritesTheNarrativeAsParagraphs()
    {
        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase()));
        string[] paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().Select(paragraph => paragraph.InnerText).ToArray();

        Assert.Equal("The ER record states she denied loss of consciousness, while the PT record says she briefly lost consciousness.", paragraphs[0]);
        Assert.Equal("Claim [CLAIM NUMBER] demands [DEMAND AMOUNT].", paragraphs[1]);
    }

    [Fact]
    public void CitesEachSourceInAFootnoteAfterItsClaim()
    {
        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase()));
        Paragraph narrative = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ElementAt(0);
        Footnote[] footnotes = document.MainDocumentPart.FootnotesPart!.Footnotes!.Elements<Footnote>().Where(footnote => footnote.Id! > 0).ToArray();

        // The repeated citation in one claim gets a single footnote.
        Assert.Equal([1L, 2L], narrative.Descendants<FootnoteReference>().Select(reference => reference.Id!.Value));
        Assert.Equal(" er.pdf, p. 2: \"Denies loss of consciousness.\"", footnotes[0].InnerText);
        Assert.Equal(" pt.txt, p. 1: \"Briefly lost consciousness.\"", footnotes[1].InnerText);

        // Each footnote reference follows the claim it supports.
        Run reference = narrative.Descendants<FootnoteReference>().First().Ancestors<Run>().First();
        Assert.EndsWith("denied loss of consciousness", string.Concat(reference.ElementsBefore().Select(element => element.InnerText)));
    }

    [Fact]
    public void HighlightsPlaceholders()
    {
        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase()));
        string[] highlighted = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ElementAt(1)
            .Descendants<Run>().Where(run => run.RunProperties?.Highlight is not null).Select(run => run.InnerText).ToArray();

        Assert.Equal(["[CLAIM NUMBER]", "[DEMAND AMOUNT]"], highlighted);
    }

    [Fact]
    public void AddsTheAttorneyReviewSectionOnANewPage()
    {
        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(CreateDraft(), CreateCase()));
        Body body = document.MainDocumentPart!.Document!.Body!;
        string[] paragraphs = body.Elements<Paragraph>().Select(paragraph => paragraph.InnerText).ToArray();
        int review = Array.IndexOf(paragraphs, "Attorney Review - Remove Before Sending");

        Assert.True(review > 0);
        Assert.Contains(body.Elements<Paragraph>().ElementAt(review - 1).Descendants<Break>(), element => element.Type?.Value == BreakValues.Page);
        Assert.Contains("• Loss of consciousness conflicts.", paragraphs);
        Assert.Contains("– er.pdf, p. 2: \"Denies loss of consciousness.\"", paragraphs);
        Assert.Contains("• Claim [CLAIM NUMBER] demands [DEMAND AMOUNT].", paragraphs);
        Assert.Contains("• [DEMAND AMOUNT]", paragraphs);
    }

    [Fact]
    public void FormatsLetterBlocksHeadingsAndExpenseItems()
    {
        DemandDraft draft = new DemandDraft()
        {
            Segments =
            [
                new DraftSegment() { Text = "[LAW FIRM NAME]\n[FIRM ADDRESS]\n\nDear [ADJUSTER NAME]:\n\n## Medical Expenses\n\n" },
                new DraftSegment() { Text = "- Mercy General ED (01/09/2026): $6,912.40", Citations = [DeniesLoc] },
                new DraftSegment() { Text = "\n- Valley Rehab: $9,860.00\n\nShe has suffered." },
            ],
        };

        using WordprocessingDocument document = Open(DemandLetterDocxRenderer.Render(draft, new PICase([])));
        Paragraph[] paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToArray();

        // The letterhead lines stay in one paragraph, separated by line breaks.
        Assert.Equal("[LAW FIRM NAME][FIRM ADDRESS]", paragraphs[0].InnerText);
        Assert.Single(paragraphs[0].Descendants<Break>());
        Assert.Equal("Dear [ADJUSTER NAME]:", paragraphs[1].InnerText);

        Assert.Equal("Medical Expenses", paragraphs[2].InnerText);
        Assert.All(paragraphs[2].Descendants<Run>(), run => Assert.NotNull(run.RunProperties?.Bold));
        Assert.NotNull(paragraphs[2].ParagraphProperties?.KeepNext);

        Assert.Equal("•\tMercy General ED (01/09/2026): $6,912.40", string.Concat(paragraphs[3].Descendants<Text>().Select(text => text.Text)));
        Assert.Single(paragraphs[3].Descendants<FootnoteReference>());
        Assert.Equal("•\tValley Rehab: $9,860.00", string.Concat(paragraphs[4].Descendants<Text>().Select(text => text.Text)));
        Assert.Equal("720", paragraphs[4].ParagraphProperties?.Indentation?.Left?.Value);
        Assert.Equal("She has suffered.", paragraphs[5].InnerText);
    }

    [Theory]
    [InlineData("Draft.docx", DraftFormat.Docx)]
    [InlineData("Draft.DOCX", DraftFormat.Docx)]
    public void DocxOutputFilesUseWordFormat(string fileName, DraftFormat expected)
    {
        Assert.Equal(expected, DemandLetterRenderer.FormatFor(new FileInfo(fileName)));
    }

    [Fact]
    public void TextRenderingRejectsWordFormat()
    {
        Assert.Throws<ArgumentException>(() => DemandLetterRenderer.Render(CreateDraft(), CreateCase(), DraftFormat.Docx));
        Assert.Equal("PK"u8.ToArray(), DemandLetterRenderer.RenderBytes(CreateDraft(), CreateCase(), DraftFormat.Docx)[..2]);
    }
}
