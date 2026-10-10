using System.CommandLine;
using MedicalRecordAnalysis;

namespace MedicalRecordAnalysisCLI;

public static class Program
{
    private static readonly RootCommand RootCmd = new RootCommand("Medical Record Analyzer");
    private static readonly Command IngestCmd = new Command("ingest", "Ingest the files in the provided directory");
    private static readonly Command DraftCmd = new Command("draft", "Draft a demand letter from the provided context file");
    private static readonly Argument<DirectoryInfo> DirectoryArgument = new Argument<DirectoryInfo>("Directory");
    private static readonly Argument<FileInfo> PathArgument = new Argument<FileInfo>("Path");
    private static readonly Option<FileInfo> DraftOutputFileOption = new Option<FileInfo>("--output", "-o")
    {
        Description = "Output file path",
        DefaultValueFactory = _ => new FileInfo("Draft.md")
    };
    private static readonly Option<FileInfo> IngestOutputFileOption = new Option<FileInfo>("--output", "-o")
    {
        Description = "Output file path",
        DefaultValueFactory = _ => new FileInfo("PICaseData.json")
    };

    private static MedicalRecordAnalyzer s_analyzer = new MedicalRecordAnalyzer();
    
    public static int Main(String[] args)
    {
        IngestCmd.Arguments.Add(DirectoryArgument);
        IngestCmd.Options.Add(IngestOutputFileOption);
        IngestCmd.SetAction(result => s_analyzer.Ingest(result.GetValue(DirectoryArgument)!, result.GetValue(IngestOutputFileOption)!));
        RootCmd.Subcommands.Add(IngestCmd);
        
        DraftCmd.Arguments.Add(PathArgument);
        DraftCmd.Options.Add(DraftOutputFileOption);
        DraftCmd.SetAction(result => s_analyzer.Draft(result.GetValue(PathArgument)!, result.GetValue(DraftOutputFileOption)!));
        RootCmd.Subcommands.Add(DraftCmd);

        return RootCmd.Parse(args).Invoke();
    }
}