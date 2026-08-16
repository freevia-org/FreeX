using System.Threading;

using Avalonia.Headless;

using Xunit.Abstractions;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class DialogTabCycleResidualTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    private static readonly string[] DialogIds =
    [
        "dialog.PageSetupDialog",
        "dialog.LegalNoticesDialog",
        "dialog.PivotFieldFilterDialog",
        "dialog.PivotValueFieldSettingsDialog",
        "dialog.SymbolPickerDialog",
        "dialog.DataValidationDialog",
        "dialog.FormatCellsDialog",
        "dialog.FindReplaceDialog",
    ];

    private static readonly IReadOnlyDictionary<string, string> ExpectedInitialFocus =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dialog.PageSetup"] = "passed:ComboBox#PageSetupOrientationBox",
            ["dialog.LegalNotices"] = "passed:TextBox#LegalNoticesProjectLicenseText",
            ["dialog.PivotFieldFilter"] = "passed:TextBox#PivotItemFilterSearchBox",
            ["dialog.PivotValueFieldSettings"] = "passed:TextBox#PivotValueFieldSettingsNameBox",
            ["dialog.DataValidation"] = "passed:ComboBox#DataValidationTypeBox",
            ["dialog.FormatCells"] = "passed:ListBox#FormatCellsNumberCategoryList",
            ["dialog.FindReplace"] = "passed:TextBox#FindReplaceFindBox",
        };

    private readonly ITestOutputHelper _output;

    public DialogTabCycleResidualTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task AssignedDialogs_CycleFocusAndHonorDefaultCancelContracts()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-dialog-tab-cycle-"))
        {
            var outputDirectory = temporaryDirectory.Path;
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

                    var contracts = window.BuildDialogInteractionContractResults(selectedIds);
                    foreach (var contract in contracts)
                        _output.WriteLine($"{contract.Id}: {contract.Status} | {contract.Evidence}");

                    contracts.Should().HaveCount(DialogIds.Length);
                    contracts.Select(contract => contract.Id).Should().BeEquivalentTo(DialogIds);
                    contracts.Should().OnlyContain(
                        contract => contract.Status == "passed",
                        string.Join(
                            Environment.NewLine,
                            contracts.Select(contract => $"{contract.Id}: {contract.Evidence}")));

                    foreach (var (surfaceId, expectedFocus) in ExpectedInitialFocus)
                    {
                        window.DialogInteractionContracts[surfaceId].InitialFocus
                            .Should().Be(expectedFocus);
                    }
                }
                finally
                {
                    foreach (var owned in window.OwnedWindows.ToArray())
                    {
                        if (owned.IsVisible)
                            owned.Close();
                    }

                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    if (window.IsVisible)
                        window.Close();
                }
                return true;
            }, CancellationToken.None);
        }
    }
}
