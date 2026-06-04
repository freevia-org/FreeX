internal sealed record SmokeOptions(
    bool ShowHelp,
    bool SaveReopen,
    bool GenerateChartFixtures,
    bool GenerateFreexFixture,
    bool GenerateFreexFeatureFixtures,
    bool GenerateSupportedCorpusFixtures,
    bool GenerateExcelFixture,
    bool FreeXResaveBeforeExcel,
    string? CorpusManifestPath,
    IReadOnlyList<string> CorpusSources,
    IReadOnlyList<string> CorpusStatuses,
    IReadOnlyList<string> CorpusIds,
    string? OutputDirectory,
    string Pattern,
    IReadOnlyList<string> Inputs)
{
    public bool HasGeneratedFixtures =>
        GenerateChartFixtures ||
        GenerateFreexFixture ||
        GenerateFreexFeatureFixtures ||
        GenerateSupportedCorpusFixtures ||
        GenerateExcelFixture;

    public bool HasCorpusManifest => !string.IsNullOrWhiteSpace(CorpusManifestPath);

    public bool HasRequestedInputs => HasGeneratedFixtures || HasCorpusManifest || Inputs.Count > 0;

    public static SmokeOptions Parse(string[] args)
    {
        var saveReopen = false;
        var generateChartFixtures = false;
        var generateFreexFixture = false;
        var generateFreexFeatureFixtures = false;
        var generateSupportedCorpusFixtures = false;
        var generateExcelFixture = false;
        var freeXResaveBeforeExcel = false;
        string? corpusManifestPath = null;
        var corpusSources = new List<string>();
        var corpusStatuses = new List<string>();
        var corpusIds = new List<string>();
        string? outputDirectory = null;
        var pattern = "*.xlsx";
        var inputs = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return new SmokeOptions(true, false, false, false, false, false, false, false, null, [], [], [], null, pattern, []);
                case "--save-reopen":
                    saveReopen = true;
                    break;
                case "--generate-chart-fixtures":
                    generateChartFixtures = true;
                    break;
                case "--generate-freex-fixture":
                    generateFreexFixture = true;
                    break;
                case "--generate-freex-feature-fixtures":
                    generateFreexFeatureFixtures = true;
                    break;
                case "--generate-supported-corpus-fixtures":
                    generateSupportedCorpusFixtures = true;
                    break;
                case "--generate-excel-fixture":
                    generateExcelFixture = true;
                    break;
                case "--freex-resave-before-excel":
                    freeXResaveBeforeExcel = true;
                    break;
                case "--corpus-manifest":
                    corpusManifestPath = ReadOptionValue(args, ref index, arg);
                    break;
                case "--corpus-source":
                    corpusSources.Add(ReadOptionValue(args, ref index, arg));
                    break;
                case "--corpus-status":
                    corpusStatuses.Add(ReadOptionValue(args, ref index, arg));
                    break;
                case "--corpus-id":
                    corpusIds.Add(ReadOptionValue(args, ref index, arg));
                    break;
                case "--out":
                    outputDirectory = ReadOptionValue(args, ref index, arg);
                    break;
                case "--pattern":
                    pattern = ReadOptionValue(args, ref index, arg);
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {arg}");
                    inputs.Add(arg);
                    break;
            }
        }

        return new SmokeOptions(
            false,
            saveReopen,
            generateChartFixtures,
            generateFreexFixture,
            generateFreexFeatureFixtures,
            generateSupportedCorpusFixtures,
            generateExcelFixture,
            freeXResaveBeforeExcel,
            corpusManifestPath,
            corpusSources,
            corpusStatuses,
            corpusIds,
            outputDirectory,
            pattern,
            inputs);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires a value.");

        index++;
        return args[index];
    }
}
