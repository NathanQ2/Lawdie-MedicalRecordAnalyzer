using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis;

/**
 * Renders a demand draft as a Word document: the letter with a Word footnote citing the source record, page, and
 * quote after each cited claim, section headings and expense items formatted, placeholders highlighted, and the
 * attorney review section on its own page.
 */
public static partial class DemandLetterDocxRenderer
{
    private const string FontSizeBody = "24"; // Half-points: 12 pt.
    private const string FontSizeFootnote = "18"; // 9 pt.

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

            new NarrativeWriter(body, footnotesPart.Footnotes).Write(draft);

            body.Append(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
            body.Append(StyledParagraph(DemandLetterRenderer.ReviewTitle, bold: true, size: "28"));
            AppendSection(body, "Contradictions between records", piCase.Contradictions.Select(contradiction =>
                (DemandLetterRenderer.DescribeContradiction(contradiction), contradiction.Citations.Select(DemandLetterRenderer.DescribeSource).ToList())));
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
     * Writes the letter into paragraphs: blank lines separate paragraphs, single line breaks stay within one (as in
     * an address block), "## " lines become bold section headings, and "- " lines become bulleted items. A footnote
     * follows each cited claim.
     */
    private sealed class NarrativeWriter
    {
        private enum Kind { Normal, Heading, Bullet }

        private readonly Body m_body;
        private readonly Footnotes m_footnotes;
        private Paragraph? m_paragraph;
        private Kind m_kind = Kind.Normal;
        private bool m_atLineStart = true;
        private int m_newlines;
        private int m_nextFootnoteId = 1;

        public NarrativeWriter(Body body, Footnotes footnotes)
        {
            m_body = body;
            m_footnotes = footnotes;
        }

        public void Write(DemandDraft draft)
        {
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
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    m_newlines++;
                    m_atLineStart = true;
                }

                string line = lines[i];
                if (m_atLineStart)
                {
                    line = line.TrimStart(' ', '\t', '\r');
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    line = StartLine(line);
                }

                if (line.Length > 0)
                {
                    AppendRuns(line.TrimEnd('\r'));
                    m_atLineStart = false;
                    m_newlines = 0;
                }
            }
        }

        /**
         * Starts a new paragraph or line break as the preceding line breaks and the line's marker require, and
         * returns the line without its marker.
         */
        private string StartLine(string line)
        {
            Kind kind = line.StartsWith("## ", StringComparison.Ordinal) ? Kind.Heading
                : line.StartsWith("- ", StringComparison.Ordinal) ? Kind.Bullet
                : Kind.Normal;

            bool newParagraph = m_paragraph is null || m_newlines >= 2 || kind != Kind.Normal || m_kind != Kind.Normal;
            if (newParagraph)
            {
                m_kind = kind;
                m_paragraph = NewParagraph(kind);
                m_body.Append(m_paragraph);
            }
            else if (m_newlines == 1)
            {
                m_paragraph!.Append(new Run(new Break()));
            }

            return kind switch
            {
                Kind.Heading => line[3..],
                Kind.Bullet => "•\t" + line[2..],
                _ => line,
            };
        }

        private void AppendRuns(string line)
        {
            m_paragraph ??= AppendNewParagraph();
            foreach (string part in Placeholder().Split(line).Where(part => part.Length > 0))
            {
                bool placeholder = Placeholder().IsMatch(part);
                m_paragraph.Append(TextRun(part, bold: placeholder || m_kind == Kind.Heading, highlight: placeholder,
                    size: m_kind == Kind.Heading ? "26" : FontSizeBody));
            }
        }

        private Paragraph AppendNewParagraph()
        {
            Paragraph paragraph = NewParagraph(Kind.Normal);
            m_body.Append(paragraph);
            return paragraph;
        }

        private void AppendFootnote(string description)
        {
            int id = m_nextFootnoteId++;
            (m_paragraph ??= AppendNewParagraph()).Append(new Run(Superscript(), new FootnoteReference() { Id = id }));
            m_footnotes.Append(new Footnote(new Paragraph(
                new Run(Superscript(), new FootnoteReferenceMark()),
                TextRun($" {description}", size: FontSizeFootnote))) { Id = id });
        }

        private static RunProperties Superscript() => new RunProperties(new VerticalTextAlignment() { Val = VerticalPositionValues.Superscript });

        private static Paragraph NewParagraph(Kind kind)
        {
            ParagraphProperties properties = kind switch
            {
                Kind.Heading => new ParagraphProperties(new KeepNext(), new SpacingBetweenLines() { Before = "240", After = "120" }),
                Kind.Bullet => new ParagraphProperties(new SpacingBetweenLines() { After = "60" }, new Indentation() { Left = "720", Hanging = "360" }),
                _ => new ParagraphProperties(new SpacingBetweenLines() { After = "200", Line = "276", LineRule = LineSpacingRuleValues.Auto }),
            };
            return new Paragraph(properties);
        }
    }
}
