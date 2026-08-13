using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FreeX.App.Avalonia.Tests;

public sealed class ScenarioManagerDialogVisualParitySourceTests
{
    [Fact]
    public void ScenarioManagerDialog_UsesWpfBodyCompositionAndSharedCompactChrome()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, dialogChrome);");
        source.Should().Contain("scenarioList.ItemTemplate = new FuncDataTemplate<ScenarioManagerDialogScenarioItem>");
        source.Should().Contain("Text = item.Choice.Name,");
        source.Should().Contain("public override string ToString() => Choice.Name;");
        source.Should().Contain("ScenarioManagerDialogLayout.FieldBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.ScenarioListHeaderBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.LockedCheckBoxBottomMargin");
        source.Should().Contain("ScenarioManagerDialogLayout.HiddenCheckBoxBottomMargin");
        source.Should().Contain("ScenarioManagerDialogChromeStyle");
        source.Should().Contain("ControlHeight = 22");
        source.Should().Contain("TextBoxHeight = 22");
        source.Should().Contain("ButtonHeight = 22");
        source.Should().Contain("ButtonPadding = new Thickness(8, 1)");
        source.Should().Contain("ScenarioManagerDialogLayout.CloseRowTopMargin");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,Auto,Auto,Auto,Auto\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions($\"{ScenarioManagerDialogLayout.FieldLabelColumnWidth},*\")");
        source.Should().Contain("control.MinWidth = 0;");
        source.Should().Contain("control.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;");
        source.Should().Contain("AddScenarioManagerField(fields, 2");
        source.Should().Contain("AddScenarioManagerCheckBox(fields, 4, preventChangesBox);");
        source.Should().Contain("Margin = new Thickness(10, 20, 0, 0)");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"*,Auto,Auto\")");
        source.Should().NotContain("dialog.Content = new ScrollViewer");
    }

    [Fact]
    public void ScenarioManagerRangePickers_WrapGridFieldsAndRemainSharedSessionBacked()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ScenarioManagerRangePickers.cs"));

        source.Should().Contain("target?.Parent is not Panel field");
        source.Should().Contain("if (field is Grid parentGrid)");
        source.Should().Contain("FreeXAutomationIdCatalog.ScenarioManager.ChangingCellsPickerButton");
        source.Should().Contain("FreeXAutomationIdCatalog.ScenarioManager.ResultCellsPickerButton");
        source.Should().NotContain("\"ScenarioManagerChangingCellsPickerButton\"");
        source.Should().NotContain("\"ScenarioManagerResultCellsPickerButton\"");
        source.Should().Contain("owner.AttachDialogRangePicker(dialog, picker, target, targetId);");
    }

    [Fact]
    public void ParityCapture_FinalizerDrainDoesNotBlockTheAvaloniaUiThread()
    {
        var source = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));
        var methodStart = source.IndexOf(
            "private static Task ReleaseCompletedDialogCaptureResourcesAsync()",
            StringComparison.Ordinal);
        var methodEnd = source.IndexOf("/// <summary>", methodStart, StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        source[methodStart..methodEnd]
            .Should().Contain("return Task.Run(() =>")
            .And.Contain("GC.WaitForPendingFinalizers();");
        source.Should().Contain("await ReleaseCompletedDialogCaptureResourcesAsync();");
    }

    [Fact]
    public void ParityCaptureRoutes_UseTheOwnedLastRunningHeadlessSession()
    {
        var testDirectory = Path.GetDirectoryName(RepoFile(
            "tests",
            "FreeX.App.Avalonia.Tests",
            "ParityCaptureTests.cs"))!;
        var captureSources = Directory.GetFiles(testDirectory, "*.cs")
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .Where(file => !string.Equals(
                Path.GetFileName(file.Path),
                nameof(ScenarioManagerDialogVisualParitySourceTests) + ".cs",
                StringComparison.Ordinal))
            .Where(file => file.Source.Contains("CaptureParitySurfacesAsync", StringComparison.Ordinal))
            .ToArray();

        captureSources.Should().NotBeEmpty();
        captureSources.Should().OnlyContain(file =>
            file.Source.Contains(
                "[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]",
                StringComparison.Ordinal) &&
            file.Source.Contains("AvaloniaParityCaptureSession.Session", StringComparison.Ordinal));

        var collectionSource = File.ReadAllText(RepoFile(
            "tests",
            "FreeX.App.Avalonia.CaptureTests",
            "AvaloniaCaptureAssembly.cs"));
        collectionSource.Should().Contain("HeadlessUnitTestSession.GetOrStartForAssembly(")
            .And.Contain("[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]");

        var sharedProjectSource = File.ReadAllText(RepoFile(
            "tests", "FreeX.App.Avalonia.CaptureTests", "CaptureTests.Shared.props"));
        captureSources.Should().OnlyContain(file => sharedProjectSource.Contains(
            Path.GetFileName(file.Path),
            StringComparison.Ordinal));

        var expectedMethods = captureSources
            .SelectMany(file => FindCaptureMethodNames(file.Source))
            .ToHashSet(StringComparer.Ordinal);
        var captureProjectDirectory = Path.GetDirectoryName(RepoFile(
            "tests", "FreeX.App.Avalonia.CaptureTests", "CaptureTests.Shared.props"))!;
        var captureFilterBatches = Directory.GetFiles(
                captureProjectDirectory,
                "FreeX.App.Avalonia.CaptureTests*.csproj")
            .Select(file => ReadFilterTerms(file, "FullyQualifiedName~", '|'))
            .ToArray();
        captureFilterBatches.Should().HaveCountGreaterThan(1)
            .And.OnlyContain(batch => batch.Length > 0 && batch.Length <= 6);

        var captureFilters = captureFilterBatches.SelectMany(batch => batch).ToArray();
        captureFilters.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(expectedMethods);

        var mainFilter = ReadFilterTerms(RepoFile(
            "tests", "FreeX.App.Avalonia.Tests", "FreeX.App.Avalonia.Tests.csproj"),
            "FullyQualifiedName!~",
            '&');
        mainFilter.Should().BeEquivalentTo(expectedMethods);
    }

    /// <summary>
    /// Names every public test method that reaches a parity capture, INCLUDING those that reach it
    /// through a private helper.
    /// </summary>
    /// <remarks>
    /// This used to scan for lines containing <c>CaptureParitySurfacesAsync</c> and walk backwards to
    /// the nearest preceding public signature. That silently under-reported the moment a capture moved
    /// into a shared helper: the single call site inside the helper was credited to whichever public
    /// method happened to sit above it in the file, and its siblings vanished from the expected set.
    /// R130 hit exactly that -- one 39-dialog test was split into four batch methods delegating to
    /// <c>RunAssignedDialogsBatchAsync</c>, and three of the four disappeared from this contract.
    ///
    /// Under-reporting here is not cosmetic: a capture route missing from the expected set is a route
    /// nothing forces into its own batch project, so it keeps running inside the main assembly's
    /// process -- which is the precise condition that lets the headless glyph-run leak accumulate.
    /// So resolve delegation transitively instead of assuming one call site per public method.
    /// </remarks>
    private static IEnumerable<string> FindCaptureMethodNames(string source)
    {
        var methods = ParseMethods(source);

        // Seed with methods that call the capture API directly (any visibility -- helpers count).
        var capture = methods
            .Where(method => method.Body.Contains("CaptureParitySurfacesAsync", StringComparison.Ordinal))
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Then close over delegation: a method that calls a capture method is itself a capture route.
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var method in methods)
            {
                if (capture.Contains(method.Name))
                    continue;
                if (!capture.Any(name => Regex.IsMatch(method.Body, @"\b" + Regex.Escape(name) + @"\s*\(")))
                    continue;
                capture.Add(method.Name);
                grew = true;
            }
        }

        return methods
            .Where(method => method.IsPublic && capture.Contains(method.Name))
            .Select(method => method.Name);
    }

    /// <summary>
    /// Splits a source file into methods, treating each signature's body as the text running up to the
    /// next signature. That is coarser than real brace matching but robust for this file shape, and it
    /// keeps expression-bodied members (<c>public Task Foo() =&gt; Helper(...);</c>) intact.
    /// </summary>
    private static (string Name, bool IsPublic, string Body)[] ParseMethods(string source)
    {
        var lines = source.Split('\n');
        var signature = new Regex(
            @"(?<modifiers>(?:public|private|protected|internal)(?:\s+(?:static|async|override|sealed|virtual|new))*)\s+"
            + @"(?:Task(?:<[^>]+>)?|void|IEnumerable<[^>]+>|string(?:\[\])?|bool|int)\s+(?<name>\w+)\s*\(");

        var starts = new List<(string Name, bool IsPublic, int Line)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = signature.Match(lines[i]);
            if (!match.Success)
                continue;
            starts.Add((
                match.Groups["name"].Value,
                match.Groups["modifiers"].Value.StartsWith("public", StringComparison.Ordinal),
                i));
        }

        var methods = new (string Name, bool IsPublic, string Body)[starts.Count];
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i].Line;
            var end = i + 1 < starts.Count ? starts[i + 1].Line : lines.Length;
            methods[i] = (
                starts[i].Name,
                starts[i].IsPublic,
                string.Join("\n", lines[start..end]));
        }

        return methods;
    }

    private static string[] ReadFilterTerms(string projectPath, string prefix, char separator) =>
        XDocument.Load(projectPath)
            .Descendants("VSTestTestCaseFilter")
            .Single()
            .Value
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.StartsWith(prefix, StringComparison.Ordinal) ? term[prefix.Length..] : term)
            .ToArray();

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
