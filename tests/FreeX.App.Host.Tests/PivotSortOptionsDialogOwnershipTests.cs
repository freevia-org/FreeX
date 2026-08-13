using System.Windows.Automation;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class PivotSortOptionsDialogOwnershipTests
{
    [Fact]
    public void Dialog_ProjectsPlannerOptionsAndInitialStateIntoNativeControls()
    {
        StaTestRunner.Run(() =>
        {
            var current = new PivotSortModel(
                PivotSortTarget.Value,
                PivotSortDirection.Descending,
                DataFieldIndex: 1,
                FieldIndex: 3);
            var dialog = new PivotSortOptionsDialog(
                "Revenue",
                sourceFieldIndex: 3,
                [new PivotDataFieldModel(0, "Sales", "sum"), new PivotDataFieldModel(1, "Margin", "sum")],
                current);

            try
            {
                dialog.Title.Should().Be(UiText.Format("PivotSort_Title", "Revenue"));

                var buttons = WpfTestTree.FindLogicalDescendants<RadioButton>(dialog).ToArray();
                buttons.Select(button => button.Content).Should().Equal(
                    PivotSortPlanner.Options.Select(option => option.Text.Resolve(UiText.Get)));
                buttons.Single(button => AutomationProperties.GetAutomationId(button) == "PivotSortOptionsValueDescending")
                    .IsChecked.Should().BeTrue();

                var valueField = WpfTestTree.FindLogicalDescendants<ComboBox>(dialog)
                    .Single(box => AutomationProperties.GetAutomationId(box) == "PivotSortOptionsValueFieldBox");
                valueField.SelectedIndex.Should().Be(1);
                valueField.IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Dialog_ResolvesPlannerOptionsThroughPseudoLocalizationAtConstruction()
    {
        StaTestRunner.Run(() =>
        {
            using var cultureScope = TestCultureScope.CurrentCultureAndUICulture("qps-ploc");
            var dialog = new PivotSortOptionsDialog("Revenue", sourceFieldIndex: 3, []);

            try
            {
                var buttons = WpfTestTree.FindLogicalDescendants<RadioButton>(dialog).ToArray();
                buttons.Select(button => button.Content).Should().Equal(
                    PivotSortPlanner.Options.Select(option => UiText.Get(option.Text.ResourceKey)));
                buttons.Select(button => button.Content).Should().NotContain(
                    PivotSortPlanner.Options.Select(option => option.Text.FallbackText));
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void Dialog_SourceDelegatesAllSortPolicyToPlanner()
    {
        var source = DialogSourceTestSupport.ReadHostSources("PivotSortOptionsDialog.cs");

        source.Should().Contain("foreach (var option in PivotSortPlanner.Options)");
        source.Should().Contain("AutomationProperties.SetAutomationId(button, option.AutomationId)");
        source.Should().Contain("PivotSortPlanner.InitialMode(currentSort, _sourceFieldIndex)");
        source.Should().Contain("PivotSortPlanner.InitialValueFieldIndex(");
        source.Should().Contain("PivotSortPlanner.ValueFieldEnabled(CurrentMode(), _dataFields.Count)");
        source.Should().Contain("PivotSortPlanner.TryValidate(");
        source.Should().Contain("PivotSortPlanner.CreateResult(CurrentMode(), _sourceFieldIndex, _valueFieldBox.SelectedIndex)");
        source.Should().NotContain("new PivotSortModel(");
        source.Should().NotContain("Add a PivotTable value field before sorting by values.");
        source.Should().NotContain("Ascending (A to Z) by labels");
        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("Keyboard.Focus(_valueFieldBox);");
        source.Should().Contain("Keyboard.Focus(initialButton);");
    }

    [Fact]
    public void AvaloniaAndWpfBothProjectTheSharedOptionCatalogAndValidationDescriptor()
    {
        var avalonia = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.PivotFieldSettings.cs");
        var wpf = DialogSourceTestSupport.ReadHostSources("PivotSortOptionsDialog.cs");

        avalonia.Should().Contain("PivotSortPlanner.GetOption(PivotSortOptionMode.LabelAscending).Text.Resolve(UiText.Get)");
        avalonia.Should().Contain("PivotSortPlanner.ValueSortRequiresValueField).Resolve(UiText.Get)");
        wpf.Should().Contain("option.Text.Resolve(UiText.Get)");
        wpf.Should().Contain("PivotSortPlanner.ValueSortRequiresValueField).Resolve(UiText.Get)");
    }
}
