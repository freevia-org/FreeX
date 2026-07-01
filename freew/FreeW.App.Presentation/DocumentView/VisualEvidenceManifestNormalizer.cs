using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeW.App.Presentation.DocumentView;

public sealed record FreeWVisualEvidenceExpectedScenario(
    string HostId,
    string ScenarioId,
    int MinimumExpectedOutputs);

public sealed record FreeWVisualEvidenceNormalizedSource(
    string ManifestPath,
    IReadOnlyList<string> HostIds,
    int EvidenceCount);

public sealed record FreeWVisualEvidenceNormalizedScenario(
    string HostId,
    string ScenarioId,
    int MinimumExpectedOutputs,
    int ActualOutputs,
    bool Expected,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedRow(
    string EvidenceId,
    string SourceManifestPath,
    string ScenarioId,
    string HostId,
    IReadOnlyList<string> ExpectedFeatureTags,
    string OutputName,
    string OutputPath,
    int PixelWidth,
    int PixelHeight,
    long ByteLength,
    string Sha256,
    FreeWVisualPixelStats PixelStats,
    int PageNumber,
    int PageCount,
    string LayoutKind,
    string ExpectedOutputName,
    FreeWVisualEvidenceTrust Trust);

public sealed record FreeWVisualEvidenceNormalizedSummary(
    string SchemaId,
    int SchemaVersion,
    IReadOnlyList<FreeWVisualEvidenceNormalizedSource> Sources,
    IReadOnlyList<FreeWVisualEvidenceExpectedScenario> ExpectedScenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> Scenarios,
    IReadOnlyList<FreeWVisualEvidenceNormalizedRow> Evidence,
    FreeWVisualEvidenceTrust Trust);

public static class FreeWVisualEvidenceManifestNormalizer
{
    public const string SummarySchemaId = "freew.visual-evidence-summary.v1";
    public const int SummarySchemaVersion = 1;
    public const string SummaryJsonFileName = "freew_visual_evidence_summary.json";
    public const string SummaryMarkdownFileName = "freew_visual_evidence_summary.md";
    public const string WpfHostId = "wpf-fidelity-render";
    public const string AvaloniaHostId = "avalonia-page-layout-shot";

    public static IReadOnlyList<FreeWVisualEvidenceExpectedScenario> DefaultExpectedScenarios { get; } =
        BuildDefaultExpectedScenarios();

    public static FreeWVisualEvidenceManifest ReadManifest(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<FreeWVisualEvidenceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Visual evidence manifest could not be read: {Path.GetFileName(manifestPath)}");
    }

    public static FreeWVisualEvidenceNormalizedSummary BuildNormalizedSummaryFromFiles(
        IReadOnlyList<string> manifestPaths,
        string runRoot,
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario>? expectedScenarios = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);

        if (manifestPaths.Count == 0)
            throw new ArgumentException("At least one visual evidence manifest is required.", nameof(manifestPaths));

        var normalizedRoot = Path.GetFullPath(runRoot);
        var expected = (expectedScenarios ?? DefaultExpectedScenarios)
            .OrderBy(e => e.HostId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var expectedByKey = expected.ToDictionary(
            e => ScenarioKey(e.HostId, e.ScenarioId),
            StringComparer.OrdinalIgnoreCase);

        var failures = new List<string>();
        var sources = new List<FreeWVisualEvidenceNormalizedSource>();
        var rows = new List<FreeWVisualEvidenceNormalizedRow>();
        var evidenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var manifestPath in manifestPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fullManifestPath = Path.GetFullPath(manifestPath);
            var sourceManifestPath = NormalizeRelativePath(normalizedRoot, fullManifestPath);
            if (!IsSubPathOf(normalizedRoot, fullManifestPath))
                failures.Add($"source manifest '{sourceManifestPath}' is outside the run root");

            var manifest = ReadManifest(fullManifestPath);
            ValidateManifestHeader(manifest, sourceManifestPath, failures);

            var hostIds = manifest.Evidence
                .Select(e => e.HostId)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sources.Add(new FreeWVisualEvidenceNormalizedSource(
                sourceManifestPath,
                hostIds,
                manifest.Evidence.Count));

            var manifestDirectory = Path.GetDirectoryName(fullManifestPath) ?? normalizedRoot;
            foreach (var row in manifest.Evidence)
            {
                rows.Add(NormalizeRow(
                    row,
                    sourceManifestPath,
                    manifestDirectory,
                    normalizedRoot,
                    evidenceIds,
                    failures));
            }
        }

        var scenarios = BuildScenarioSummaries(rows, expected, expectedByKey, failures);
        var summaryTrust = new FreeWVisualEvidenceTrust(failures.Count == 0, failures);
        return new FreeWVisualEvidenceNormalizedSummary(
            SummarySchemaId,
            SummarySchemaVersion,
            sources
                .OrderBy(s => s.ManifestPath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            expected,
            scenarios,
            rows
                .OrderBy(r => r.HostId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.ScenarioId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PageNumber)
                .ThenBy(r => r.OutputName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            summaryTrust);
    }

    public static string ToJson(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    public static string ToMarkdown(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var sb = new StringBuilder();
        sb.AppendLine("# FreeW Visual Evidence Summary");
        sb.AppendLine();
        sb.AppendLine($"Schema: `{summary.SchemaId}` v{summary.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Trust: {(summary.Trust.Passed ? "passed" : "failed")}");
        sb.AppendLine($"Evidence rows: {summary.Evidence.Count.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine();

        if (summary.Trust.Failures.Count > 0)
        {
            sb.AppendLine("## Validation Failures");
            sb.AppendLine();
            foreach (var failure in summary.Trust.Failures)
                sb.AppendLine($"- {failure}");
            sb.AppendLine();
        }

        sb.AppendLine("## Scenario Coverage");
        sb.AppendLine();
        sb.AppendLine("| Host | Scenario | Outputs | Minimum | Trust |");
        sb.AppendLine("| --- | --- | ---: | ---: | --- |");
        foreach (var scenario in summary.Scenarios)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(scenario.HostId)} | {EscapeMarkdown(scenario.ScenarioId)} | " +
                $"{scenario.ActualOutputs.ToString(CultureInfo.InvariantCulture)} | " +
                $"{scenario.MinimumExpectedOutputs.ToString(CultureInfo.InvariantCulture)} | " +
                $"{(scenario.Trust.Passed ? "passed" : "failed")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Evidence");
        sb.AppendLine();
        sb.AppendLine("| Host | Scenario | Output | Size | Bytes | SHA-256 | Trust |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | --- | --- |");
        foreach (var row in summary.Evidence)
        {
            sb.AppendLine(
                $"| {EscapeMarkdown(row.HostId)} | {EscapeMarkdown(row.ScenarioId)} | " +
                $"{EscapeMarkdown(row.OutputPath)} | " +
                $"{row.PixelWidth.ToString(CultureInfo.InvariantCulture)}x{row.PixelHeight.ToString(CultureInfo.InvariantCulture)} | " +
                $"{row.ByteLength.ToString(CultureInfo.InvariantCulture)} | `{row.Sha256}` | " +
                $"{(row.Trust.Passed ? "passed" : "failed")} |");
        }

        return sb.ToString();
    }

    public static void WriteSummaryFiles(
        FreeWVisualEvidenceNormalizedSummary summary,
        string jsonPath,
        string markdownPath)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markdownPath)) ?? ".");
        File.WriteAllText(jsonPath, ToJson(summary));
        File.WriteAllText(markdownPath, ToMarkdown(summary));
    }

    public static void EnsureSummaryTrusted(FreeWVisualEvidenceNormalizedSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Trust.Passed)
            return;

        throw new InvalidOperationException(
            "Visual evidence summary failed validation: " + string.Join("; ", summary.Trust.Failures));
    }

    private static IReadOnlyList<FreeWVisualEvidenceExpectedScenario> BuildDefaultExpectedScenarios()
    {
        var expected = new List<FreeWVisualEvidenceExpectedScenario>();
        foreach (var scenario in FreeWVisualEvidencePlanner.Scenarios)
        {
            if (scenario.ScenarioId.StartsWith("f2-", StringComparison.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    WpfHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
            else if (scenario.ExpectedFeatureTags.Contains("avalonia", StringComparer.OrdinalIgnoreCase))
            {
                expected.Add(new FreeWVisualEvidenceExpectedScenario(
                    AvaloniaHostId,
                    scenario.ScenarioId,
                    scenario.MinimumExpectedOutputs));
            }
        }

        return expected;
    }

    private static void ValidateManifestHeader(
        FreeWVisualEvidenceManifest manifest,
        string sourceManifestPath,
        List<string> failures)
    {
        if (!string.Equals(manifest.SchemaId, FreeWVisualEvidencePlanner.SchemaId, StringComparison.Ordinal))
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported schema '{manifest.SchemaId}'");
        if (manifest.SchemaVersion != FreeWVisualEvidencePlanner.SchemaVersion)
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported schema version {manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture)}");
        if (!string.Equals(manifest.Product, "FreeW", StringComparison.Ordinal))
            failures.Add($"source manifest '{sourceManifestPath}' has unsupported product '{manifest.Product}'");
        if (manifest.Evidence.Count == 0)
            failures.Add($"source manifest '{sourceManifestPath}' contains no evidence rows");
    }

    private static FreeWVisualEvidenceNormalizedRow NormalizeRow(
        FreeWVisualEvidenceRow row,
        string sourceManifestPath,
        string manifestDirectory,
        string runRoot,
        HashSet<string> evidenceIds,
        List<string> summaryFailures)
    {
        var rowFailures = new List<string>();
        if (!evidenceIds.Add(row.EvidenceId))
            rowFailures.Add($"duplicate evidence id '{row.EvidenceId}'");
        if (string.IsNullOrWhiteSpace(row.HostId))
            rowFailures.Add("host id is required");
        if (string.IsNullOrWhiteSpace(row.ScenarioId))
            rowFailures.Add("scenario id is required");
        if (row.PixelWidth <= 0 || row.PixelHeight <= 0)
            rowFailures.Add("pixel dimensions must be positive");
        if (row.PixelStats.Width != row.PixelWidth || row.PixelStats.Height != row.PixelHeight)
            rowFailures.Add("pixel stats dimensions do not match evidence dimensions");
        if (!string.Equals(row.OutputName, row.PageExpectation.ExpectedOutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output name '{row.OutputName}' does not match expected '{row.PageExpectation.ExpectedOutputName}'");

        var outputPath = ResolveOutputPath(row.OutputPath, manifestDirectory);
        var relativeOutputPath = NormalizeRelativePath(runRoot, outputPath);
        if (!IsSubPathOf(runRoot, outputPath))
            rowFailures.Add($"output path '{relativeOutputPath}' is outside the run root");
        if (!string.Equals(Path.GetFileName(outputPath), row.OutputName, StringComparison.OrdinalIgnoreCase))
            rowFailures.Add($"output file name '{Path.GetFileName(outputPath)}' does not match manifest output name '{row.OutputName}'");

        var fileLength = 0L;
        var sha256 = string.Empty;
        if (File.Exists(outputPath))
        {
            var file = new FileInfo(outputPath);
            fileLength = file.Length;
            sha256 = ComputeSha256(outputPath);
            if (fileLength != row.ByteLength)
                rowFailures.Add($"byte length {row.ByteLength.ToString(CultureInfo.InvariantCulture)} does not match file length {fileLength.ToString(CultureInfo.InvariantCulture)} for '{relativeOutputPath}'");
        }
        else
        {
            rowFailures.Add($"output file '{relativeOutputPath}' does not exist");
        }

        rowFailures.AddRange(row.Trust.Failures);
        if (!row.Trust.Passed)
            rowFailures.Add($"manifest trust failed for '{row.OutputName}'");

        if (rowFailures.Count > 0)
        {
            foreach (var failure in rowFailures)
                summaryFailures.Add($"{row.HostId}/{row.ScenarioId}/{row.OutputName}: {failure}");
        }

        var trust = new FreeWVisualEvidenceTrust(rowFailures.Count == 0, rowFailures);
        return new FreeWVisualEvidenceNormalizedRow(
            row.EvidenceId,
            sourceManifestPath,
            row.ScenarioId,
            row.HostId,
            row.ExpectedFeatureTags,
            row.OutputName,
            relativeOutputPath,
            row.PixelWidth,
            row.PixelHeight,
            fileLength > 0 ? fileLength : row.ByteLength,
            sha256,
            row.PixelStats,
            row.PageExpectation.PageNumber,
            row.PageExpectation.PageCount,
            row.PageExpectation.LayoutKind,
            row.PageExpectation.ExpectedOutputName,
            trust);
    }

    private static IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> BuildScenarioSummaries(
        IReadOnlyList<FreeWVisualEvidenceNormalizedRow> rows,
        IReadOnlyList<FreeWVisualEvidenceExpectedScenario> expected,
        IReadOnlyDictionary<string, FreeWVisualEvidenceExpectedScenario> expectedByKey,
        List<string> failures)
    {
        var keys = rows
            .Select(r => ScenarioKey(r.HostId, r.ScenarioId))
            .Concat(expected.Select(e => ScenarioKey(e.HostId, e.ScenarioId)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scenarios = new List<FreeWVisualEvidenceNormalizedScenario>();
        foreach (var key in keys)
        {
            var keyParts = key.Split('\u001f');
            var hostId = keyParts[0];
            var scenarioId = keyParts.Length > 1 ? keyParts[1] : string.Empty;
            var scenarioRows = rows
                .Where(r => string.Equals(r.HostId, hostId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var scenarioFailures = scenarioRows
                .SelectMany(r => r.Trust.Failures)
                .ToList();
            var expectedScenario = expectedByKey.TryGetValue(key, out var item) ? item : null;
            var minimumExpectedOutputs = expectedScenario?.MinimumExpectedOutputs ?? 0;
            if (expectedScenario is not null && scenarioRows.Count < minimumExpectedOutputs)
            {
                scenarioFailures.Add(
                    $"expected at least {minimumExpectedOutputs.ToString(CultureInfo.InvariantCulture)} output(s), found {scenarioRows.Count.ToString(CultureInfo.InvariantCulture)}");
            }

            if (scenarioFailures.Count > 0)
            {
                foreach (var failure in scenarioFailures)
                    failures.Add($"{hostId}/{scenarioId}: {failure}");
            }

            scenarios.Add(new FreeWVisualEvidenceNormalizedScenario(
                hostId,
                scenarioId,
                minimumExpectedOutputs,
                scenarioRows.Count,
                expectedScenario is not null,
                new FreeWVisualEvidenceTrust(scenarioFailures.Count == 0, scenarioFailures)));
        }

        return scenarios;
    }

    private static string ScenarioKey(string hostId, string scenarioId) =>
        string.Concat(hostId, "\u001f", scenarioId);

    private static string ResolveOutputPath(string outputPath, string manifestDirectory)
    {
        if (Path.IsPathRooted(outputPath))
            return Path.GetFullPath(outputPath);

        return Path.GetFullPath(Path.Combine(manifestDirectory, outputPath));
    }

    private static string NormalizeRelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative.Replace('\\', '/');
    }

    private static bool IsSubPathOf(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullRoot, fullPath, StringComparison.OrdinalIgnoreCase))
            return true;

        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string EscapeMarkdown(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
