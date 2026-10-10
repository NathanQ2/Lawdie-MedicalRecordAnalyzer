using System.Text.Json.Serialization;
using Anthropic;
using MedicalRecordAnalysis;
using MedicalRecordAnalysis.Api;
using MedicalRecordAnalysis.Api.Cases;
using MedicalRecordAnalysis.Api.Endpoints;
using MedicalRecordAnalysis.Api.Jobs;
using MedicalRecordAnalysis.LLM;
using MedicalRecordAnalysis.LLM.Providers;
using MedicalRecordAnalysis.OCR;
using MedicalRecordAnalysis.OCR.Providers;
using Microsoft.AspNetCore.Http.Features;

const long MaxUploadBytes = 250L * 1024 * 1024;
const string ClientHeader = "X-MRA-Client";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    DotEnv.Load(builder.Environment.ContentRootPath);
}

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxUploadBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = MaxUploadBytes);

builder.Services.AddSingleton<ICaseStore>(services =>
{
    IConfiguration configuration = services.GetRequiredService<IConfiguration>();
    IWebHostEnvironment environment = services.GetRequiredService<IWebHostEnvironment>();
    string root = configuration["CaseStorage:RootDirectory"] ?? "../cases";
    return new FileCaseStore(new DirectoryInfo(Path.GetFullPath(Path.Combine(environment.ContentRootPath, root))));
});

builder.Services.AddSingleton<CaseJobQueue>();
builder.Services.AddSingleton<IJobQueue>(services => services.GetRequiredService<CaseJobQueue>());
builder.Services.AddHostedService(services => services.GetRequiredService<CaseJobQueue>());

// The LLM provider is created on first use, so a missing API key fails the job with a clear message rather
// than stopping the server from starting.
builder.Services.AddSingleton<ILLMProvider>(services =>
{
    IConfiguration configuration = services.GetRequiredService<IConfiguration>();
    string? apiKey = configuration["Anthropic:ApiKey"] ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException("No Anthropic API key is configured. Set ANTHROPIC_API_KEY or Anthropic:ApiKey.");
    }

    string? model = configuration["Anthropic:Model"];
    return new AnthropicProvider(new AnthropicClient() { ApiKey = apiKey }, string.IsNullOrWhiteSpace(model) ? null : model);
});
builder.Services.AddSingleton<IOCRProvider>(_ => new ChainedOCRProvider([new DirectTextProvider()]));
builder.Services.AddTransient(services =>
    new MedicalRecordAnalyzer(services.GetRequiredService<ILLMProvider>(), services.GetRequiredService<IOCRProvider>()));

WebApplication app = builder.Build();

// Browsers send cross-site form posts without preflight, so any web page could otherwise upload files or start
// paid jobs on this local server. A custom header cannot be sent cross-site without a CORS preflight, which
// this server never allows, so requiring one on every change blocks those requests.
app.Use(async (context, next) =>
{
    bool changesState = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method);
    if (changesState && context.Request.Path.StartsWithSegments("/api") && !context.Request.Headers.ContainsKey(ClientHeader))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new ErrorResponse($"Requests that change data must include the {ClientHeader} header."));
        return;
    }

    await next();
});

app.MapCaseEndpoints();

app.Run();

public partial class Program;
