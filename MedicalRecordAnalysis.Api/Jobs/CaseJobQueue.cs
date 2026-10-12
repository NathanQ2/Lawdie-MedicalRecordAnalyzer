using System.Collections.Concurrent;
using System.Threading.Channels;
using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Api.Jobs;

public interface IJobQueue
{
    /**
     * Queues an ingest or draft job for the case. Throws JobConflictException if the case already has a job
     * queued or running, and returns null if the case does not exist.
     */
    public Task<JobStatus?> Enqueue(string caseId, JobKind kind, CancellationToken cancellationToken = default);

    /**
     * Cancels the case's queued or running job. Returns false if there was nothing to cancel.
     */
    public Task<bool> Cancel(string caseId, CancellationToken cancellationToken = default);

    /**
     * The case's latest job with live progress applied while it runs.
     */
    public JobStatus? CurrentJob(CaseMeta meta);
}

public class JobConflictException : Exception
{
    public JobConflictException(string message) : base(message)
    {
    }
}

/**
 * Runs ingest and draft jobs one at a time on a background worker. Job status is saved with the case; live
 * progress is kept in memory and merged into the status when it is read.
 */
public sealed class CaseJobQueue : BackgroundService, IJobQueue
{
    private readonly Channel<(string CaseId, string JobId)> m_channel = Channel.CreateUnbounded<(string, string)>();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> m_running = new ConcurrentDictionary<string, CancellationTokenSource>();
    private readonly ConcurrentDictionary<string, PipelineProgress> m_progress = new ConcurrentDictionary<string, PipelineProgress>();
    private readonly ICaseStore m_store;
    private readonly IServiceScopeFactory m_scopes;
    private readonly ILogger<CaseJobQueue> m_logger;

    public CaseJobQueue(ICaseStore store, IServiceScopeFactory scopes, ILogger<CaseJobQueue> logger)
    {
        m_store = store;
        m_scopes = scopes;
        m_logger = logger;
    }

    public async Task<JobStatus?> Enqueue(string caseId, JobKind kind, CancellationToken cancellationToken = default)
    {
        JobStatus job = new JobStatus()
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = kind,
            State = JobState.Queued,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        bool conflict = false;
        CaseMeta? meta = await m_store.Update(caseId, meta =>
        {
            if (meta.CurrentJob?.IsActive == true)
            {
                conflict = true;
                return;
            }

            meta.CurrentJob = job;
        }, cancellationToken);

        if (meta is null)
        {
            return null;
        }

        if (conflict)
        {
            throw new JobConflictException("This case is already being analyzed or drafted.");
        }

        await m_channel.Writer.WriteAsync((caseId, job.Id), cancellationToken);
        return job;
    }

    public async Task<bool> Cancel(string caseId, CancellationToken cancellationToken = default)
    {
        bool cancelled = false;
        await m_store.Update(caseId, meta =>
        {
            if (meta.CurrentJob?.State == JobState.Queued)
            {
                meta.CurrentJob.State = JobState.Cancelled;
                meta.CurrentJob.FinishedAt = DateTimeOffset.UtcNow;
                cancelled = true;
            }
        }, cancellationToken);

        if (!cancelled && m_running.TryGetValue(caseId, out CancellationTokenSource? running))
        {
            await running.CancelAsync();
            cancelled = true;
        }

        return cancelled;
    }

