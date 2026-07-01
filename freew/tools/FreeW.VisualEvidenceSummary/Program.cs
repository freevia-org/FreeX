using FreeW.App.Presentation.DocumentView;

var options = Parse(args);
if (options.ShowHelp || string.IsNullOrWhiteSpace(options.RunRoot) || options.ManifestPaths.Count == 0)
{
    PrintUsage();
    return options.ShowHelp ? 0 : 2;
}

var runRoot = Path.GetFullPath(options.RunRoot);
var jsonPath = string.IsNullOrWhiteSpace(options.OutputJson)
    ? Path.Combine(runRoot, FreeWVisualEvidenceManifestNormalizer.SummaryJsonFileName)
    : Path.GetFullPath(options.OutputJson);
var markdownPath = string.IsNullOrWhiteSpace(options.OutputMarkdown)
    ? Path.Combine(runRoot, FreeWVisualEvidenceManifestNormalizer.SummaryMarkdownFileName)
    : Path.GetFullPath(options.OutputMarkdown);

try
{
    var summary = FreeWVisualEvidenceManifestNormalizer.BuildNormalizedSummaryFromFiles(
        options.ManifestPaths,
        runRoot);
    FreeWVisualEvidenceManifestNormalizer.WriteSummaryFiles(summary, jsonPath, markdownPath);

    Console.WriteLine($"summary json: {jsonPath}");
    Console.WriteLine($"summary markdown: {markdownPath}");
    Console.WriteLine($"evidence rows: {summary.Evidence.Count}");
    Console.WriteLine($"trust: {(summary.Trust.Passed ? "passed" : "failed")}");

    if (!summary.Trust.Passed)
    {
        foreach (var failure in summary.Trust.Failures)
            Console.Error.WriteLine($"FAIL: {failure}");
        return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FreeW.VisualEvidenceSummary failed: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static Options Parse(string[] args)
{
    var options = new Options();
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--help":
            case "-h":
            case "/?":
                options.ShowHelp = true;
                break;
            case "--run-root":
                options.RunRoot = ReadValue(args, ref i, arg);
                break;
            case "--output-json":
                options.OutputJson = ReadValue(args, ref i, arg);
                break;
            case "--output-md":
            case "--output-markdown":
                options.OutputMarkdown = ReadValue(args, ref i, arg);
                break;
            case "--manifest":
                options.ManifestPaths.Add(ReadValue(args, ref i, arg));
                break;
            default:
                if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    options.ManifestPaths.Add(arg);
                else if (string.IsNullOrWhiteSpace(options.RunRoot))
                    options.RunRoot = arg;
                else
                    throw new ArgumentException($"Unknown argument: {arg}");
                break;
        }
    }

    return options;
}

static string ReadValue(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length)
        throw new ArgumentException($"{option} requires a value.");

    index++;
    return args[index];
}

static void PrintUsage()
{
    Console.Error.WriteLine("usage: FreeW.VisualEvidenceSummary --run-root <dir> --manifest <manifest.json> [--manifest <manifest.json>] [--output-json <path>] [--output-md <path>]");
}

sealed class Options
{
    public string? RunRoot { get; set; }
    public string? OutputJson { get; set; }
    public string? OutputMarkdown { get; set; }
    public bool ShowHelp { get; set; }
    public List<string> ManifestPaths { get; } = [];
}
