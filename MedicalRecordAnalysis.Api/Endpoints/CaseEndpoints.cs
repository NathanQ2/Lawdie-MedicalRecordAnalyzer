using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.Api.Jobs;
using MedicalRecordAnalysis.LLM;

namespace MedicalRecordAnalysis.Api.Endpoints;

public static class CaseEndpoints
{
    private const int MaxCaseNameLength = 200;

    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder cases = app.MapGroup("/api/cases");

        cases.MapGet("/", ListCases);
        cases.MapPost("/", CreateCase);
        cases.MapGet("/{caseId}", GetCase);
        cases.MapDelete("/{caseId}", DeleteCase);

        cases.MapGet("/{caseId}/records", ListRecords);
        // Cross-site requests are blocked by the client header check instead of antiforgery tokens.
        cases.MapPost("/{caseId}/records", UploadRecords).DisableAntiforgery();
        cases.MapGet("/{caseId}/records/{fileName}", GetRecord);
        cases.MapDelete("/{caseId}/records/{fileName}", DeleteRecord);

        cases.MapPost("/{caseId}/ingest", (string caseId, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken) =>
            StartJob(caseId, JobKind.Ingest, store, jobs, cancellationToken));
        cases.MapPost("/{caseId}/draft", (string caseId, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken) =>
            StartJob(caseId, JobKind.Draft, store, jobs, cancellationToken));
        cases.MapGet("/{caseId}/jobs/current", GetCurrentJob);
        cases.MapPost("/{caseId}/jobs/cancel", CancelJob);

        cases.MapGet("/{caseId}/analysis", GetAnalysis);
        cases.MapGet("/{caseId}/draft", GetDraft);
        cases.MapGet("/{caseId}/draft/export", ExportDraft);

