using System.Threading;

using Avalonia.Headless;

using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class PivotDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static readonly string[] DialogIds =
    [
        "dialog.PivotCalculatedFieldDialog",
        "dialog.PivotCalculatedItemDialog",
        "dialog.PivotChartOptionsDialog",
        "dialog.PivotChartTypeDialog",
        "dialog.PivotFieldFilterDialog",
        "dialog.PivotFieldGroupingDialog",
        "dialog.PivotLabelFilterDialog",
        "dialog.PivotSortOptionsDialog",
        "dialog.PivotStyleGalleryDialog",
        "dialog.PivotTableDialog",
        "dialog.PivotTableOptionsDialog",
        "dialog.PivotValueFieldSettingsDialog",
        "dialog.PivotValueFilterDialog",
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedInitialFocus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dialog.PivotCalculatedField"] = "passed:TextBox#PivotCalcFieldNameBox",
            ["dialog.PivotCalculatedItem"] = "passed:TextBox#PivotCalcItemNameBox",
            ["dialog.PivotChartOptions"] = "passed:CheckBox#PivotChartOptionsShowFieldButtons",
            ["dialog.PivotChartType"] = "passed:ListBox#ChangeChartTypeSubtypeGallery",
            ["dialog.PivotFieldFilter"] = "passed:TextBox#PivotItemFilterSearchBox",
            ["dialog.PivotFieldGrouping"] = "passed:ComboBox#PivotGroupFieldBox",
            ["dialog.PivotLabelFilter"] = "passed:ComboBox#PivotLabelFilterKindBox",
            ["dialog.PivotSortOptions"] = "passed:RadioButton#PivotSortOptionsLabelAscending",
            ["dialog.PivotStyleGallery"] = "passed:ListBox#PivotStyleGalleryList",
            ["dialog.PivotTable"] = "passed:TextBox#InsertPivotTableSourceRangeBox",
            ["dialog.PivotTableOptions"] = "passed:ComboBox#PivotOptionsReportLayoutBox",
            ["dialog.PivotValueFieldSettings"] = "passed:TextBox#PivotValueFieldSettingsNameBox",
            ["dialog.PivotValueFilter"] = "passed:ComboBox#PivotValueFilterKindBox",
        };

    private readonly ITestOutputHelper _output;

    public PivotDialogLifecycleRegressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task PivotDialogs_MatchWpfInitialFocusAndCompleteBothKeyboardCycles()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "freex-pivot-dialog-lifecycle-" + Guid.NewGuid().ToString("N"));

        try
        {
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = DialogIds.ToHashSet(StringComparer.Ordinal);

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var results = window.BuildDialogInteractionContractResults(selectedIds);
                    foreach (var result in results)
                        _output.WriteLine($"{result.Id}: {result.Status} | {result.Evidence}");

                    results.Should().HaveCount(DialogIds.Length);
                    results.Select(result => result.Id).Should().BeEquivalentTo(DialogIds);
                    results.Should().OnlyContain(
                        result => result.Status == "passed",
                        string.Join(Environment.NewLine, results.Select(result =>
                            $"{result.Id}: {result.Status} | {result.Evidence}")));

                    foreach (var (surfaceId, expectedFocus) in ExpectedInitialFocus)
                    {
                        var contract = window.DialogInteractionContracts[surfaceId];
                        contract.InitialFocus.Should().Be(expectedFocus, surfaceId);
                        contract.TabForward.Should().StartWith("passed:full-cycle:", surfaceId);
                        contract.TabBackward.Should().StartWith("passed:full-cycle:", surfaceId);
                        contract.EscapeCancel.Should().Be("passed:closed-by-escape", surfaceId);
                    }
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                    {
                        if (owned.IsVisible)
                            owned.Close();
                    }

                    if (window.IsVisible)
                        window.Close();
                }
            }, CancellationToken.None);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDirectory))
                    Directory.Delete(outputDirectory, recursive: true);
            }
            catch
            {
                // Test cleanup must not hide the dialog lifecycle regression.
            }
        }
    }
}
