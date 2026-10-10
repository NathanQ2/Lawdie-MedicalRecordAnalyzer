using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using TextractS3Object = Amazon.Textract.Model.S3Object;

namespace MedicalRecordAnalysis.OCR.Providers;

public class TextractProvider : IOCRProvider
{
    private static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
    };

    private static readonly HashSet<string> MultipageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".tif",
        ".tiff",
    };

    private const string BucketEnvironmentVariable = "TEXTRACT_S3_BUCKET";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int MaxPollAttempts = 120;

    private readonly IAmazonTextract m_textractClient;
    private readonly IAmazonS3 m_s3Client;
    private readonly string? m_bucketName;

    public TextractProvider(IAmazonTextract? textractClient = null, IAmazonS3? s3Client = null, string? bucketName = null)
    {
        m_textractClient = textractClient ?? new AmazonTextractClient();
        m_s3Client = s3Client ?? new AmazonS3Client();
        m_bucketName = bucketName ?? Environment.GetEnvironmentVariable(BucketEnvironmentVariable);
    }

    public async Task<OCRResult> DetectTextFromFile(FileInfo file, CancellationToken cancellationToken = default)
    {
        if (!file.Exists)
        {
            throw new FileNotFoundException($"The source file does not exist: {file.FullName}", file.FullName);
        }

        string extension = file.Extension;
        bool isImage = ImageExtensions.Contains(extension);
        bool isMultipage = MultipageExtensions.Contains(extension);

        if (!isImage && !isMultipage)
        {
            throw new NotSupportedException($"Unsupported file type for OCR: '{extension}' ({file.FullName}).");
        }

        List<Block> blocks;
        if (isMultipage && m_bucketName is not null)
        {
            blocks = await DetectMultipageText(file, m_bucketName, cancellationToken);
        }
        else
        {
            blocks = await DetectSinglePageText(file, cancellationToken);
        }

        (string text, int[] pageStarts) = BuildText(blocks);
        return new OCRResult()
        {
            File = file,
            Text = text,
            PageStartPositions = pageStarts,
        };
    }

    private async Task<List<Block>> DetectSinglePageText(FileInfo file, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
        DetectDocumentTextRequest request = new DetectDocumentTextRequest()
        {
            Document = new Document()
            {
                Bytes = new MemoryStream(bytes),
            },
        };

        try
        {
            DetectDocumentTextResponse response = await m_textractClient.DetectDocumentTextAsync(request, cancellationToken);
            return response.Blocks;
        }
        catch (AmazonTextractException exception)
        {
            throw new InvalidOperationException($"Textract could not read '{file.FullName}'.", exception);
        }
    }

    private async Task<List<Block>> DetectMultipageText(FileInfo file, string bucketName, CancellationToken cancellationToken)
    {
        string key = $"{Guid.NewGuid():N}{file.Extension}";

        await UploadToBucket(file, bucketName, key, cancellationToken);
        try
        {
            string jobId = await StartTextDetectionJob(bucketName, key, cancellationToken);
            return await WaitForTextDetectionJob(jobId, cancellationToken);
        }
        finally
        {
            await DeleteFromBucket(bucketName, key);
        }
    }

    private async Task UploadToBucket(FileInfo file, string bucketName, string key, CancellationToken cancellationToken)
    {
        using FileStream stream = file.OpenRead();
        PutObjectRequest request = new PutObjectRequest()
        {
            BucketName = bucketName,
            Key = key,
            InputStream = stream,
            ContentType = "application/octet-stream",
        };

        try
        {
            await m_s3Client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw new InvalidOperationException($"Could not upload '{file.FullName}' to S3 bucket '{bucketName}'.", exception);
        }
    }

    private async Task<string> StartTextDetectionJob(string bucketName, string key, CancellationToken cancellationToken)
    {
        StartDocumentTextDetectionRequest request = new StartDocumentTextDetectionRequest()
        {
            DocumentLocation = new DocumentLocation()
            {
                S3Object = new TextractS3Object()
                {
                    Bucket = bucketName,
                    Name = key,
                },
            },
        };

        try
        {
            StartDocumentTextDetectionResponse response = await m_textractClient.StartDocumentTextDetectionAsync(request, cancellationToken);
            return response.JobId;
        }
        catch (AmazonTextractException exception)
        {
            throw new InvalidOperationException($"Could not start a text detection job for '{key}' in bucket '{bucketName}'.", exception);
        }
    }

    private async Task<List<Block>> WaitForTextDetectionJob(string jobId, CancellationToken cancellationToken)
    {
        List<Block> blocks = [];
        string? nextToken = null;
        int pollAttempts = 0;

        while (true)
        {
            GetDocumentTextDetectionRequest request = new GetDocumentTextDetectionRequest()
            {
                JobId = jobId,
                NextToken = nextToken,
            };

            GetDocumentTextDetectionResponse response;
            try
            {
                response = await m_textractClient.GetDocumentTextDetectionAsync(request, cancellationToken);
            }
            catch (AmazonTextractException exception)
            {
                throw new InvalidOperationException($"Could not fetch results for text detection job '{jobId}'.", exception);
            }

            if (response.JobStatus == JobStatus.FAILED)
            {
                throw new InvalidOperationException($"Textract text detection job '{jobId}' failed: {response.StatusMessage}");
            }

            if (response.JobStatus == JobStatus.IN_PROGRESS)
            {
                pollAttempts++;
                if (pollAttempts > MaxPollAttempts)
                {
                    throw new TimeoutException($"Textract text detection job '{jobId}' did not complete in time.");
                }

                await Task.Delay(PollInterval, cancellationToken);
                continue;
            }

            blocks.AddRange(response.Blocks);

            if (response.NextToken is null)
            {
                return blocks;
            }

            nextToken = response.NextToken;
        }
    }

    private async Task DeleteFromBucket(string bucketName, string key)
    {
        try
        {
            await m_s3Client.DeleteObjectAsync(bucketName, key);
        }
        catch (AmazonS3Exception)
        {
            // Cleaning up the temporary upload must not fail an otherwise successful OCR run.
        }
    }

    /**
     * Joins LINE blocks in reading order, one line per row and a blank line between pages, and records the
     * offset where each page begins.
     */
    private static (string Text, int[] PageStartPositions) BuildText(List<Block> blocks)
    {
        IEnumerable<IGrouping<int, Block>> pages = blocks
            .Where(block => block.BlockType == BlockType.LINE && block.Text is not null)
            .GroupBy(block => block.Page ?? 1)
            .OrderBy(page => page.Key);

        StringBuilder text = new StringBuilder();
        List<int> pageStarts = [];
        foreach (IGrouping<int, Block> page in pages)
        {
            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            pageStarts.Add(text.Length);
            text.Append(string.Join("\n", page
                .OrderBy(block => block.Geometry?.BoundingBox?.Top ?? 0)
                .ThenBy(block => block.Geometry?.BoundingBox?.Left ?? 0)
                .Select(line => line.Text)));
        }

        return (text.ToString(), pageStarts.ToArray());
    }
}
