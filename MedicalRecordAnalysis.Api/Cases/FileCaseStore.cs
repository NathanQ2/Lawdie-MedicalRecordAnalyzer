using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MedicalRecordAnalysis.Caching;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Api.Cases;

/**
 * Keeps each case in its own folder under the root: meta.json, records/, cache/, case.json, and draft.json.
 * Case ids and record names are validated so a request can never reach a path outside its case folder.
 */
public sealed partial class FileCaseStore : ICaseStore
{
    private const string MetaFileName = "meta.json";
    private const string AnalysisFileName = "case.json";
    private const string DraftFileName = "draft.json";
    private const int MaxRecordNameLength = 200;

    private static readonly JsonSerializerOptions MetaJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly DirectoryInfo m_root;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> m_locks = new ConcurrentDictionary<string, SemaphoreSlim>();

    public FileCaseStore(DirectoryInfo root)
    {
        m_root = root;
        m_root.Create();
    }

    [GeneratedRegex("^[a-z0-9]{12}$")]
    private static partial Regex CaseIdPattern();

    public static bool IsValidCaseId(string caseId) => CaseIdPattern().IsMatch(caseId);

    /**
     * A record name must be a plain file name: no directories, not hidden, no invalid characters.
     */
    public static bool IsValidRecordName(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName)
            && fileName.Length <= MaxRecordNameLength
            && fileName == fileName.Trim()
            && Path.GetFileName(fileName) == fileName
            && !fileName.StartsWith('.')
            && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !fileName.Contains('\\');
    }

    public async Task<CaseMeta> Create(string name, MatterDetails? matter = null, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        CaseMeta meta = new CaseMeta()
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name.Trim(),
            DateOfLoss = matter?.DateOfLoss,
            Jurisdiction = matter?.Jurisdiction,
            County = matter?.County,
            CreatedAt = now,
            UpdatedAt = now,
        };

        CaseDirectory(meta.Id).CreateSubdirectory("records");
        await WriteMeta(meta, cancellationToken);
        return meta;
    }

    public async Task<IReadOnlyList<CaseMeta>> List(CancellationToken cancellationToken = default)
    {
        List<CaseMeta> cases = [];
        foreach (DirectoryInfo directory in m_root.GetDirectories())
        {
            if (IsValidCaseId(directory.Name) && await ReadMeta(directory.Name, cancellationToken) is CaseMeta meta)
            {
                cases.Add(meta);
            }
        }

        return cases.OrderByDescending(meta => meta.UpdatedAt).ToList();
    }

    public Task<CaseMeta?> Get(string caseId, CancellationToken cancellationToken = default)
    {
        return IsValidCaseId(caseId) ? ReadMeta(caseId, cancellationToken) : Task.FromResult<CaseMeta?>(null);
    }

    public async Task<CaseMeta?> Update(string caseId, Action<CaseMeta> change, CancellationToken cancellationToken = default)
    {
        if (!IsValidCaseId(caseId))
        {
            return null;
        }

        SemaphoreSlim caseLock = m_locks.GetOrAdd(caseId, _ => new SemaphoreSlim(1, 1));
        await caseLock.WaitAsync(cancellationToken);
        try
        {
            CaseMeta? meta = await ReadMeta(caseId, cancellationToken);
            if (meta is null)
            {
                return null;
            }

            change(meta);
            meta.UpdatedAt = DateTimeOffset.UtcNow;
            await WriteMeta(meta, cancellationToken);
            return meta;
        }
        finally
        {
            caseLock.Release();
        }
    }

    public Task<bool> Delete(string caseId, CancellationToken cancellationToken = default)
    {
        DirectoryInfo directory = CaseDirectory(caseId);
        if (!IsValidCaseId(caseId) || !directory.Exists)
        {
            return Task.FromResult(false);
        }

        directory.Delete(recursive: true);
        m_locks.TryRemove(caseId, out _);
        return Task.FromResult(true);
    }

    public IReadOnlyList<FileInfo> ListRecords(string caseId)
    {
        DirectoryInfo records = RecordsDirectory(caseId);
        return records.Exists ? MedicalRecordAnalyzer.GetRecordFiles(records) : [];
    }

    public FileInfo? GetRecord(string caseId, string fileName)
    {
        if (!IsValidCaseId(caseId) || !IsValidRecordName(fileName))
        {
            return null;
        }

        FileInfo file = new FileInfo(Path.Combine(RecordsDirectory(caseId).FullName, fileName));
        return file.Exists ? file : null;
    }

    public async Task<FileInfo> SaveRecord(string caseId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!IsValidCaseId(caseId) || !IsValidRecordName(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a valid record name.", nameof(fileName));
        }

        DirectoryInfo records = RecordsDirectory(caseId);
        records.Create();
        FileInfo file = new FileInfo(Path.Combine(records.FullName, fileName));

        string temporary = Path.Combine(records.FullName, $".{fileName}.upload");
        await using (FileStream stream = File.Create(temporary))
        {
            await content.CopyToAsync(stream, cancellationToken);
        }

        File.Move(temporary, file.FullName, overwrite: true);
        file.Refresh();
        return file;
    }

    public bool DeleteRecord(string caseId, string fileName)
    {
        FileInfo? file = GetRecord(caseId, fileName);
        if (file is null)
        {
            return false;
        }

        file.Delete();
        return true;
    }

    public IDocumentCache DocumentCache(string caseId)
    {
        return new FileDocumentCache(new DirectoryInfo(Path.Combine(CaseDirectory(caseId).FullName, "cache")));
    }

    public Task SaveAnalysis(string caseId, PICase piCase, CancellationToken cancellationToken = default)
    {
        return MedicalRecordAnalyzer.SaveJson(piCase, CaseFile(caseId, AnalysisFileName), cancellationToken);
    }

    public async Task<PICase?> LoadAnalysis(string caseId, CancellationToken cancellationToken = default)
    {
        FileInfo file = CaseFile(caseId, AnalysisFileName);
        return IsValidCaseId(caseId) && file.Exists ? await MedicalRecordAnalyzer.LoadCase(file, cancellationToken) : null;
    }

    public Task SaveDraft(string caseId, DemandDraft draft, CancellationToken cancellationToken = default)
    {
        return MedicalRecordAnalyzer.SaveJson(draft, CaseFile(caseId, DraftFileName), cancellationToken);
    }

    public async Task<DemandDraft?> LoadDraft(string caseId, CancellationToken cancellationToken = default)
    {
        FileInfo file = CaseFile(caseId, DraftFileName);
        return IsValidCaseId(caseId) && file.Exists ? await MedicalRecordAnalyzer.LoadDraft(file, cancellationToken) : null;
    }

    private DirectoryInfo CaseDirectory(string caseId) => new DirectoryInfo(Path.Combine(m_root.FullName, caseId));

    private DirectoryInfo RecordsDirectory(string caseId) => new DirectoryInfo(Path.Combine(CaseDirectory(caseId).FullName, "records"));

    private FileInfo CaseFile(string caseId, string fileName) => new FileInfo(Path.Combine(CaseDirectory(caseId).FullName, fileName));

    private async Task<CaseMeta?> ReadMeta(string caseId, CancellationToken cancellationToken)
    {
        FileInfo file = CaseFile(caseId, MetaFileName);
        if (!file.Exists)
        {
            return null;
        }

        await using FileStream stream = file.OpenRead();
        return await JsonSerializer.DeserializeAsync<CaseMeta>(stream, MetaJsonOptions, cancellationToken);
    }

    private async Task WriteMeta(CaseMeta meta, CancellationToken cancellationToken)
    {
        FileInfo file = CaseFile(meta.Id, MetaFileName);
        string temporary = file.FullName + ".tmp";
        await using (FileStream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, meta, MetaJsonOptions, cancellationToken);
        }

        File.Move(temporary, file.FullName, overwrite: true);
    }
}
