using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.Api.Endpoints;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.Retrieval;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedicalRecordAnalysis.Tests;

/**
 * Runs the API in memory against a temporary cases folder, with a test LLM provider instead of Anthropic.
 */
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("MedicalRecordAnalysisApi");
    public ILLMProvider Provider { get; set; } = new DummyLLMProvider();

    public HttpClient CreateApiClient()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add("X-MRA-Client", "tests");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CaseStorage:RootDirectory", Root.FullName);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ILLMProvider>();
            services.AddSingleton(_ => Provider);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Root.Exists)
        {
            Root.Delete(recursive: true);
        }
    }
}

public sealed class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiFactory m_factory = new ApiFactory();
    private readonly TestFiles m_files = new TestFiles();

    public void Dispose()
    {
        m_factory.Dispose();
        m_files.Dispose();
    }

    private async Task<CaseDto> CreateCase(HttpClient client, string name = "Ellis v. Truck Co.")
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/cases", new CreateCaseRequest(name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CaseDto>(Json))!;
    }

    private static MultipartFormDataContent Files(params (string Name, byte[] Content)[] files)
    {
        MultipartFormDataContent form = new MultipartFormDataContent();
        foreach ((string name, byte[] content) in files)
        {
            form.Add(new ByteArrayContent(content), "files", name);
        }

        return form;
    }

    private static byte[] Text(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    private static async Task<JobStatus> WaitForJob(HttpClient client, string caseId, Func<JobStatus, bool> until)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            JobStatus? job = await client.GetFromJsonAsync<JobStatus>($"/api/cases/{caseId}/jobs/current", Json);
            if (job is not null && until(job))
            {
                return job;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the job; last state {job?.State}.");
            await Task.Delay(25);
        }
    }

    private static Task<JobStatus> WaitForJobToFinish(HttpClient client, string caseId) =>
        WaitForJob(client, caseId, job => !job.IsActive);

    [Fact]
    public async Task CreatesListsAndOpensCases()
    {
        HttpClient client = m_factory.CreateApiClient();

        CaseDto created = await CreateCase(client);
        CaseListItemDto[] cases = (await client.GetFromJsonAsync<CaseListItemDto[]>("/api/cases", Json))!;
        CaseDto opened = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;

        Assert.Equal("Ellis v. Truck Co.", Assert.Single(cases).Name);
        Assert.Equal(created.Id, opened.Id);
        Assert.Empty(opened.Records);
        Assert.Null(opened.Job);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsBlankCaseNames(string name)
    {
        HttpResponseMessage response = await m_factory.CreateApiClient().PostAsJsonAsync("/api/cases", new CreateCaseRequest(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("zzzzzzzzzzzz")]
    [InlineData("not-a-case-id")]
    [InlineData("..%2F..%2Fetc")]
    public async Task UnknownOrMalformedCasesAreNotFound(string caseId)
    {
        HttpResponseMessage response = await m_factory.CreateApiClient().GetAsync($"/api/cases/{caseId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangesWithoutTheClientHeaderAreForbidden()
    {
        HttpClient client = m_factory.CreateClient();

        HttpResponseMessage post = await client.PostAsJsonAsync("/api/cases", new CreateCaseRequest("Cross-site"));
        HttpResponseMessage get = await client.GetAsync("/api/cases");

        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task UploadsServesAndDeletesRecords()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        byte[] pdf = await File.ReadAllBytesAsync(m_files.WritePdf("record.pdf", "Acute cervical strain diagnosed.").FullName);

        HttpResponseMessage upload = await client.PostAsync($"/api/cases/{created.Id}/records",
            Files(("note.txt", Text("Neck pain after collision.")), ("record.pdf", pdf)));
        RecordDto[] records = (await upload.Content.ReadFromJsonAsync<RecordDto[]>(Json))!;

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(["note.txt", "record.pdf"], records.Select(record => record.Name));
        Assert.All(records, record => Assert.Equal(RecordStatus.NotIngested, record.Status));

        HttpResponseMessage text = await client.GetAsync($"/api/cases/{created.Id}/records/note.txt");
        Assert.Equal("text/plain", text.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Neck pain after collision.", await text.Content.ReadAsStringAsync());
        Assert.Equal("nosniff", text.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("sandbox", text.Headers.GetValues("Content-Security-Policy").Single());

        HttpResponseMessage served = await client.GetAsync($"/api/cases/{created.Id}/records/record.pdf");
        Assert.Equal("application/pdf", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdf, await served.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/cases/{created.Id}/records/note.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/cases/{created.Id}/records/note.txt")).StatusCode);
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData(".hidden.txt")]
    [InlineData("dir/note.txt")]
    public async Task RejectsUnsafeRecordNames(string fileName)
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);

        HttpResponseMessage response = await client.PostAsync($"/api/cases/{created.Id}/records", Files((fileName, Text("text"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(Path.Combine(m_factory.Root.FullName, created.Id, "records")));
    }

    [Fact]
    public async Task RecordPathsCannotEscapeTheCaseFolder()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);

        HttpResponseMessage response = await client.GetAsync($"/api/cases/{created.Id}/records/..%2Fmeta.json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestAndDraftRunAsBackgroundJobs()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records",
            Files(("note.txt", Text("Neck pain after collision.")), ("scan.png", Text("not really an image"))));

        HttpResponseMessage ingest = await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        Assert.Equal(HttpStatusCode.Accepted, ingest.StatusCode);
        Assert.Equal(JobState.Succeeded, (await WaitForJobToFinish(client, created.Id)).State);

        PICase analysis = (await client.GetFromJsonAsync<PICase>($"/api/cases/{created.Id}/analysis", Json))!;
        Assert.Single(analysis.DocumentSummaries);
        Assert.Equal("scan.png", Path.GetFileName(Assert.Single(analysis.SkippedFiles).FilePath));

        CaseDto ingested = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;
        Assert.Equal(RecordStatus.Summarized, ingested.Records.Single(record => record.Name == "note.txt").Status);
        RecordDto scan = ingested.Records.Single(record => record.Name == "scan.png");
        Assert.Equal(RecordStatus.Skipped, scan.Status);
        Assert.Contains("DirectTextProvider", scan.Reason);
        Assert.NotNull(ingested.AnalysisUpdatedAt);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/cases/{created.Id}/draft", null)).StatusCode);
        JobStatus drafted = await WaitForJobToFinish(client, created.Id);
        Assert.Equal((JobKind.Draft, JobState.Succeeded), (drafted.Kind, drafted.State));

        DemandDraft draft = (await client.GetFromJsonAsync<DemandDraft>($"/api/cases/{created.Id}/draft", Json))!;
        Assert.Contains("TestDraftNarrative", draft.Narrative);

        HttpResponseMessage markdown = await client.GetAsync($"/api/cases/{created.Id}/draft/export?format=md");
        Assert.Contains("## Attorney Review", await markdown.Content.ReadAsStringAsync());
        Assert.Equal("Ellis-v--Truck-Co-draft.md", markdown.Content.Headers.ContentDisposition?.FileName);

        HttpResponseMessage text = await client.GetAsync($"/api/cases/{created.Id}/draft/export?format=txt");
        Assert.Contains("ATTORNEY REVIEW", await text.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/cases/{created.Id}/draft/export?format=docx")).StatusCode);
    }

    [Fact]
    public async Task JobsNeedRecordsAndDraftsNeedAnAnalysis()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/cases/{created.Id}/ingest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/cases/{created.Id}/draft", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/cases/{created.Id}/analysis")).StatusCode);
    }

    [Fact]
    public async Task RunningJobsBlockChangesAndCanBeCancelled()
    {
        GatedProvider provider = new GatedProvider();
        m_factory.Provider = provider;
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain."))));

        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        JobStatus running = await WaitForJob(client, created.Id, job => job.State == JobState.Running && job.CurrentFile == "note.txt");
        Assert.Equal(PipelineStage.SummarizingDocuments, running.Stage);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/cases/{created.Id}/ingest", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/cases/{created.Id}/records", Files(("b.txt", Text("B"))))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/cases/{created.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/cases/{created.Id}/jobs/cancel", null)).StatusCode);
        Assert.Equal(JobState.Cancelled, (await WaitForJobToFinish(client, created.Id)).State);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/cases/{created.Id}/jobs/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task FailedJobsReportTheError()
    {
        m_factory.Provider = new FailingProvider();
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain."))));

        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        JobStatus job = await WaitForJobToFinish(client, created.Id);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("Verification failed.", job.Error);
    }

    [Fact]
    public async Task AnalysisIsOutOfDateAfterRecordsChange()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("a.txt", Text("Record A."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);

        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("b.txt", Text("Record B."))));
        CaseDto updated = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;

        Assert.True(updated.AnalysisOutOfDate);
        Assert.Equal(RecordStatus.NotIngested, updated.Records.Single(record => record.Name == "b.txt").Status);
        Assert.Equal(RecordStatus.Summarized, updated.Records.Single(record => record.Name == "a.txt").Status);
    }

    [Fact]
    public async Task ReviewItemsCanBeResolvedWithANote()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        await client.PostAsync($"/api/cases/{created.Id}/draft", null);
        await WaitForJobToFinish(client, created.Id);

        ReviewItem placeholder = Assert.Single((await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!);
        Assert.Equal((ReviewItemKind.Placeholder, "[CLAIM NUMBER]", ReviewStatus.Open), (placeholder.Kind, placeholder.Text, placeholder.Status));

        HttpResponseMessage update = await client.PutAsJsonAsync($"/api/cases/{created.Id}/review/{placeholder.Id}",
            new UpdateReviewRequest(ReviewStatus.Resolved, "Claim number is 26-55102."), Json);
        ReviewItem reviewed = Assert.Single((await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal((ReviewStatus.Resolved, "Claim number is 26-55102."), (reviewed.Status, reviewed.Note));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/cases/{created.Id}/review/placeholder-0000",
            new UpdateReviewRequest(ReviewStatus.Resolved, null), Json)).StatusCode);
    }

    [Fact]
    public async Task JobsInterruptedByARestartAreMarkedFailed()
    {
        FileCaseStore store = new FileCaseStore(m_factory.Root);
        CaseMeta meta = await store.Create("Interrupted");
        await store.Update(meta.Id, current => current.CurrentJob = new JobStatus() { Id = "job", Kind = JobKind.Ingest, State = JobState.Running });

        CaseDto reopened = (await m_factory.CreateApiClient().GetFromJsonAsync<CaseDto>($"/api/cases/{meta.Id}", Json))!;

        Assert.Equal(JobState.Failed, reopened.Job?.State);
        Assert.Contains("server stopped", reopened.Job?.Error);
    }

    /**
     * Blocks in SummarizeDocument until cancelled, so tests can act while a job is running.
     */
    private sealed class GatedProvider : ILLMProvider
    {
        private readonly DummyLLMProvider m_inner = new DummyLLMProvider();

        public async Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return await m_inner.SummarizeDocument(file, text, cancellationToken);
        }

        public Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default) => m_inner.SummarizeCase(piCase, cancellationToken);
        public Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default) => m_inner.Verify(piCase, cancellationToken);
        public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default) =>
            m_inner.DraftDemand(piCase, passages, cancellationToken);
    }

    private sealed class FailingProvider : ILLMProvider
    {
        private readonly DummyLLMProvider m_inner = new DummyLLMProvider();

        public Task<DocumentSummary> SummarizeDocument(FileInfo file, string text, CancellationToken cancellationToken = default) =>
            m_inner.SummarizeDocument(file, text, cancellationToken);
        public Task<CaseSummary> SummarizeCase(PICase piCase, CancellationToken cancellationToken = default) => m_inner.SummarizeCase(piCase, cancellationToken);
        public Task<VerificationResult> Verify(PICase piCase, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verification failed.");
        public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default) =>
            m_inner.DraftDemand(piCase, passages, cancellationToken);
    }
}