    public JobStatus? CurrentJob(CaseMeta meta)
    {
        JobStatus? job = meta.CurrentJob;
        if (job?.State == JobState.Running && m_progress.TryGetValue(meta.Id, out PipelineProgress? progress))
        {
            job.Stage = progress.Stage;
            job.Completed = progress.Completed;
            job.Total = progress.Total;
            job.CurrentFile = progress.CurrentFile;
        }

        return job;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Jobs left queued or running by a previous server process can no longer finish.
        foreach (CaseMeta meta in await m_store.List(cancellationToken))
        {
            if (meta.CurrentJob?.IsActive == true)
            {
                await m_store.Update(meta.Id, current => Finish(current, JobState.Failed, "Interrupted because the server stopped."), cancellationToken);
            }
        }

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach ((string caseId, string jobId) in m_channel.Reader.ReadAllAsync(stoppingToken))
            {
                await Run(caseId, jobId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task Run(string caseId, string jobId, CancellationToken stoppingToken)
    {
        // Register the cancellation source before the job is marked running, so a cancel can never miss it.
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        m_running[caseId] = cancellation;

        bool started = false;
        CaseMeta? meta = await m_store.Update(caseId, current =>
        {
            if (current.CurrentJob?.Id == jobId && current.CurrentJob.State == JobState.Queued)
            {
                current.CurrentJob.State = JobState.Running;
                current.CurrentJob.StartedAt = DateTimeOffset.UtcNow;
                started = true;
            }
        }, stoppingToken);

        // The case was deleted or the job cancelled while it waited in the queue.
        if (meta is null || !started)
        {
            m_running.TryRemove(caseId, out _);
            return;
        }

        JobKind kind = meta.CurrentJob!.Kind;
        try
        {
            using IServiceScope scope = m_scopes.CreateScope();
            MedicalRecordAnalyzer analyzer = scope.ServiceProvider.GetRequiredService<MedicalRecordAnalyzer>();
            Progress progress = new Progress(m_progress, caseId);

            if (kind == JobKind.Ingest)
            {
                await RunIngest(caseId, analyzer, progress, cancellation.Token);
            }
            else
            {
                await RunDraft(caseId, analyzer, progress, cancellation.Token);
            }

            await m_store.Update(caseId, current => Finish(current, JobState.Succeeded, null), CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            string? reason = stoppingToken.IsCancellationRequested ? "Interrupted because the server stopped." : null;
            await m_store.Update(caseId, current => Finish(current, JobState.Cancelled, reason), CancellationToken.None);
        }
        catch (Exception exception)
        {
            m_logger.LogError(exception, "{Kind} job {JobId} for case {CaseId} failed.", kind, jobId, caseId);
            await m_store.Update(caseId, current => Finish(current, JobState.Failed, exception.Message), CancellationToken.None);
        }
        finally
        {
            m_running.TryRemove(caseId, out _);
            m_progress.TryRemove(caseId, out _);
        }
    }

    private async Task RunIngest(string caseId, MedicalRecordAnalyzer analyzer, Progress progress, CancellationToken cancellationToken)
    {
        IReadOnlyList<FileInfo> records = m_store.ListRecords(caseId);
        if (records.Count == 0)
        {
            throw new InvalidOperationException("Add at least one document before analyzing.");
        }

        PICase piCase = await analyzer.Ingest(records, m_store.DocumentCache(caseId), progress, cancellationToken);
        await m_store.SaveAnalysis(caseId, piCase, CancellationToken.None);
        await m_store.Update(caseId, meta => meta.AnalysisUpdatedAt = DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private async Task RunDraft(string caseId, MedicalRecordAnalyzer analyzer, Progress progress, CancellationToken cancellationToken)
    {
        PICase piCase = await m_store.LoadAnalysis(caseId, cancellationToken)
            ?? throw new InvalidOperationException("Analyze the documents before drafting.");

        DemandDraft draft = await analyzer.Draft(piCase, progress, cancellationToken);
        await m_store.SaveDraft(caseId, draft, CancellationToken.None);
        await m_store.Update(caseId, meta => meta.DraftUpdatedAt = DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private static void Finish(CaseMeta meta, JobState state, string? error)
    {
        if (meta.CurrentJob is null)
        {
            return;
        }

        meta.CurrentJob.State = state;
        meta.CurrentJob.Error = error;
        meta.CurrentJob.FinishedAt = DateTimeOffset.UtcNow;
    }

    /**
     * Keeps the latest progress for a case in memory, synchronously, so reads always see the newest report.
     */
    private sealed class Progress : IProgress<PipelineProgress>
    {
        private readonly ConcurrentDictionary<string, PipelineProgress> m_latest;
        private readonly string m_caseId;

        public Progress(ConcurrentDictionary<string, PipelineProgress> latest, string caseId)
        {
            m_latest = latest;
            m_caseId = caseId;
        }

        public void Report(PipelineProgress value) => m_latest[m_caseId] = value;
    }
}
