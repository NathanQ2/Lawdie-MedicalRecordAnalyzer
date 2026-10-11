using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

/**
 * Renders a demand draft as a Word document: the narrative with a Word footnote citing the source record, page,
 * and quote after each cited claim, placeholders highlighted, and the attorney review section on its own page.
 */
public static partial class DemandLetterDocxRenderer
{
    private const string FontSizeBody = "24"; // Half-points: 12 pt.
    private const string FontSizeFootnote = "18"; // 9 pt.

    [GeneratedRegex(@"\n[ \t]*\n\s*")]
    private static partial Regex ParagraphBreak();

    [GeneratedRegex(@"(\[[A-Z][A-Z0-9 &/'.,-]*\])")]
    private static partial Regex Placeholder();

    [GeneratedRegex("Target=\"(/[^\"]*)\"")]
    private static partial Regex AbsoluteTarget();

    public static byte[] Render(DemandDraft draft, PICase piCase)
    {
        using MemoryStream stream = new MemoryStream();
        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            Body body = new Body();
            main.Document = new Document(body);

            // Word requires the separator footnotes (ids -1 and 0) before any real footnote.
            FootnotesPart footnotesPart = main.AddNewPart<FootnotesPart>();
            footnotesPart.Footnotes = new Footnotes(
                new Footnote(new Paragraph(new Run(new SeparatorMark()))) { Type = FootnoteEndnoteValues.Separator, Id = -1 },
                new Footnote(new Paragraph(new Run(new ContinuationSeparatorMark()))) { Type = FootnoteEndnoteValues.ContinuationSeparator, Id = 0 });

            body.Append(StyledParagraph(DemandLetterRenderer.Title, bold: true, size: "32"));
            new NarrativeWriter(body, footnotesPart.Footnotes).Write(draft);

            body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
            body.Append(StyledParagraph(DemandLetterRenderer.ReviewTitle, bold: true, size: "28"));
            AppendSection(body, "Contradictions between records", piCase.Contradictions.Select(contradiction =>
                (contradiction.Summary, contradiction.Citations.Select(DemandLetterRenderer.DescribeSource).ToList())));
            AppendSection(body, "Statements without a cited source", draft.UnsupportedStatements.Select(statement => (statement, new List<string>())));
            AppendSection(body, "Placeholders to fill in", DemandLetterRenderer.FindPlaceholders(draft.Narrative).Select(placeholder => (placeholder, new List<string>())));
        }

        MakeRelationshipTargetsRelative(stream);
        return stream.ToArray();
    }

    /**
     * The SDK writes package relationships with absolute targets ("/word/document.xml"). Those are valid, but some
     * readers (macOS's text system, and with it Pages and Quick Look) reject them, so rewrite each as a path
     * relative to its source part, as Word itself does.
     */
    private static void MakeRelationshipTargetsRelative(MemoryStream package)
    {
        using ZipArchive archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true);
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.EndsWith(".rels", StringComparison.Ordinal)).ToList())
        {
            string xml;
            using (StreamReader reader = new StreamReader(entry.Open()))
            {
                xml = reader.ReadToEnd();
            }

            // "word/_rels/document.xml.rels" describes "/word/document.xml", so its targets are relative to "/word/".
            string relsFolder = Path.GetDirectoryName(entry.FullName)?.Replace('\\', '/') ?? "";
            string sourceFolder = "/" + (Path.GetDirectoryName(relsFolder)?.Replace('\\', '/') is { Length: > 0 } folder ? folder + "/" : "");
            Uri baseUri = new Uri("http://package" + sourceFolder);
            string rewritten = AbsoluteTarget().Replace(xml, match =>
                $"Target=\"{baseUri.MakeRelativeUri(new Uri("http://package" + match.Groups[1].Value))}\"");

            if (rewritten != xml)
            {
                string name = entry.FullName;
                entry.Delete();
                using StreamWriter writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(rewritten);
            }
        }
    }

    private static void AppendSection(Body body, string heading, IEnumerable<(string Text, List<string> Details)> items)
    {
        body.Append(StyledParagraph(heading, bold: true, size: FontSizeBody));

        bool any = false;
        foreach ((string text, List<string> details) in items)
        {
            body.Append(StyledParagraph($"• {text}", indent: "360"));
            foreach (string detail in details)
            {
                body.Append(StyledParagraph($"– {detail}", indent: "720", size: "20"));
            }

            any = true;
        }

        if (!any)
        {
            body.Append(StyledParagraph("None.", indent: "360"));
        }
    }

    private static Paragraph StyledParagraph(string text, bool bold = false, string size = FontSizeBody, string? indent = null)
    {
        ParagraphProperties properties = new ParagraphProperties(new SpacingBetweenLines() { After = "120" });
        if (indent is not null)
        {
            properties.Append(new Indentation() { Left = indent });
        }

        return new Paragraph(properties, TextRun(text, bold, size));
    }

    private static Run TextRun(string text, bool bold = false, string size = FontSizeBody, bool highlight = false)
    {
        // The Word schema fixes the order of run properties: bold, then font size, then highlight.
        RunProperties properties = new RunProperties();
        if (bold)
        {
            properties.Append(new Bold());
        }

        properties.Append(new FontSize() { Val = size });
        if (highlight)
        {
            properties.Append(new Highlight() { Val = HighlightColorValues.Yellow });
        }

        return new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }

    /**
     * Writes the narrative into paragraphs (blank lines separate them), adding a footnote after each cited claim.
     */
    private sealed class NarrativeWriter(Body body, Footnotes footnotes)
    {
        private Paragraph m_paragraph = NewParagraph();
        private int m_nextFootnoteId = 1;

        public void Write(DemandDraft draft)
        {
            body.Append(m_paragraph);
            foreach (DraftSegment segment in draft.Segments)
            {
                string text = segment.Text.TrimEnd();
                AppendText(text);

                HashSet<(string, int, string)> referenced = [];
                foreach (Citation citation in segment.Citations)
                {
                    if (referenced.Add((citation.FilePath, citation.StartPosition, citation.CitedText)))
                    {
                        AppendFootnote(DemandLetterRenderer.DescribeSource(citation));
                    }
                }

                AppendText(segment.Text[text.Length..]);
            }
        }

        private void AppendText(string text)
        {
            string[] paragraphs = ParagraphBreak().Split(text);
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0)
                {
                    m_paragraph = NewParagraph();
                    body.Append(m_paragraph);
                }

                string[] lines = paragraphs[i].Split('\n');
                for (int line = 0; line < lines.Length; line++)
                {
                    if (line > 0)
                    {
                        m_paragraph.Append(new Run(new Break()));
                    }

                    foreach (string part in Placeholder().Split(lines[line]).Where(part => part.Length > 0))
                    {
                        bool placeholder = Placeholder().IsMatch(part);
                        m_paragraph.Append(TextRun(part, bold: placeholder, highlight: placeholder));
                    }
                }
            }
        }

        private void AppendFootnote(string description)
        {
            int id = m_nextFootnoteId++;
            m_paragraph.Append(new Run(Superscript(), new FootnoteReference() { Id = id }));
            footnotes.Append(new Footnote(new Paragraph(
                new Run(Superscript(), new FootnoteReferenceMark()),
                TextRun($" {description}", size: FontSizeFootnote))) { Id = id });
        }

        private static RunProperties Superscript() => new RunProperties(new VerticalTextAlignment() { Val = VerticalPositionValues.Superscript });

        private static Paragraph NewParagraph() => new Paragraph(new ParagraphProperties(new SpacingBetweenLines() { After = "200", Line = "276", LineRule = LineSpacingRuleValues.Auto }));
    }
}
