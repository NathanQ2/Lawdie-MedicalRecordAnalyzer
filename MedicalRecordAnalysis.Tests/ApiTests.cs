using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.Api.Endpoints;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using MedicalRecordAnalysis.Retrieval;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedicalRecordAnalysis.Tests;

/**
 * Runs the API in memory against a temporary cases folder, with a test LLM provider instead of Anthropic. The type
 * argument only identifies the API assembly, whose entry point is run; Program is static and cannot be used here.
 */
public sealed class ApiFactory : WebApplicationFactory<FileCaseStore>
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
            // Never call a real OCR service from tests, even when its keys are set in the environment.
            services.RemoveAll<IOCRProvider>();
            services.AddSingleton<IOCRProvider>(_ => new ChainedOCRProvider([new DirectTextProvider()]));
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

    [Fact]
    public async Task RenamesCases()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);

        HttpResponseMessage renamed = await client.PatchAsJsonAsync($"/api/cases/{created.Id}", new RenameCaseRequest("  Ellis v. Cascade Freight  "));
        HttpResponseMessage blank = await client.PatchAsJsonAsync($"/api/cases/{created.Id}", new RenameCaseRequest(" "));
        HttpResponseMessage missing = await client.PatchAsJsonAsync("/api/cases/zzzzzzzzzzzz", new RenameCaseRequest("Name"));

        Assert.Equal("Ellis v. Cascade Freight", (await renamed.Content.ReadFromJsonAsync<CaseDto>(Json))!.Name);
        Assert.Equal("Ellis v. Cascade Freight", (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!.Name);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task SavesIntakeDetails()
    {
        HttpClient client = m_factory.CreateApiClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/cases",
            new CreateCaseRequest("Ellis v. Cascade Freight", new DateOnly(2026, 1, 9), " Washington ", "   "));
        CaseDto created = (await response.Content.ReadFromJsonAsync<CaseDto>(Json))!;
        CaseListItemDto listed = Assert.Single((await client.GetFromJsonAsync<CaseListItemDto[]>("/api/cases", Json))!);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((new DateOnly(2026, 1, 9), "Washington", (string?)null), (created.DateOfLoss, created.Jurisdiction, created.County));
        Assert.Equal(new DateOnly(2026, 1, 9), listed.DateOfLoss);
    }

    [Fact]
    public async Task UpdatesIntakeDetails()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        string path = $"/api/cases/{created.Id}/matter";

        HttpResponseMessage updated = await client.PutAsJsonAsync(path, new UpdateMatterRequest("Ellis v. Cascade Freight", new DateOnly(2026, 1, 9), "Washington", "King"));
        HttpResponseMessage cleared = await client.PutAsJsonAsync(path, new UpdateMatterRequest("Ellis v. Cascade Freight", new DateOnly(2026, 1, 9), "Washington", null));
        HttpResponseMessage future = await client.PutAsJsonAsync(path, new UpdateMatterRequest("Ellis", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), null, null));
        HttpResponseMessage tooLong = await client.PutAsJsonAsync(path, new UpdateMatterRequest("Ellis", null, new string('x', 101), null));
        HttpResponseMessage blankName = await client.PutAsJsonAsync(path, new UpdateMatterRequest(" ", null, null, null));
        HttpResponseMessage missing = await client.PutAsJsonAsync("/api/cases/zzzzzzzzzzzz/matter", new UpdateMatterRequest("Ellis", null, null, null));

        Assert.Equal("King", (await updated.Content.ReadFromJsonAsync<CaseDto>(Json))!.County);
        CaseDto current = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;
        Assert.Equal(("Ellis v. Cascade Freight", new DateOnly(2026, 1, 9), "Washington", (string?)null),
            (current.Name, current.DateOfLoss, current.Jurisdiction, current.County));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankName.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task FillsTheDateOfLossInTheServedDraft()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain after collision."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        await client.PostAsync($"/api/cases/{created.Id}/draft", null);
        await WaitForJobToFinish(client, created.Id);

        DemandDraft before = (await client.GetFromJsonAsync<DemandDraft>($"/api/cases/{created.Id}/draft", Json))!;
        await client.PutAsJsonAsync($"/api/cases/{created.Id}/matter", new UpdateMatterRequest(created.Name, new DateOnly(2026, 1, 9), null, null));
        DemandDraft after = (await client.GetFromJsonAsync<DemandDraft>($"/api/cases/{created.Id}/draft", Json))!;
        ReviewItem[] review = (await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!;
        string markdown = await client.GetStringAsync($"/api/cases/{created.Id}/draft/export?format=md");

        Assert.Contains(DemandLetterTemplate.DateOfLossPlaceholder, before.Narrative);
        Assert.DoesNotContain(DemandLetterTemplate.DateOfLossPlaceholder, after.Narrative);
        Assert.Contains("Date of Loss: January 9, 2026", after.Narrative);
        Assert.Contains("January 9, 2026", markdown);
        // The export compares the intake details with the documents, as the review checklist does.
        Assert.Contains("### Intake details the documents contradict", markdown);
        Assert.DoesNotContain(review, item => item.Text == DemandLetterTemplate.DateOfLossPlaceholder);
        Assert.Contains(review, item => item.Text == "[CLAIM NUMBER]");
    }

    [Fact]
    public async Task SavesTheFirmProfile()
    {
        HttpClient client = m_factory.CreateApiClient();

        FirmProfile empty = (await client.GetFromJsonAsync<FirmProfile>("/api/firm", Json))!;
        HttpResponseMessage saved = await client.PutAsJsonAsync("/api/firm", new FirmProfile(" Reyes Law ", "1 Main St\r\nSeattle, WA", "", null, "Dana Reyes"));
        FirmProfile loaded = (await client.GetFromJsonAsync<FirmProfile>("/api/firm", Json))!;
        HttpResponseMessage blankLine = await client.PutAsJsonAsync("/api/firm", new FirmProfile("Reyes Law", "1 Main St\n\nSeattle", null, null, null));
        HttpResponseMessage tooLong = await client.PutAsJsonAsync("/api/firm", new FirmProfile(new string('x', 301), null, null, null, null));

        Assert.Equal(FirmProfile.Empty, empty);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(new FirmProfile("Reyes Law", "1 Main St\nSeattle, WA", null, null, "Dana Reyes"), loaded);
        Assert.Equal(HttpStatusCode.BadRequest, blankLine.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }

    [Fact]
    public async Task FillsPlaceholdersFromTheFirmProfileAndTheCasesValues()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        string path = $"/api/cases/{created.Id}/letter/placeholders";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);

        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain after collision."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        await client.PostAsync($"/api/cases/{created.Id}/draft", null);
        await WaitForJobToFinish(client, created.Id);

        await client.PutAsJsonAsync("/api/firm", new FirmProfile("Reyes Law", null, null, null, "Dana Reyes"));
        HttpResponseMessage updated = await client.PutAsJsonAsync(path, new UpdatePlaceholdersRequest(new Dictionary<string, string?>()
        {
            ["[CLAIM NUMBER]"] = " CL-2026-0042 ",
            ["[ATTORNEY NAME]"] = "Sam Ortiz",
        }));
        LetterPlaceholderDto[] placeholders = (await updated.Content.ReadFromJsonAsync<LetterPlaceholderDto[]>(Json))!;
        DemandDraft draft = (await client.GetFromJsonAsync<DemandDraft>($"/api/cases/{created.Id}/draft", Json))!;
        ReviewItem[] review = (await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!;
        string markdown = await client.GetStringAsync($"/api/cases/{created.Id}/draft/export?format=md");

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Contains(new LetterPlaceholderDto("[CLAIM NUMBER]", "CL-2026-0042", null, false), placeholders);
        Assert.Contains(new LetterPlaceholderDto("[ATTORNEY NAME]", "Sam Ortiz", "Dana Reyes", false), placeholders);
        Assert.Contains(new LetterPlaceholderDto("[LAW FIRM NAME]", null, "Reyes Law", false), placeholders);
        Assert.Contains(new LetterPlaceholderDto(DemandLetterTemplate.DateOfLossPlaceholder, null, null, true), placeholders);
        // The case's own value overrides the firm's, and filled placeholders leave the review checklist.
        Assert.StartsWith("Reyes Law\n", draft.Narrative);
        Assert.Contains("Claim No.: CL-2026-0042", draft.Narrative);
        Assert.Contains("Sam Ortiz\nReyes Law", draft.Narrative);
        Assert.Contains("Claim No.: CL-2026-0042", markdown);
        Assert.DoesNotContain(review, item => item.Text is "[CLAIM NUMBER]" or "[LAW FIRM NAME]" or "[ATTORNEY NAME]");
        Assert.Contains(review, item => item.Text == "[CLIENT NAME]");

        HttpResponseMessage cleared = await client.PutAsJsonAsync(path, new UpdatePlaceholdersRequest(new Dictionary<string, string?>() { ["[CLAIM NUMBER]"] = "" }));
        Assert.Contains(new LetterPlaceholderDto("[CLAIM NUMBER]", null, null, false), (await cleared.Content.ReadFromJsonAsync<LetterPlaceholderDto[]>(Json))!);
    }

    [Fact]
    public async Task EditsTheLettersTextAndListsNewSentencesForReview()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain after collision."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        await client.PostAsync($"/api/cases/{created.Id}/draft", null);
        await WaitForJobToFinish(client, created.Id);

        DemandDraft drafted = (await client.GetFromJsonAsync<DemandDraft>($"/api/cases/{created.Id}/draft?fill=false", Json))!;
        Citation source = drafted.Segments.First(segment => segment.Citations.Length > 0).Citations[0];
        const string original = "TestDraftNarrative. Claim [CLAIM NUMBER].";
        const string revised = "An added sentence. TestDraftNarrative, revised. Claim [CLAIM NUMBER].";
        int start = drafted.Narrative.IndexOf(original, StringComparison.Ordinal);
        string path = $"/api/cases/{created.Id}/draft/text";
        Dictionary<int, Citation> sources = new Dictionary<int, Citation>() { [1] = source };

        HttpResponseMessage edited = await client.PutAsJsonAsync(path,
            new EditLetterRequest(start, original, "An added sentence. TestDraftNarrative, revised.[1] Claim [CLAIM NUMBER].", sources));
        DemandDraft letter = (await edited.Content.ReadFromJsonAsync<DemandDraft>(Json))!;
        ReviewItem[] review = (await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!;
        CaseDto afterEdit = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;
        HttpResponseMessage stale = await client.PutAsJsonAsync(path, new EditLetterRequest(start, original, "Anything.", sources));
        Citation invented = new Citation() { FilePath = source.FilePath, CitedText = "Words the document never says.", StartPosition = 0 };
        HttpResponseMessage foreign = await client.PutAsJsonAsync(path,
            new EditLetterRequest(start, revised, "TestDraftNarrative.[1]", new Dictionary<int, Citation>() { [1] = invented }));

        Assert.True(start > 0);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Contains(revised, letter.Narrative);
        Assert.Contains(review, item => item.Kind == ReviewItemKind.UnsupportedStatement && item.Text == "An added sentence.");
        Assert.DoesNotContain(review, item => item.Text.Contains("revised", StringComparison.Ordinal));
        Assert.NotNull(afterEdit.LetterEditedAt);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        // Drafting again replaces the edits.
        await client.PostAsync($"/api/cases/{created.Id}/draft", null);
        await WaitForJobToFinish(client, created.Id);
        CaseDto redrafted = (await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!;
        Assert.Null(redrafted.LetterEditedAt);
    }

    [Theory]
    [InlineData("CLAIM NUMBER", "CL-1")]
    [InlineData("[Claim number]", "CL-1")]
    [InlineData("[DATE OF LOSS]", "January 9, 2026")]
    [InlineData("[CLAIM NUMBER]", "Line one\n\nLine two")]
    public async Task RejectsInvalidPlaceholderValues(string placeholder, string value)
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);

        HttpResponseMessage response = await client.PutAsJsonAsync($"/api/cases/{created.Id}/letter/placeholders",
            new UpdatePlaceholdersRequest(new Dictionary<string, string?>() { [placeholder] = value }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(served.Headers.Contains("Content-Security-Policy"), "Chrome will not display PDFs in a sandboxed page.");
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
        HttpResponseMessage word = await client.GetAsync($"/api/cases/{created.Id}/draft/export?format=docx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", word.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Ellis-v--Truck-Co-draft.docx", word.Content.Headers.ContentDisposition?.FileName);
        using (DocumentFormat.OpenXml.Packaging.WordprocessingDocument document =
            DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(await word.Content.ReadAsByteArrayAsync()), false))
        {
            Assert.Contains("TestDraftNarrative", document.MainDocumentPart!.Document!.Body!.InnerText);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/cases/{created.Id}/draft/export?format=pdf")).StatusCode);
    }

    [Fact]
    public async Task MarksTheAnalysisReviewedUntilItIsRedone()
    {
        HttpClient client = m_factory.CreateApiClient();
        CaseDto created = await CreateCase(client);
        string reviewed = $"/api/cases/{created.Id}/analysis/reviewed";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(reviewed, null)).StatusCode);

        await client.PostAsync($"/api/cases/{created.Id}/records", Files(("note.txt", Text("Neck pain after collision."))));
        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        Assert.False((await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!.AnalysisReviewed);

        HttpResponseMessage marked = await client.PostAsync(reviewed, null);
        Assert.True((await marked.Content.ReadFromJsonAsync<CaseDto>(Json))!.AnalysisReviewed);

        await client.PostAsync($"/api/cases/{created.Id}/ingest", null);
        await WaitForJobToFinish(client, created.Id);
        Assert.False((await client.GetFromJsonAsync<CaseDto>($"/api/cases/{created.Id}", Json))!.AnalysisReviewed);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/cases/zzzzzzzzzzzz/analysis/reviewed", null)).StatusCode);
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

        ReviewItem[] items = (await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!;
        Assert.All(items, item => Assert.Equal(ReviewStatus.Open, item.Status));
        ReviewItem placeholder = items.Single(item => item.Kind == ReviewItemKind.Placeholder && item.Text == "[CLAIM NUMBER]");

        HttpResponseMessage update = await client.PutAsJsonAsync($"/api/cases/{created.Id}/review/{placeholder.Id}",
            new UpdateReviewRequest(ReviewStatus.Resolved, "Claim number is 26-55102."), Json);
        ReviewItem reviewed = (await client.GetFromJsonAsync<ReviewItem[]>($"/api/cases/{created.Id}/review", Json))!.Single(item => item.Id == placeholder.Id);

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
        public Task<ChronologyResult> BuildChronology(PICase piCase, CancellationToken cancellationToken = default) => m_inner.BuildChronology(piCase, cancellationToken);
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
        public Task<ChronologyResult> BuildChronology(PICase piCase, CancellationToken cancellationToken = default) => m_inner.BuildChronology(piCase, cancellationToken);
        public Task<DemandDraft> DraftDemand(PICase piCase, IReadOnlyList<RetrievedPassage> passages, CancellationToken cancellationToken = default) =>
            m_inner.DraftDemand(piCase, passages, cancellationToken);
    }
}
