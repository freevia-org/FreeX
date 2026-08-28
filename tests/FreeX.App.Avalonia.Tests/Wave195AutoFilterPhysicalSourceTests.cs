using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Avalonia.Headless;
using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class Wave195AutoFilterPhysicalSourceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void Manifest_Lists75ExistingHashMatchingArtifacts_AndReferencedCommitsAreAncestors()
    {
        using var manifest = ReadManifest();
        var root = manifest.RootElement;
        var evidenceRoot = EvidenceRoot;

        root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("wave").GetInt32().Should().Be(195);
        root.GetProperty("status").GetString().Should().Be("passed");
        root.GetProperty("app").GetString().Should().Be("FreeX");
        root.GetProperty("platform").GetString().Should().Be("linux");
        root.GetProperty("shell").GetString().Should().Be("avalonia");
        root.GetProperty("validationMode").GetString().Should().Be("physical-only");
        root.GetProperty("claimBoundary").GetString().Should().Contain("not exhaustive");

        var files = root.GetProperty("files").EnumerateArray().ToArray();
        files.Should().HaveCount(75);
        var relativePaths = files.Select(file => file.GetProperty("path").GetString()!).ToArray();
        relativePaths.Should().OnlyHaveUniqueItems();
        relativePaths.Count(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Should().Be(58);

        foreach (var file in files)
        {
            var relativePath = file.GetProperty("path").GetString()!;
            var fullPath = ResolveEvidencePath(evidenceRoot, relativePath);
            File.Exists(fullPath).Should().BeTrue($"the manifest artifact '{relativePath}' must exist");

            var hashMode = file.GetProperty("hashMode").GetString();
            hashMode.Should().BeOneOf("raw", "canonical-lf");
            var actualHash = ComputeSha256(fullPath, hashMode == "canonical-lf");
            actualHash.Should().Be(
                file.GetProperty("sha256").GetString()!.ToLowerInvariant(),
                $"the manifest hash for '{relativePath}' must match its committed bytes");
        }

        var head = RunGit(evidenceRoot, "rev-parse", "HEAD").StandardOutput.Trim();
        head.Should().MatchRegex("^[0-9a-f]{40}$");

        var commits = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddCommit(root, commits, "appPayloadSourceCommit");
        AddCommit(root, commits, "captureHarnessEquivalentCommit");
        AddCommit(root, commits, "postCaptureCleanupCommit");
        AddCommit(root, commits, "packagingBaseCommit");
        AddCommit(root, commits, "originMainAtPackaging");
        foreach (var session in root.GetProperty("sessions").EnumerateArray())
        {
            AddCommit(session, commits, "appPayloadSourceCommit");
            AddCommit(session, commits, "captureHarnessEquivalentCommit");
        }

        commits.Should().HaveCount(5);
        foreach (var commit in commits)
        {
            RunGit(evidenceRoot, "cat-file", "-e", $"{commit}^{{commit}}").ExitCode.Should().Be(0,
                $"referenced commit {commit} must exist");
            RunGit(evidenceRoot, "merge-base", "--is-ancestor", commit, head).ExitCode.Should().Be(0,
                $"referenced commit {commit} must be an ancestor of the checked-out HEAD");
        }
    }

    [Fact]
    public void Sessions_ContainPassedSelectorsAndMeaningfulPostconditions()
    {
        using var manifest = ReadManifest();
        var root = manifest.RootElement;
        var evidenceRoot = EvidenceRoot;
        var sessions = root.GetProperty("sessions").EnumerateArray().ToArray();
        sessions.Should().HaveCount(2);

        foreach (var session in sessions)
        {
            var name = session.GetProperty("name").GetString()!;
            SessionContracts.Should().ContainKey(name);
            var contract = SessionContracts[name];
            var selector = session.GetProperty("selector").GetString()!;
            selector.Should().Be(contract.Selector);
            session.GetProperty("resultId").GetString().Should().Be(contract.ResultId);
            session.GetProperty("reloadWitnessPassed").GetBoolean().Should().BeTrue();
            session.GetProperty("payloadFileCount").GetInt32().Should().Be(778);
            session.GetProperty("payloadFingerprint").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
            session.GetProperty("appImageId").GetString().Should().MatchRegex("^sha256:[0-9a-f]{64}$");

            AssertSelectorDispatch(selector, contract, RepositoryRoot);
            AssertInteractionReport(session, contract, evidenceRoot);
            AssertX11Report(session, contract, evidenceRoot);
            AssertPostcondition(session, contract, evidenceRoot);
        }
    }

    [Fact]
    public Task ProductionFilterEntryPoint_AndsTwoColumnsAndClearsEachColumn() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Multi Column");
            window.Session.SelectSheet(sheet.Id);
            PopulateRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 7, 3));
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C7", null);

            window.RunAutoFilterForTest(range, 0, ["North"]);
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColumnId == 0 && column.Values.SequenceEqual(new[] { "North" }))
                .Should().ContainSingle();
            window.RunAutoFilterForTest(range, 1, ["Hardware"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u, 6u, 7u]);
            sheet.AutoFilter!.FilterColumns.Should().HaveCount(2);
            sheet.AutoFilter.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Hardware" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 1, ["Software"]);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 5u, 6u, 7u]);
            sheet.AutoFilter!.FilterColumns.Should().HaveCount(2);
            sheet.AutoFilter.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Software" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 0, []);
            sheet.FilterHiddenRows.Should().BeEquivalentTo([2u, 4u, 6u]);
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColumnId == 1 && column.Values.SequenceEqual(new[] { "Software" }))
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 1, []);
            sheet.FilterHiddenRows.Should().BeEmpty();
            sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ProductionDropdownPlanner_RoutesBColumnChecklistAndResult() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Planner Route");
            window.Session.SelectSheet(sheet.Id);
            PopulateRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 7, 3));
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:C7", null);

            window.RunAutoFilterForTest(range, 0, ["North"]);
            window.RunAutoFilterForTest(range, 1, ["Hardware"]);

            AutoFilterDropdownMenuPlanner.TryPlan(
                    range,
                    new CellAddress(sheet.Id, 1, 2),
                    out var plan)
                .Should().BeTrue();
            plan.FilterColumnOffset.Should().Be(1);
            var menuPlan = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
                window.Session.Workbook,
                sheet,
                plan,
                InvariantAutoFilterMenuTextProvider.Instance,
                InvariantAutoFilterMenuTextProvider.BlankDisplayText);
            var menu = AutoFilterMenuPlanner.Build(menuPlan);

            menu.Header.Should().Be("Category");
            menu.Items
                .Where(item => item.Kind == AutoFilterMenuItemKind.ChecklistItem)
                .Select(item => (item.Label, item.IsChecked))
                .Should().Equal(("Hardware", true), ("Software", false));
            var result = AutoFilterMenuPlanner.BuildResult(
                AutoFilterMenuPlanner.CreateDialogItems(menu),
                searchText: "",
                criteriaText: "");
            result.SelectedValues.Should().Equal("Hardware");

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    [Fact]
    public Task ProductionColorFilterClear_RemovesTheSerializedCriterion() =>
        Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("Wave195 Color Clear");
            window.Session.SelectSheet(sheet.Id);
            PopulateColorRows(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 2));
            var red = new CellColor(0, 176, 80);
            sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B5", null);

            window.Session.ExecuteReviewCommand(new CellFillColorFilterCommand(sheet.Id, range, 0, red))
                .Success.Should().BeTrue();
            sheet.AutoFilter!.FilterColumns
                .Where(column => column.ColorFilter is { CellColor: true, Color: var color } && color == red)
                .Should().ContainSingle();

            window.RunAutoFilterForTest(range, 0, []);

            sheet.AutoFilter!.FilterColumns.Should().BeEmpty();
            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);

    private static void PopulateRows(Sheet sheet)
    {
        var rows = new (string Region, string Category, double Amount)[]
        {
            ("Region", "Category", 0),
            ("North", "Hardware", 100),
            ("North", "Software", 200),
            ("South", "Hardware", 300),
            ("South", "Software", 400),
            ("East", "Hardware", 500),
            ("East", "Software", 600)
        };

        for (var row = 0; row < rows.Length; row++)
        {
            var addressRow = (uint)(row + 1);
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 1), new TextValue(rows[row].Region));
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 2), new TextValue(rows[row].Category));
            if (row > 0)
                sheet.SetCell(new CellAddress(sheet.Id, addressRow, 3), new NumberValue(rows[row].Amount));
        }
    }

    private static void PopulateColorRows(Sheet sheet)
    {
        var rows = new[] { "Region", "North", "South", "East", "West" };
        for (var row = 0; row < rows.Length; row++)
        {
            var addressRow = (uint)(row + 1);
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 1), new TextValue(rows[row]));
            sheet.SetCell(new CellAddress(sheet.Id, addressRow, 2), new TextValue("Value"));
        }
    }

    private static readonly IReadOnlyDictionary<string, SessionContract> SessionContracts =
        new Dictionary<string, SessionContract>(StringComparer.Ordinal)
        {
            ["multi-column"] = new(
                "autofilter-multi-column-persistence",
                "autofilter-multi-column-criteria-change-clear-physical",
                "multi-column/x11-validation/autofilter-multi-column-postcondition.txt",
                [
                    "autofilter-multi-column-region-applied.png",
                    "autofilter-multi-column-both-applied.png",
                    "autofilter-multi-column-category-changed.png",
                    "autofilter-multi-column-region-cleared.png",
                    "autofilter-multi-column-all-cleared.png",
                    "autofilter-multi-column-reload-witness-unsaved.png",
                    "autofilter-multi-column-reload-discard-prompt.png",
                    "autofilter-multi-column-reopened.png",
                    "autofilter-multi-column-popup-gate.txt",
                    "autofilter-multi-column-postcondition.txt"
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["menu-open"] = "true",
                    ["region-visible"] = "North,North,",
                    ["both-visible"] = "North,",
                    ["category-changed-visible"] = "North,",
                    ["region-cleared-visible"] = "North,South,East,",
                    ["all-cleared-visible"] = "North,North,South,South,East,East,",
                    ["region-package"] = "ref=A1:C7|columns=0:North;",
                    ["both-package"] = "ref=A1:C7|columns=0:North;1:Hardware;",
                    ["changed-package"] = "ref=A1:C7|columns=0:North;1:Software;",
                    ["region-cleared-package"] = "ref=A1:C7|columns=1:Software;",
                    ["cleared-package"] = "ref=A1:C7|columns=",
                    ["dialog-open"] = "true",
                    ["dialog-closed"] = "true",
                    ["reload-witness-before"] = "__FREEX_RELOAD_WITNESS__",
                    ["reload-witness-before-read"] = "true",
                    ["reload-witness-discarded"] = "true",
                    ["reload-witness-after"] = "East",
                    ["reload-witness-after-read"] = "true",
                    ["reload-witness-passed"] = "true",
                    ["reopened-visible"] = "North,North,South,South,East,East,"
                }),
            ["color-change-clear"] = new(
                "autofilter-color-change-clear-persistence",
                "autofilter-color-criteria-change-clear-physical",
                "color-change-clear/x11-validation/autofilter-color-change-clear-postcondition.txt",
                [
                    "autofilter-color-change-clear-green-menu-open.png",
                    "autofilter-color-change-clear-green-applied.png",
                    "autofilter-color-change-clear-yellow-menu-open.png",
                    "autofilter-color-change-clear-yellow-applied.png",
                    "autofilter-color-change-clear-clear-menu-open.png",
                    "autofilter-color-change-clear-cleared.png",
                    "autofilter-color-change-clear-reload-witness-unsaved.png",
                    "autofilter-color-change-clear-reload-discard-prompt.png",
                    "autofilter-color-change-clear-reopened.png",
                    "autofilter-color-change-clear-popup-gate.txt",
                    "autofilter-color-change-clear-postcondition.txt"
                ],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["green-menu-open"] = "true",
                    ["green-criteria"] = "fill:#00B050",
                    ["green-visible"] = "North,East,",
                    ["yellow-menu-open"] = "true",
                    ["yellow-criteria"] = "fill:#FFC000",
                    ["yellow-visible"] = "South,West,",
                    ["clear-menu-open"] = "true",
                    ["cleared-visible"] = "North,South,East,West,",
                    ["green-package"] = "ref=A1:B5|colId=0|cellColor=1|fill=FF00B050",
                    ["yellow-package"] = "ref=A1:B5|colId=0|cellColor=1|fill=FFFFC000",
                    ["cleared-package"] = "ref=A1:B5|columns=",
                    ["dialog-open"] = "true",
                    ["dialog-closed"] = "true",
                    ["reload-witness-before"] = "__FREEX_RELOAD_WITNESS__",
                    ["reload-witness-before-read"] = "true",
                    ["reload-witness-discarded"] = "true",
                    ["reload-witness-after"] = "East",
                    ["reload-witness-after-read"] = "true",
                    ["reload-witness-passed"] = "true",
                    ["reopened-visible"] = "North,South,East,West,"
                })
        };

    private static JsonDocument ReadManifest() =>
        JsonDocument.Parse(File.ReadAllText(
            TestWorkspaceFileLocator.FindFromWorkspaceRoot(
                "docs", "parity", "evidence", "wave195-freex-autofilter-criteria-workflows-20260828", "manifest.json")));

    private static string EvidenceRoot =>
        Path.GetDirectoryName(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "docs", "parity", "evidence", "wave195-freex-autofilter-criteria-workflows-20260828", "manifest.json"))!;

    private static string RepositoryRoot =>
        Path.GetDirectoryName(TestWorkspaceFileLocator.FindFromWorkspaceRoot("FreeX.slnx"))!;

    private static void AddCommit(JsonElement element, ISet<string> commits, string propertyName)
    {
        var commit = element.GetProperty(propertyName).GetString();
        commit.Should().NotBeNullOrWhiteSpace();
        commits.Add(commit!);
    }

    private static string ResolveEvidencePath(string evidenceRoot, string relativePath)
    {
        Path.IsPathRooted(relativePath).Should().BeFalse($"manifest path '{relativePath}' must be relative");
        var root = Path.GetFullPath(evidenceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            .Should().BeTrue($"manifest path '{relativePath}' must stay under the evidence directory");
        return fullPath;
    }

    private static string ComputeSha256(string path, bool canonicalizeLineEndings)
    {
        var bytes = File.ReadAllBytes(path);
        if (canonicalizeLineEndings)
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            bytes = Encoding.UTF8.GetBytes(text);
        }

        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void AssertSelectorDispatch(string selector, SessionContract contract, string repositoryRoot)
    {
        var runner = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "Run-FreeXLinuxInteractionValidation.ps1"));
        var probe = File.ReadAllText(Path.Combine(repositoryRoot, "tools", "LinuxInteractiveDocker", "run-freex-input-probes.sh"));
        var functionName = $"probe_{selector.Replace('-', '_')}_physical";

        runner.Should().Contain($"$PhysicalProbeSelector -eq \"{selector}\"");
        runner.Should().Contain($"@(\"{contract.ResultId}\")");
        probe.Should().Contain($"if [[ \"$probe_selector\" == \"{selector}\" ]]; then");
        probe.Should().Contain($"{functionName}() {{");
        probe.Should().Contain(functionName, $"the {selector} dispatch must invoke its physical function");
    }

    private static void AssertInteractionReport(JsonElement session, SessionContract contract, string evidenceRoot)
    {
        var sessionRoot = ResolveEvidencePath(evidenceRoot, session.GetProperty("name").GetString()!);
        var reportPath = Path.Combine(sessionRoot, "run-report", "interaction-validation.json");
        File.Exists(reportPath).Should().BeTrue();
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("app").GetString().Should().Be("FreeX");
        root.GetProperty("platform").GetString().Should().Be("linux");
        root.GetProperty("shell").GetString().Should().Be("avalonia");
        root.GetProperty("validationMode").GetString().Should().Be("physical-only");
        root.GetProperty("coverage").GetProperty("exhaustive").GetBoolean().Should().BeFalse();
        root.GetProperty("coverage").GetProperty("scope").GetString().Should().Contain("bounded");

        var results = root.GetProperty("results").EnumerateArray().ToArray();
        results.Should().ContainSingle();
        var result = results.Single();
        result.GetProperty("id").GetString().Should().Be(contract.ResultId);
        result.GetProperty("category").GetString().Should().Be("x11-input");
        result.GetProperty("status").GetString().Should().Be("passed");
        result.GetProperty("evidenceLevel").GetString().Should().Be("physical-x11-input");

        var evidence = result.GetProperty("evidence").GetString()!;
        foreach (var evidenceFile in contract.RequiredEvidence)
            evidence.Should().Contain(evidenceFile);
        result.GetProperty("artifacts").EnumerateArray().Select(item => item.GetString()!).Should()
            .Contain(Path.GetFileName(contract.Postcondition));
        root.GetProperty("summary").GetProperty("passed").GetInt32().Should().Be(1);
        root.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(1);
    }

    private static void AssertX11Report(JsonElement session, SessionContract contract, string evidenceRoot)
    {
        var sessionRoot = ResolveEvidencePath(evidenceRoot, session.GetProperty("name").GetString()!);
        var reportPath = Path.Combine(sessionRoot, "x11-validation", "x11-input-results.json");
        File.Exists(reportPath).Should().BeTrue();
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        var root = report.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
        root.GetProperty("platform").GetString().Should().Be("linux");
        root.GetProperty("shell").GetString().Should().Be("avalonia");
        root.GetProperty("calibration").GetProperty("status").GetString().Should().Be("passed");
        root.GetProperty("calibration").GetProperty("selectionColor").GetString().Should().Be("#217346");
        root.GetProperty("summary").GetProperty("passed").GetInt32().Should().Be(1);
        root.GetProperty("summary").GetProperty("failed").GetInt32().Should().Be(0);
        root.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(1);

        var results = root.GetProperty("results").EnumerateArray().ToArray();
        results.Should().ContainSingle();
        var result = results.Single();
        result.GetProperty("id").GetString().Should().Be(contract.ResultId);
        result.GetProperty("status").GetString().Should().Be("passed");
        result.GetProperty("evidenceLevel").GetString().Should().Be("physical-x11-input");
    }

    private static void AssertPostcondition(JsonElement session, SessionContract contract, string evidenceRoot)
    {
        var postcondition = session.GetProperty("postcondition").GetString()!;
        postcondition.Should().Be(contract.Postcondition);
        var path = ResolveEvidencePath(evidenceRoot, postcondition);
        File.Exists(path).Should().BeTrue();

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var separator = line.IndexOf('=');
            separator.Should().BeGreaterThan(0, $"postcondition line '{line}' must be a key/value pair");
            values.Add(line[..separator], line[(separator + 1)..]);
        }

        values.Should().HaveCountGreaterThan(contract.Postconditions.Count - 1);
        foreach (var expected in contract.Postconditions)
        {
            values.Should().ContainKey(expected.Key);
            values[expected.Key].Should().Be(expected.Value, $"postcondition '{expected.Key}' must preserve the workflow result");
        }
    }

    private static GitResult RunGit(string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new GitResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record SessionContract(
        string Selector,
        string ResultId,
        string Postcondition,
        string[] RequiredEvidence,
        IReadOnlyDictionary<string, string> Postconditions);

    private sealed record GitResult(int ExitCode, string StandardOutput, string StandardError);
}
