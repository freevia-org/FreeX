using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class SortDialogPlannerTests
{
    [Fact]
    public void VisualMetrics_PreserveSharedWpfAuthorityGeometry()
    {
        SortDialogVisualMetrics.WindowWidth.Should().Be(380);
        SortDialogVisualMetrics.RootInset.Should().Be(14);
        SortDialogVisualMetrics.PromptBottomMargin.Should().Be(10);
        SortDialogVisualMetrics.PrimaryHeadingBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.OptionalKeyTopMargin.Should().Be(8);
        SortDialogVisualMetrics.OptionalKeyBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.TypeMinimumWidth.Should().Be(120);
        SortDialogVisualMetrics.TypeControlBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.KeyRowBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.TypeLabelTrailingMargin.Should().Be(8);
        SortDialogVisualMetrics.RadioLeftMargin.Should().Be(4);
        SortDialogVisualMetrics.AscendingRightMargin.Should().Be(8);
        SortDialogVisualMetrics.RadioBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.CaseSensitiveTopMargin.Should().Be(10);
        SortDialogVisualMetrics.CaseSensitiveBottomMargin.Should().Be(4);
        SortDialogVisualMetrics.ActionButtonWidth.Should().Be(72);
        SortDialogVisualMetrics.ActionRowTopMargin.Should().Be(14);
        SortDialogVisualMetrics.ActionSpacing.Should().Be(8);
    }

    [Fact]
    public void Renderers_ConsumeSharedVisualMetricsAndAvaloniaUsesCompactChromeAndInitialFocus()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "SortDialog.cs"));
        var avaloniaSource = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "ParagraphCommandDialogs.cs"));
        var avaloniaStart = avaloniaSource.IndexOf("public sealed class SortDialog", StringComparison.Ordinal);
        avaloniaStart.Should().BeGreaterThanOrEqualTo(0);
        var avalonia = avaloniaSource[avaloniaStart..];

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SortDialogVisualMetrics.WindowWidth");
            source.Should().Contain("SortDialogVisualMetrics.RootInset");
            source.Should().Contain("SortDialogVisualMetrics.PromptBottomMargin");
            source.Should().Contain("SortDialogVisualMetrics.PrimaryHeadingBottomMargin");
            source.Should().Contain("SortDialogVisualMetrics.OptionalKeyTopMargin");
            source.Should().Contain("SortDialogVisualMetrics.TypeMinimumWidth");
            source.Should().Contain("SortDialogVisualMetrics.TypeControlBottomMargin");
            source.Should().Contain("SortDialogVisualMetrics.KeyRowBottomMargin");
            source.Should().Contain("SortDialogVisualMetrics.TypeLabelTrailingMargin");
            source.Should().Contain("SortDialogVisualMetrics.RadioLeftMargin");
            source.Should().Contain("SortDialogVisualMetrics.AscendingRightMargin");
            source.Should().Contain("SortDialogVisualMetrics.RadioBottomMargin");
            source.Should().Contain("SortDialogVisualMetrics.CaseSensitiveTopMargin");
            source.Should().Contain("SortDialogVisualMetrics.ActionButtonWidth");
            source.Should().Contain("SortDialogVisualMetrics.ActionRowTopMargin");
            source.Should().NotContain("Width = 360");
            source.Should().NotContain("new Thickness(16)");
            source.Should().NotContain("MinWidth = 120");
            source.Should().NotContain("MinWidth = 76");
        }

        avalonia.Should().Contain("AvaloniaCompactDialogChrome.ApplyComboBox(combo, DialogChromeStyle)");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.ApplyRadioButton(radio, DialogChromeStyle)");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DialogChromeStyle)");
        avalonia.Should().Contain("AvaloniaCompactDialogChrome.CreateActionButton(");
        avalonia.Should().Contain("Opened += (_, _) => _type1.Focus()");
    }

    [Fact]
    public void TypeChoices_ExposeWordSortTypesInDisplayOrder()
    {
        SortDialogPlanner.TypeChoices.Select(choice => choice.Label)
            .Should().Equal("Text", "Number", "Date");
        SortDialogPlanner.TypeChoices.Select(choice => choice.Value)
            .Should().Equal(SortKind.Text, SortKind.Number, SortKind.Date);
    }

    [Fact]
    public void PromptLabel_DistinguishesParagraphAndTableSortSurfaces()
    {
        SortDialogPlanner.PromptLabel(forTable: false)
            .Should().Be("Sort the selected paragraphs:");
        SortDialogPlanner.PromptLabel(forTable: true)
            .Should().Be("Sort the table rows by the current column:");
    }

    [Fact]
    public void BuildResult_MapsPrimaryAndOptionalKeysWithClampedTypeIndexes()
    {
        var result = SortDialogPlanner.BuildResult(
            key1TypeIndex: 1,
            key1Ascending: false,
            useKey2: true,
            key2TypeIndex: 2,
            key2Ascending: true,
            useKey3: true,
            key3TypeIndex: 99,
            key3Ascending: false,
            caseSensitive: true,
            hasHeaderRow: true);

        result.Kind.Should().Be(SortKind.Number);
        result.Ascending.Should().BeFalse();
        result.Key2.Should().Be(new SortDialogKey(SortKind.Date, Ascending: true));
        result.Key3.Should().Be(new SortDialogKey(SortKind.Date, Ascending: false));
        result.CaseSensitive.Should().BeTrue();
        result.HasHeaderRow.Should().BeTrue();
    }

    [Fact]
    public void BuildResult_OmitsDisabledSecondaryKeys()
    {
        var result = SortDialogPlanner.BuildResult(
            key1TypeIndex: -10,
            key1Ascending: true,
            useKey2: false,
            key2TypeIndex: 1,
            key2Ascending: false,
            useKey3: false,
            key3TypeIndex: 2,
            key3Ascending: false,
            caseSensitive: false,
            hasHeaderRow: false);

        result.Key1.Should().Be(new SortDialogKey(SortKind.Text, Ascending: true));
        result.Key2.Should().BeNull();
        result.Key3.Should().BeNull();
    }
}
