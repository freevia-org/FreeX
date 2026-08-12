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

    [Fact]
    public void ActiveAvaloniaRenderer_DoesNotAssignResourceTextFromRawLiterals()
    {
        var resourceValues = UiText.GetNeutralResourceKeys()
            .Select(UiText.GetNeutral)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var sourceDirectory = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory(
            "src",
            "FreeX.App.Avalonia");
        var sources = AuditedSources(sourceDirectory);
        var failures = new List<string>();

        foreach (var (name, source) in sources)
        {
            foreach (Match match in RawSemanticAssignment.Matches(source))
            {
                var literal = Regex.Unescape(match.Groups["text"].Value);
                if (!resourceValues.Contains(literal))
                    continue;

                var line = source.AsSpan(0, match.Index).Count('\n') + 1;
                failures.Add($"{name}:{line}: {literal}");
            }
        }

        failures.Should().BeEmpty(
            "semantic renderer text that already exists in the localization catalog must be resolved through UiText");
    }

    [Fact]
    public void ActiveAvaloniaRenderer_DoesNotPassScopedSemanticTextAsRawArguments()
    {
        var sourceDirectory = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory(
            "src",
            "FreeX.App.Avalonia");
        var failures = new List<string>();

        foreach (var (name, source) in AuditedSources(sourceDirectory))
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

    private static IReadOnlyList<(string Name, string Source)> AuditedSources(string sourceDirectory)
    {
        string Read(string fileName) => File.ReadAllText(Path.Combine(sourceDirectory, fileName));

        var sources = new List<(string Name, string Source)>();
        foreach (var fileName in new[]
                 {
                     "FormatCellsColorPicker.cs",
                     "MainWindow.AutoFilter.cs",
                     "MainWindow.CellStyles.cs",
                     "MainWindow.Comments.cs",
                     "MainWindow.ConditionalFormat.cs",
                     "MainWindow.InsertDeleteCells.cs",
                     "MainWindow.InsertFunction.cs",
                     "MainWindow.LiveBackstage.cs",
                     "MainWindow.MoreColors.cs",
                     "MainWindow.RowColumnVisibility.cs",
                     "MainWindow.StatusBar.cs",
                     "MainWindow.Symbol.cs",
                     "MainWindow.Themes.cs",
                 })
        {
            sources.Add((fileName, Read(fileName)));
        }

        var mainWindow = Read("MainWindow.cs");
        foreach (var signature in new[]
                 {
                     "private Control BuildToolbar()",
                     "private Control BuildStatusBar()",
                     "private async Task ShowZoomDialogAsync()",
                     "private async Task<string?> ShowRenameSheetDialogAsync",
                     "private async Task<FindDialogResult?> ShowFindInputDialogAsync",
                     "private async Task<ReplaceDialogResult?> ShowReplaceInputDialogAsync",
                     "private async Task ShowGoToDialogAsync()",
                     "private async Task<GoToSpecialDialogResult?> ShowGoToSpecialInputDialogAsync",
                     "private async Task ShowWorkbookStatisticsDialogAsync()",
                     "private async Task<SortDialogResult?> ShowSortInputDialogAsync(Action",
                     "private async Task ShowGoalSeekDialogAsync()",
                     "private async Task<GoalSeekStatusDialogChoice> ShowGoalSeekStatusDialogAsync",
                     "private async Task<AdvancedFilterPlan?> ShowAdvancedFilterInputDialogAsync()",
                     "private async Task<RemoveDuplicatesPlan?> ShowRemoveDuplicatesInputDialogAsync",
                     "private async Task<ForecastSheetPlan?> ShowForecastSheetInputDialogAsync()",
                     "private async Task<bool> ConfirmNormalizedOverwriteAsync",
                     "private async Task<DirtyWorkbookCloseChoice> ShowDirtyWorkbookCloseDialogAsync",
                     "internal async Task<bool> ShowRecoveryPromptAsync",
                     "private static void UpdateFindReplaceFormatState",
                 })
        {
            sources.Add(($"MainWindow.cs::{signature}", ExtractMethod(mainWindow, signature)));
        }

        return sources;
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"'{signature}' must remain present");
        var openingBrace = source.IndexOf('{', start);
        openingBrace.Should().BeGreaterThan(start);

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
            if (depth == 0)
                return source[start..(index + 1)];
        }

        throw new InvalidOperationException($"Could not find the end of '{signature}'.");
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