        cases.MapGet("/{caseId}/review", GetReview);
        cases.MapPut("/{caseId}/review/{itemId}", UpdateReview);
    }

    private static async Task<IResult> ListCases(ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        IReadOnlyList<CaseMeta> cases = await store.List(cancellationToken);
        return Results.Ok(cases.Select(meta => new CaseListItemDto(meta.Id, meta.Name, meta.CreatedAt, meta.UpdatedAt,
            store.ListRecords(meta.Id).Count, meta.AnalysisUpdatedAt is not null, meta.DraftUpdatedAt is not null, jobs.CurrentJob(meta))));
    }

    private static async Task<IResult> CreateCase(CreateCaseRequest request, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        string name = request.Name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxCaseNameLength)
        {
            return Error(StatusCodes.Status400BadRequest, $"A case name of 1 to {MaxCaseNameLength} characters is required.");
        }

        CaseMeta meta = await store.Create(name, cancellationToken);
        return Results.Created($"/api/cases/{meta.Id}", await ToCaseDto(meta, store, jobs, cancellationToken));
    }

    private static async Task<IResult> GetCase(string caseId, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        return meta is null ? CaseNotFound() : Results.Ok(await ToCaseDto(meta, store, jobs, cancellationToken));
    }

    private static async Task<IResult> DeleteCase(string caseId, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        if (meta.CurrentJob?.IsActive == true)
        {
            return JobActive();
        }

        await store.Delete(caseId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListRecords(string caseId, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        return meta is null ? CaseNotFound() : Results.Ok(await ToRecordDtos(meta, store, cancellationToken));
    }

    private static async Task<IResult> UploadRecords(string caseId, IFormFileCollection files, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        if (meta.CurrentJob?.IsActive == true)
        {
            return JobActive();
        }

        if (files.Count == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "No files were uploaded.");
        }

        string[] invalid = files.Select(file => file.FileName).Where(name => !FileCaseStore.IsValidRecordName(name)).ToArray();
        if (invalid.Length > 0)
        {
            return Error(StatusCodes.Status400BadRequest, $"Invalid file names: {string.Join(", ", invalid)}");
        }

        foreach (IFormFile file in files)
        {
            await using Stream content = file.OpenReadStream();
            await store.SaveRecord(caseId, file.FileName, content, cancellationToken);
        }

        meta = await store.Update(caseId, current => current.RecordsChangedAt = DateTimeOffset.UtcNow, cancellationToken);
        return Results.Ok(await ToRecordDtos(meta!, store, cancellationToken));
    }

    private static IResult GetRecord(string caseId, string fileName, ICaseStore store, HttpContext context)
    {
        FileInfo? file = store.GetRecord(caseId, fileName);
        if (file is null)
        {
            return Error(StatusCodes.Status404NotFound, "Document not found.");
        }

        // Records are untrusted uploads: never let the browser sniff them into active content. Only PDF, text, and
        // image types are served as such (anything else downloads). Chrome will not show PDFs in a sandboxed page,
        // so the sandbox policy applies to every other type.
        string contentType = ContentType(file);
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (contentType != "application/pdf")
        {
            context.Response.Headers.ContentSecurityPolicy = "sandbox";
        }

        return Results.File(file.FullName, contentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> DeleteRecord(string caseId, string fileName, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        if (meta.CurrentJob?.IsActive == true)
        {
            return JobActive();
        }

        if (!store.DeleteRecord(caseId, fileName))
        {
            return Error(StatusCodes.Status404NotFound, "Document not found.");
        }

        await store.Update(caseId, current => current.RecordsChangedAt = DateTimeOffset.UtcNow, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> StartJob(string caseId, JobKind kind, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        if (kind == JobKind.Ingest && store.ListRecords(caseId).Count == 0)
        {
            return Error(StatusCodes.Status400BadRequest, "Upload at least one document before running ingest.");
        }

        if (kind == JobKind.Draft && meta.AnalysisUpdatedAt is null)
        {
            return Error(StatusCodes.Status409Conflict, "Run ingest before drafting.");
        }

        try
        {
            JobStatus? job = await jobs.Enqueue(caseId, kind, cancellationToken);
            return job is null ? CaseNotFound() : Results.Accepted($"/api/cases/{caseId}/jobs/current", job);
        }
        catch (JobConflictException exception)
        {
            return Error(StatusCodes.Status409Conflict, exception.Message);
        }
    }

    private static async Task<IResult> GetCurrentJob(string caseId, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        JobStatus? job = jobs.CurrentJob(meta);
        return job is null ? Results.NoContent() : Results.Ok(job);
    }

    private static async Task<IResult> CancelJob(string caseId, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        if (await store.Get(caseId, cancellationToken) is null)
        {
            return CaseNotFound();
        }

        return await jobs.Cancel(caseId, cancellationToken)
            ? Results.Accepted($"/api/cases/{caseId}/jobs/current")
            : Error(StatusCodes.Status409Conflict, "There is no queued or running job to cancel.");
    }

    private static async Task<IResult> GetAnalysis(string caseId, ICaseStore store, CancellationToken cancellationToken)
    {
        if (await store.Get(caseId, cancellationToken) is null)
        {
            return CaseNotFound();
        }

        PICase? analysis = await store.LoadAnalysis(caseId, cancellationToken);
        return analysis is null ? Error(StatusCodes.Status404NotFound, "This case has not been ingested yet.") : Results.Ok(analysis);
    }

    private static async Task<IResult> GetDraft(string caseId, ICaseStore store, CancellationToken cancellationToken)
    {
        if (await store.Get(caseId, cancellationToken) is null)
        {
            return CaseNotFound();
        }

        DemandDraft? draft = await store.LoadDraft(caseId, cancellationToken);
        return draft is null ? Error(StatusCodes.Status404NotFound, "This case has not been drafted yet.") : Results.Ok(draft);
    }

    private static async Task<IResult> ExportDraft(string caseId, string? format, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        DraftFormat? draftFormat = (format ?? "md").ToLowerInvariant() switch
        {
            "md" => DraftFormat.Markdown,
            "txt" => DraftFormat.PlainText,
            "docx" => DraftFormat.Docx,
            _ => null,
        };
        if (draftFormat is null)
        {
            return Error(StatusCodes.Status400BadRequest, "The format must be 'md', 'txt', or 'docx'.");
        }

        DemandDraft? draft = await store.LoadDraft(caseId, cancellationToken);
        PICase? analysis = await store.LoadAnalysis(caseId, cancellationToken);
        if (draft is null || analysis is null)
        {
            return Error(StatusCodes.Status404NotFound, "This case has not been drafted yet.");
        }

        (string contentType, string extension) = draftFormat.Value switch
        {
            DraftFormat.Docx => ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
            DraftFormat.PlainText => ("text/plain; charset=utf-8", "txt"),
            _ => ("text/markdown; charset=utf-8", "md"),
        };
        return Results.File(DemandLetterRenderer.RenderBytes(draft, analysis, draftFormat.Value), contentType,
            $"{SafeFileName(meta.Name)}-draft.{extension}");
    }

    private static async Task<IResult> GetReview(string caseId, ICaseStore store, CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        return meta is null ? CaseNotFound() : Results.Ok(await BuildReview(meta, store, cancellationToken));
    }

    private static async Task<IResult> UpdateReview(string caseId, string itemId, UpdateReviewRequest request, ICaseStore store,
        CancellationToken cancellationToken)
    {
        CaseMeta? meta = await store.Get(caseId, cancellationToken);
        if (meta is null)
        {
            return CaseNotFound();
        }

        if ((await BuildReview(meta, store, cancellationToken)).All(item => item.Id != itemId))
        {
            return Error(StatusCodes.Status404NotFound, "Review item not found.");
        }

        meta = await store.Update(caseId, current => current.Review[itemId] = new ReviewState()
        {
            Status = request.Status,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        return Results.Ok((await BuildReview(meta!, store, cancellationToken)).Single(item => item.Id == itemId));
    }

    private static async Task<IReadOnlyList<ReviewItem>> BuildReview(CaseMeta meta, ICaseStore store, CancellationToken cancellationToken)
    {
        PICase? analysis = await store.LoadAnalysis(meta.Id, cancellationToken);
        DemandDraft? draft = await store.LoadDraft(meta.Id, cancellationToken);
        return ReviewItems.Build(analysis, draft, meta.Review);
    }

    private static async Task<CaseDto> ToCaseDto(CaseMeta meta, ICaseStore store, IJobQueue jobs, CancellationToken cancellationToken)
    {
        bool analysisOutOfDate = meta.AnalysisUpdatedAt is not null && meta.RecordsChangedAt > meta.AnalysisUpdatedAt;
        bool draftOutOfDate = meta.DraftUpdatedAt is not null && meta.AnalysisUpdatedAt > meta.DraftUpdatedAt;
        return new CaseDto(meta.Id, meta.Name, meta.CreatedAt, meta.UpdatedAt, await ToRecordDtos(meta, store, cancellationToken),
            meta.AnalysisUpdatedAt, analysisOutOfDate, meta.DraftUpdatedAt, draftOutOfDate, jobs.CurrentJob(meta));
    }

    private static async Task<IReadOnlyList<RecordDto>> ToRecordDtos(CaseMeta meta, ICaseStore store, CancellationToken cancellationToken)
    {
        PICase? analysis = meta.AnalysisUpdatedAt is null ? null : await store.LoadAnalysis(meta.Id, cancellationToken);
        HashSet<string> summarized = analysis?.Sources.Select(source => source.FilePath).ToHashSet() ?? [];
        Dictionary<string, string> skipped = analysis?.SkippedFiles.ToDictionary(file => file.FilePath, file => file.Reason) ?? [];

        return store.ListRecords(meta.Id).Select(file =>
        {
            DateTimeOffset modified = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            bool known = summarized.Contains(file.FullName) || skipped.ContainsKey(file.FullName);
            RecordStatus status = !known ? RecordStatus.NotIngested
                : modified > meta.AnalysisUpdatedAt ? RecordStatus.Changed
                : summarized.Contains(file.FullName) ? RecordStatus.Summarized
                : RecordStatus.Skipped;
            string? reason = status == RecordStatus.Skipped ? skipped[file.FullName] : null;
            return new RecordDto(file.Name, file.Length, modified, status, reason);
        }).ToList();
    }

    private static string ContentType(FileInfo file)
    {
        return file.Extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".txt" or ".md" => "text/plain; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tif" or ".tiff" => "image/tiff",
            _ => "application/octet-stream",
        };
    }

    private static string SafeFileName(string name)
    {
        string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-');
        return safe.Length == 0 ? "case" : safe;
    }

    private static IResult Error(int statusCode, string message) => Results.Json(new ErrorResponse(message), statusCode: statusCode);

    private static IResult CaseNotFound() => Error(StatusCodes.Status404NotFound, "Case not found.");

    private static IResult JobActive() => Error(StatusCodes.Status409Conflict, "Wait for the current job to finish or cancel it first.");
}
