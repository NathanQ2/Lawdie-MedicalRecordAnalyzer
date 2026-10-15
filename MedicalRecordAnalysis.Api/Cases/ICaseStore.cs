using MedicalRecordAnalysis.Caching;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Api.Cases;

/**
 * Stores cases: their metadata, uploaded records, ingest cache, and results, plus the firm's profile that every
 * case's letter uses. The local implementation keeps one folder per case; a cloud implementation would keep the
 * same shape in object storage and materialize records as local files for processing.
 */
public interface ICaseStore
{
    public Task<CaseMeta> Create(string name, MatterDetails? matter = null, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<CaseMeta>> List(CancellationToken cancellationToken = default);
    public Task<CaseMeta?> Get(string caseId, CancellationToken cancellationToken = default);

    /**
     * Applies a change to a case's metadata under a per-case lock and saves it. Returns null if the case does
     * not exist.
     */
    public Task<CaseMeta?> Update(string caseId, Action<CaseMeta> change, CancellationToken cancellationToken = default);
    public Task<bool> Delete(string caseId, CancellationToken cancellationToken = default);

    public IReadOnlyList<FileInfo> ListRecords(string caseId);
    public FileInfo? GetRecord(string caseId, string fileName);
    public Task<FileInfo> SaveRecord(string caseId, string fileName, Stream content, CancellationToken cancellationToken = default);
    public bool DeleteRecord(string caseId, string fileName);

    public IDocumentCache DocumentCache(string caseId);

    public Task SaveAnalysis(string caseId, PICase piCase, CancellationToken cancellationToken = default);
    public Task<PICase?> LoadAnalysis(string caseId, CancellationToken cancellationToken = default);
    public Task SaveDraft(string caseId, DemandDraft draft, CancellationToken cancellationToken = default);
    public Task<DemandDraft?> LoadDraft(string caseId, CancellationToken cancellationToken = default);

    /** The firm's profile, or an empty one until the attorney fills it in. */
    public Task<FirmProfile> LoadFirmProfile(CancellationToken cancellationToken = default);
    public Task SaveFirmProfile(FirmProfile profile, CancellationToken cancellationToken = default);
}
