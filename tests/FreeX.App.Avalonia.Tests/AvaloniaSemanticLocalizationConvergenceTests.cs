using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Free.Shared.Localization;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.ThemeUI;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaSemanticLocalizationConvergenceTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly Regex RawSemanticAssignment = new(
        "(?:\\b(?:Text|Content|Header|Title|ToolTip|Watermark|PlaceholderText|Description|Message|Prompt|Label)" +
        "\\s*=\\s*|AutomationProperties\\.Set(?:Name|HelpText)\\([^,\\r\\n]+,\\s*)" +
        "\\\"(?<text>(?:\\\\.|[^\\\"\\\\])*)\\\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ScopedIndirectSemanticLiteralPatterns =
    {
        "ApplyZoomPercent(inputPlan.ZoomPercent, \"Zoom failed.\")",
        "ApplyZoomPercent(zoomPercent, \"Zoom failed.\")",
        "\"Normal view\",",
        "\"Page layout view\",",
        "\"Page break preview\",",
        "CreateGoToSpecialValueTypeBox(\"Numbers\"",
        "CreateGoToSpecialValueTypeBox(\"Text\"",
        "CreateGoToSpecialValueTypeBox(\"Logicals\"",
        "CreateGoToSpecialValueTypeBox(\"Errors\"",
        "Custom sort supports cell values, cell color, font color",
        "? \"None\" : choice.Label",
        "CreateHeaderCell(optionsState.LeftToRight ? \"Sort by row\" : \"Sort by\"",
        "CreateHeaderCell(\"Sort On\"",
        "CreateHeaderCell(\"Order\"",
        "CreateHeaderCell(\"Color\"",
        "CreateHeaderCell(\"Icon\"",
        "\"List range:\",",
        "\"Criteria range:\",",
        "\"Copy to:\",",
        "CreateForecastSheetField(\"Forecast periods\"",
        "AddSymbolChooserField(grid, 0, \"Font:\"",
        "AddSymbolChooserField(grid, 4, \"Search:\"",
        "$\"Symbols shown:",
        "\"Special Characters\",",
        "$\"Inserted {selection.Symbol} into",
        "result.ErrorMessage ?? \"Could not insert the symbol.\"",
        "Content = PrettyStyleName(preset)",
    };

    private static readonly IReadOnlyDictionary<(string File, string Literal), int> JustifiedRendererLiterals =
        new Dictionary<(string File, string Literal), int>
        {
            [("FreeX.App.Avalonia/MainWindow.cs", "B")] = 1,
            [("FreeX.App.Avalonia/MainWindow.cs", "I")] = 1,
            [("FreeX.App.Avalonia/MainWindow.cs", "U")] = 2,
            [("FreeX.App.Avalonia/MainWindow.cs", "S")] = 1,
            [("FreeX.App.Avalonia/MainWindow.cs", "A+")] = 1,
            [("FreeX.App.Avalonia/MainWindow.cs", "A-")] = 1,
            [("FreeX.App.Avalonia/MainWindow.cs", "A")] = 1,
            [("FreeX.App.Avalonia/MainWindow.SlicerTimelinePane.cs", "X")] = 1,
        };

    private static readonly IReadOnlyDictionary<(string File, string Literal), int> CatalogCollisionAllowlist =
        new Dictionary<(string File, string Literal), int>
        {
            [("FreeX.App.Avalonia/MainWindow.FillSeries.cs", "1")] = 1,
            [("FreeX.App.Host/PrintPreviewDialog.Layout.cs", "1")] = 3,
        };

    [Fact]
    public void ActiveAvaloniaRenderer_DoesNotAssignResourceTextFromRawLiterals()
    {
        var resourceValues = UiText.GetNeutralResourceKeys()
            .Select(UiText.GetNeutral)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var sources = AuditedRendererSources();
        var failures = new List<string>();
        var allowedCounts = new Dictionary<(string File, string Literal), int>();

        foreach (var (name, source) in sources)
        {
            foreach (Match match in RawSemanticAssignment.Matches(source))
            {
                var literal = Regex.Unescape(match.Groups["text"].Value);
                if (!resourceValues.Contains(literal))
                    continue;

                var key = (name, literal);
                if (CatalogCollisionAllowlist.ContainsKey(key))
                {
                    allowedCounts[key] = allowedCounts.GetValueOrDefault(key) + 1;
                    continue;
                }

                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                failures.Add($"{name}:{line}: {literal}");
            }
        }

        allowedCounts.Should().BeEquivalentTo(CatalogCollisionAllowlist);
        failures.Should().BeEmpty(
            "semantic renderer text that already exists in the localization catalog must be resolved through UiText");
    }

    [Fact]
    public void ActiveAvaloniaRenderer_DoesNotPassScopedSemanticTextAsRawArguments()
    {
        var failures = new List<string>();

        foreach (var (name, source) in AuditedRendererSources())
        {
            foreach (var pattern in ScopedIndirectSemanticLiteralPatterns)
            {
                var index = source.IndexOf(pattern, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                var line = source.AsSpan(0, index).Count('\n') + 1;
                failures.Add($"{name}:{line}: {pattern}");
            }
        }

        failures.Should().BeEmpty(
            "semantic text passed indirectly to renderer helpers must also be resolved through UiText");
    }

    [Fact]
    public void ShippingRenderers_OnlyUsePreciselyAllowlistedRawSemanticLiterals()
    {
        var actual = new Dictionary<(string File, string Literal), int>();

        foreach (var (name, source) in AuditedRendererSources())
        {
            foreach (Match match in RawSemanticAssignment.Matches(source))
            {
                var literal = Regex.Unescape(match.Groups["text"].Value);
                if (!literal.Any(char.IsLetter))
                    continue;

                var key = (name, literal);
                actual[key] = actual.GetValueOrDefault(key) + 1;
            }
        }

        actual.Should().BeEquivalentTo(
            JustifiedRendererLiterals,
            options => options.WithStrictOrdering(),
            "raw renderer text is limited to language-independent formatting glyphs with exact counts");
    }

    [Fact]
    public async Task ToolbarAndColorPicker_ConstructPseudoLocalizedSemanticText()
    {
        await Session.Dispatch(() => WithPseudoCulture(() =>
        {
            var window = new MainWindow([]);

            AssertPseudoLocalized(Field<Button>(window, "_openButton").Content);
            AssertPseudoLocalized(Field<Button>(window, "_fillCellsButton").Content);
            AssertPseudoLocalized(Field<Button>(window, "_clearButton").Content);
            AssertPseudoLocalized(AutomationProperties.GetName(Field<TextBlock>(window, "_selectionStatsText")));

            var recentColorsPath = Path.Combine(
                Path.GetTempPath(),
                $"freex-localization-colors-{Guid.NewGuid():N}.json");
            try
            {
                var picker = new FormatCellsColorPicker(
                    new RecentColorsStore(recentColorsPath),
                    (_, initial) => Task.FromResult<CellColor?>(initial),
                    UiText.Get("ColorPicker_NoColor"),
                    includeClear: false,
                    UiText.Get("ColorPicker_SelectColor"));
                picker.ConfigureCompactPickButton();
                AssertPseudoLocalized(picker.Content);
            }
            finally
            {
                File.Delete(recentColorsPath);
            }

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }), CancellationToken.None);
    }

    [Fact]
    public void PresentationDialogDescriptors_ResolvePseudoLocalizedText()
    {
        WithPseudoCulture(() =>
        {
            AssertPseudoLocalized(FindReplaceDialogSchema.Resolve(
                FindReplaceDialogText.ResultsAutomationName,
                UiText.Get,
                UiText.Format));

            AssertPseudoLocalized(GoalSeekStatusDialogPlanner.DescribeStatus(
                    converged: true,
                    targetValue: 10,
                    actualResult: 10,
                    foundValue: 5,
                    GoalSeekPresentationProfile.Avalonia)
                .Resolve(UiText.Get, UiText.Format));

            AssertPseudoLocalized(GoalSeekStatusDialogPlanner.DescribeValidationError(
                    GoalSeekRequestParseResult.Invalid(GoalSeekRequestParseError.SetCellRequired),
                    GoalSeekPresentationProfile.Avalonia)
                .Message
                .Resolve(UiText.Get, UiText.Format));
        });
    }

    [Fact]
    public void ThemeAndCellStyleDescriptors_ResolvePseudoLocalizedText()
    {
        WithPseudoCulture(() =>
        {
            WorkbookThemeCatalog.ThemePresets
                .Concat<object>(WorkbookThemeCatalog.ColorPresets)
                .Concat(WorkbookThemeCatalog.FontPresets)
                .Concat(WorkbookThemeCatalog.EffectPresets)
                .Select(option => option switch
                {
                    WorkbookThemePresetOption value => value.LabelResourceKey,
                    WorkbookThemeColorPresetOption value => value.LabelResourceKey,
                    WorkbookThemeFontPresetOption value => value.LabelResourceKey,
                    WorkbookThemeEffectPresetOption value => value.LabelResourceKey,
                    _ => throw new InvalidOperationException(),
                })
                .Should().OnlyContain(key => IsPseudoLocalized(UiText.Get(key)));

            Enum.GetValues<CellStylePreset>()
                .Select(CellStyleDiffPlanner.GetCellStylePresetLabelResourceKey)
                .Should().OnlyContain(key => IsPseudoLocalized(UiText.Get(key)));
        });
    }

    [Fact]
    public void PresentationPlanners_DoNotOwnAvaloniaSpecificEnglishFallbacks()
    {
        var findReplace = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Dialogs", "FindReplaceDialogSchema.cs");
        var goalSeek = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Presentation", "Dialogs", "GoalSeekStatusDialogPlanner.cs");

        findReplace.Should().Contain(
            "FindReplaceDialogText.ResultsAutomationName => \"FindReplace_ResultsAutomationName\"");
        findReplace.Should().NotContain("LocalizedTextDescriptor.Literal(\"Find all results\")");
        goalSeek.Should().NotMatchRegex("LocalizedTextDescriptor\\.Literal\\(\\s*\\$?\\\"");
        goalSeek.Should().Contain("GoalSeekStatus_SuccessSummary");
        goalSeek.Should().Contain("GoalSeek_RequestInvalid");
    }

    private static T Field<T>(MainWindow window, string name)
        where T : class =>
        (T)(typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
            ?? throw new InvalidOperationException($"Missing MainWindow field '{name}'."));

    private static IReadOnlyList<(string Name, string Source)> AuditedRendererSources()
    {
        var sources = new List<(string Name, string Source)>();
        foreach (var projectName in new[] { "FreeX.App.Avalonia", "FreeX.App.Host" })
        {
            var sourceDirectory = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", projectName);
            foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
                         .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
            {
                var relativePath = Path.GetRelativePath(Path.GetDirectoryName(sourceDirectory)!, path)
                    .Replace('\\', '/');
                sources.Add((relativePath, File.ReadAllText(path)));
            }
        }

        return sources;
    }

    private static void AssertPseudoLocalized(object? value)
    {
        value.Should().BeOfType<string>();
        ((string)value!).Should().StartWith("[[").And.EndWith("]]");
    }

    private static bool IsPseudoLocalized(string value) =>
        value.StartsWith("[[", StringComparison.Ordinal) &&
        value.EndsWith("]]", StringComparison.Ordinal);

    private static void WithPseudoCulture(Action action)
    {
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            var pseudo = CultureInfo.GetCultureInfo(LocalizedTextCatalog.PseudoLocalizationCultureName);
            CultureInfo.CurrentUICulture = pseudo;
            CultureInfo.CurrentCulture = pseudo;
            action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
