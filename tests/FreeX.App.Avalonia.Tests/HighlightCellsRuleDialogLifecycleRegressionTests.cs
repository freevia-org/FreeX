using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;

namespace FreeX.App.Avalonia.Tests;

[Collection(AvaloniaHeadlessCollectionOrderer.ParityCaptureCollectionName)]
public sealed class HighlightCellsRuleDialogLifecycleRegressionTests
{
    private static readonly HeadlessUnitTestSession Session = AvaloniaParityCaptureSession.Session;

    [Fact]
    public async Task HighlightCellsRuleDialog_UsesWpfConditionSelectorAsInitialFocusAndTabOrigin()
    {
        using (var temporaryDirectory = new TestTemporaryDirectory("freex-highlight-cells-rule-focus-"))
        {
            var outputDirectory = temporaryDirectory.Path;
            await Session.Dispatch(async () =>
            {
                var window = new MainWindow([]);
                try
                {
                    window.Show();
                    var selectedIds = new HashSet<string>(StringComparer.Ordinal)
                    {
                        "dialog.HighlightCellsRuleDialog",
                    };

                    await window.CaptureParitySurfacesAsync(
                        outputDirectory,
                        interactionOnly: true,
                        interactionDialogCatalogIds: selectedIds);

                    var contract = window.DialogInteractionContracts["dialog.HighlightCellsRuleDialog"];
                    contract.InitialFocus.Should().Be("passed:ComboBox#ConditionalFormatRuleTypeBox");
                    contract.TabForward.Should().StartWith("passed:full-cycle:");
                    contract.TabBackward.Should().StartWith("passed:full-cycle:");
                    contract.EscapeCancel.Should().Be("passed:closed-by-escape");
                    window.BuildDialogInteractionContractResults(selectedIds)
                        .Should().ContainSingle(
                            result => result.Status == "passed",
                            "Highlight Cells Rule contract should pass: {0}; {1}; {2}; {3}",
                            contract.InitialFocus,
                            contract.TabForward,
                            contract.TabBackward,
                            contract.EscapeCancel);
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
