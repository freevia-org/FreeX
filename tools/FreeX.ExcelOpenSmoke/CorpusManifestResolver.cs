using System.Text;
using FreeX.Core.IO;
using FreeX.Core.IO.Tests;

internal static class CorpusManifestResolver
{
    private static readonly HashSet<string> DefaultStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "supported-pass",
        "supported-metadata-pass",
        "supported-pivot-metadata-pass",
        "public-pass"
    };
    private static readonly HashSet<string> GeneratedFixtureDefaultStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "supported-pass"
    };

    public static CorpusManifestSelection Resolve(
        SmokeOptions options,
        WorkbookValidationWorkflow workflow)
    {
        if (string.IsNullOrWhiteSpace(options.CorpusManifestPath))
            throw new ArgumentException("--corpus-manifest requires a manifest path.");

        var manifestPath = Path.GetFullPath(options.CorpusManifestPath);
        if (!File.Exists(manifestPath))
            throw new ArgumentException($"Corpus manifest was not found: {options.CorpusManifestPath}");

        var manifestDirectory = Path.GetDirectoryName(manifestPath)
            ?? throw new ArgumentException($"Corpus manifest path has no directory: {manifestPath}");
        var sourceFilter = ToFilter(options.CorpusSources);
        var statusFilter = options.CorpusStatuses.Count > 0
            ? ToFilter(options.CorpusStatuses)
            : DefaultStatuses;

        var inputs = new List<WorkbookSmokeInput>();
        var skipped = new List<CorpusManifestSkip>();

        foreach (var row in ReadRows(manifestPath))
        {
            if (sourceFilter.Count > 0 && !sourceFilter.Contains(row.SourceType))
            {
                skipped.Add(new CorpusManifestSkip(row, "source-filter", null));
                continue;
            }

            if (!statusFilter.Contains(row.ExpectedStatus))
            {
                skipped.Add(new CorpusManifestSkip(row, "status-filter", null));
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(manifestDirectory, row.RelativePath));
            if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new CorpusManifestSkip(row, "not-xlsx", fullPath));
                continue;
            }

            if (!File.Exists(fullPath))
            {
                skipped.Add(new CorpusManifestSkip(row, "missing-file", fullPath));
                continue;
            }

            inputs.Add(new WorkbookSmokeInput(
                fullPath,
                workflow,
                $"Corpus {row.SourceType} row {row.Id}",
                CorpusRow: row));
        }

        return new CorpusManifestSelection(manifestPath, inputs, skipped);
    }

    public static CorpusManifestSelection GenerateSupportedFixtures(
        SmokeOptions options,
        WorkbookValidationWorkflow workflow,
        string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(options.CorpusManifestPath))
            throw new ArgumentException("--generate-supported-corpus-fixtures requires --corpus-manifest.");

        var manifestPath = Path.GetFullPath(options.CorpusManifestPath);
        if (!File.Exists(manifestPath))
            throw new ArgumentException($"Corpus manifest was not found: {options.CorpusManifestPath}");

        var sourceFilter = ToFilter(options.CorpusSources);
        var statusFilter = options.CorpusStatuses.Count > 0
            ? ToFilter(options.CorpusStatuses)
            : GeneratedFixtureDefaultStatuses;

        Directory.CreateDirectory(outputDirectory);
        var inputs = new List<WorkbookSmokeInput>();
        var skipped = new List<CorpusManifestSkip>();

        foreach (var row in ReadRows(manifestPath))
        {
            if (sourceFilter.Count > 0 && !sourceFilter.Contains(row.SourceType))
            {
                skipped.Add(new CorpusManifestSkip(row, "source-filter", null));
                continue;
            }

            if (!string.Equals(row.SourceType, "generated", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new CorpusManifestSkip(row, "not-generated-source", null));
                continue;
            }

            if (!statusFilter.Contains(row.ExpectedStatus))
            {
                skipped.Add(new CorpusManifestSkip(row, "status-filter", null));
                continue;
            }

            if (!string.Equals(Path.GetExtension(row.RelativePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new CorpusManifestSkip(row, "not-xlsx", null));
                continue;
            }

            if (!XlsxCorpusFixtureFactory.CanCreate(row.Id))
            {
                skipped.Add(new CorpusManifestSkip(row, "no-generated-fixture", null));
                continue;
            }

            var outputPath = Path.Combine(outputDirectory, Path.GetFileName(row.RelativePath));
            SaveGeneratedFixture(row.Id, outputPath);
            Console.WriteLine($"Generated: {outputPath}");
            inputs.Add(new WorkbookSmokeInput(
                outputPath,
                workflow,
                $"Generated corpus row {row.Id}",
                CorpusRow: row));
        }

        return new CorpusManifestSelection(manifestPath, inputs, skipped);
    }

    private static void SaveGeneratedFixture(string id, string outputPath)
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var workbook = XlsxCorpusFixtureFactory.Create(id);
        using var output = File.Create(outputPath);
        new XlsxFileAdapter().Save(workbook, output);
    }

    private static HashSet<string> ToFilter(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<CorpusManifestRow> ReadRows(string manifestPath)
    {
        using var reader = File.OpenText(manifestPath);
        var headerLine = reader.ReadLine();
        if (headerLine is null)
            throw new ArgumentException($"Corpus manifest is empty: {manifestPath}");

        var header = ParseCsvLine(headerLine);
        var expectedHeader = new[]
        {
            "id",
            "path",
            "source_type",
            "source_url",
            "retrieved_on",
            "license",
            "feature_tags",
            "expected_warnings",
            "expected_status",
            "notes"
        };
        if (!header.SequenceEqual(expectedHeader, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Corpus manifest has an unexpected header: {manifestPath}");

        var rows = new List<CorpusManifestRow>();
        for (var lineNumber = 2; !reader.EndOfStream; lineNumber++)
        {
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = ParseCsvLine(line);
            if (fields.Count != expectedHeader.Length)
            {
                throw new ArgumentException(
                    $"Corpus manifest line {lineNumber} has {fields.Count} fields; expected {expectedHeader.Length}.");
            }

            rows.Add(new CorpusManifestRow(
                fields[0],
                fields[1],
                fields[2],
                fields[3],
                fields[4],
                fields[5],
                fields[6],
                fields[7],
                fields[8],
                fields[9]));
        }

        return rows;
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            field.Append(current);
        }

        fields.Add(field.ToString());
        return fields;
    }
}
