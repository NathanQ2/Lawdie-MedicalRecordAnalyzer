using System.Security.Cryptography;
using System.Text.Json;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.Retrieval;

namespace MedicalRecordAnalysis.Caching;

/**
 * One record's ingest results, keyed by a hash of the file's contents, so an unchanged record never has to be
 * extracted or summarized again.
 */
public class CachedDocument
{
    public required string ContentHash { get; init; }
    public required DocumentSummary Summary { get; init; }
    public required SourceDocument Source { get; init; }

    /**
     * Returns a copy whose citations and source point at filePath, for when identical content was cached under
     * another path (e.g. the case folder moved).
     */
    public CachedDocument WithFilePath(string filePath)
    {
        if (Source.FilePath == filePath)
        {
            return this;
        }

        return new CachedDocument()
        {
            ContentHash = ContentHash,
            Summary = new DocumentSummary()
            {
                Summary = Summary.Summary,
                Citations = Summary.Citations.Select(citation => new Citation()
                {
                    FilePath = filePath,
                    CitedText = citation.CitedText,
                    StartPosition = citation.StartPosition,
                    Claim = citation.Claim,
                    PageNumber = citation.PageNumber,
                    EndPageNumber = citation.EndPageNumber,
                }).ToArray(),
            },
            Source = new SourceDocument()
            {
                FilePath = filePath,
                Passages = Source.Passages,
                PageStartPositions = Source.PageStartPositions,
            },
        };
    }

    public static async Task<string> HashFile(FileInfo file, CancellationToken cancellationToken = default)
    {
        await using FileStream stream = file.OpenRead();
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }
}

public interface IDocumentCache
{
    public Task<CachedDocument?> Get(string contentHash, CancellationToken cancellationToken = default);
    public Task Set(CachedDocument document, CancellationToken cancellationToken = default);
}

/**
 * Stores each cached document as <hash>.json in a directory. An unreadable entry counts as a miss.
 */
public class FileDocumentCache : IDocumentCache
{
    private readonly DirectoryInfo m_directory;

    public FileDocumentCache(DirectoryInfo directory)
    {
        m_directory = directory;
    }

    public async Task<CachedDocument?> Get(string contentHash, CancellationToken cancellationToken = default)
    {
        FileInfo file = EntryFile(contentHash);
        if (!file.Exists)
        {
            return null;
        }

        try
        {
            await using FileStream stream = file.OpenRead();
            CachedDocument? document = await JsonSerializer.DeserializeAsync<CachedDocument>(stream, cancellationToken: cancellationToken);
            return document?.ContentHash == contentHash ? document : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task Set(CachedDocument document, CancellationToken cancellationToken = default)
    {
        m_directory.Create();
        FileInfo file = EntryFile(document.ContentHash);

        // Write to a temporary file first so an interrupted write never leaves a half-written entry.
        string temporary = file.FullName + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, document, cancellationToken: cancellationToken);
        }

        File.Move(temporary, file.FullName, overwrite: true);
    }

    private FileInfo EntryFile(string contentHash)
    {
        return new FileInfo(Path.Combine(m_directory.FullName, $"{contentHash}.json"));
    }
}
