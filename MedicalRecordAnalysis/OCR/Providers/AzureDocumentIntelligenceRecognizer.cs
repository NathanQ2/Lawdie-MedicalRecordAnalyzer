using Azure;
using Azure.AI.DocumentIntelligence;

namespace MedicalRecordAnalysis.OCR.Providers;

/**
 * Recognizes printed and handwritten text with Azure AI Document Intelligence's Read model. Only the requested pages
 * are analyzed (and billed), so DirectTextProvider can send just the pages of a PDF that have no embedded text.
 * Handwriting and words read with low confidence are reported so citations from them can be flagged for review.
 */
public class AzureDocumentIntelligenceRecognizer : IPageRecognizer
{
    public const string EndpointVariable = "AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT";
    public const string KeyVariable = "AZURE_DOCUMENT_INTELLIGENCE_KEY";

    private const string ReadModel = "prebuilt-read";

    /**
     * Words below this confidence (0 to 1) are flagged as uncertain. Clear print usually scores above 0.95, while
     * smudged or handwritten words that are often misread fall below this.
     */
    public const float LowConfidenceThreshold = 0.8f;

    private readonly DocumentIntelligenceClient m_client;

    public AzureDocumentIntelligenceRecognizer(DocumentIntelligenceClient client)
    {
        m_client = client;
    }

    public AzureDocumentIntelligenceRecognizer(Uri endpoint, string apiKey)
        : this(new DocumentIntelligenceClient(endpoint, new AzureKeyCredential(apiKey)))
    {
    }

    /**
     * A recognizer configured from AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT and AZURE_DOCUMENT_INTELLIGENCE_KEY, or null
     * when either is missing so callers can run without OCR.
     */
    public static AzureDocumentIntelligenceRecognizer? FromEnvironment()
    {
        return Create(Environment.GetEnvironmentVariable(EndpointVariable), Environment.GetEnvironmentVariable(KeyVariable));
    }

    /**
     * A recognizer for the given endpoint and key, or null when either is missing.
     */
    public static AzureDocumentIntelligenceRecognizer? Create(string? endpoint, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException($"The Document Intelligence endpoint must be an https URL, but it is '{endpoint}'.");
        }

        return new AzureDocumentIntelligenceRecognizer(uri, apiKey);
    }

    public async Task<RecognizedPage[]> RecognizePages(FileInfo file, IReadOnlyList<int>? pageNumbers, CancellationToken cancellationToken = default)
    {
        BinaryData bytes = BinaryData.FromBytes(await File.ReadAllBytesAsync(file.FullName, cancellationToken));
        AnalyzeDocumentOptions options = new AnalyzeDocumentOptions(ReadModel, bytes);
        if (pageNumbers is not null)
        {
            options.Pages = string.Join(",", pageNumbers);
        }

        AnalyzeResult result;
        try
        {
            Operation<AnalyzeResult> operation = await m_client.AnalyzeDocumentAsync(WaitUntil.Completed, options, cancellationToken);
            result = operation.Value;
        }
        catch (RequestFailedException exception) when (exception.Status is 401 or 403)
        {
            throw new InvalidOperationException(
                $"The OCR service rejected the request. Check {EndpointVariable} and {KeyVariable}.", exception);
        }
        catch (RequestFailedException exception) when (exception.Status is 400 or 413 or 415)
        {
            // The service could not read this file (corrupt, unsupported, or too large), so it is skipped with the
            // reason rather than failing the whole ingest.
            throw new NotSupportedException($"The OCR service could not read the file: {exception.ErrorCode ?? exception.Message}", exception);
        }

        return ToPages(result);
    }

    /**
     * Converts the service's result to page texts. The Read model returns one text for the whole request, with each
     * page's span of it; handwriting styles and word confidences are moved into page-relative positions.
     */
    internal static RecognizedPage[] ToPages(AnalyzeResult result)
    {
        string content = result.Content ?? "";
        DocumentSpan[] handwriting = result.Styles
            .Where(style => style.IsHandwritten == true)
            .SelectMany(style => style.Spans)
            .ToArray();

        List<RecognizedPage> pages = [];
        foreach (DocumentPage page in result.Pages)
        {
            PageText pageText = new PageText(content, page.Spans);
            pages.Add(new RecognizedPage()
            {
                PageNumber = page.PageNumber,
                Text = pageText.Text,
                HandwrittenSpans = handwriting.SelectMany(pageText.Map).ToArray(),
                LowConfidenceSpans = page.Words
                    .Where(word => word.Confidence < LowConfidenceThreshold)
                    .SelectMany(word => pageText.Map(word.Span))
                    .ToArray(),
            });
        }

        return pages.ToArray();
    }

    /**
     * A page's text, built from its spans of the result's content, and the mapping from content offsets into it.
     */
    private sealed class PageText
    {
        private readonly List<(int ContentStart, int Length, int PageStart)> m_pieces = [];

        public string Text { get; }

        public PageText(string content, IEnumerable<DocumentSpan> spans)
        {
            List<string> parts = [];
            int pageStart = 0;
            foreach (DocumentSpan span in spans.OrderBy(span => span.Offset))
            {
                int start = Math.Clamp(span.Offset, 0, content.Length);
                int length = Math.Clamp(span.Length, 0, content.Length - start);
                if (parts.Count > 0)
                {
                    pageStart += 1; // The newline joining this span to the previous one
                }

                m_pieces.Add((start, length, pageStart));
                parts.Add(content.Substring(start, length));
                pageStart += length;
            }

            Text = string.Join("\n", parts);
        }

        /**
         * The parts of a content span that fall on this page, in page-text positions.
         */
        public IEnumerable<TextSpan> Map(DocumentSpan span)
        {
            foreach ((int contentStart, int length, int pageStart) in m_pieces)
            {
                int start = Math.Max(span.Offset, contentStart);
                int end = Math.Min(span.Offset + span.Length, contentStart + length);
                if (end > start)
                {
                    yield return new TextSpan(pageStart + start - contentStart, end - start);
                }
            }
        }
    }
}
