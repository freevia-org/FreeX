using System.Windows.Data;
using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void AppliesToColumn_UsesEditableRangeTextAndPickerButton()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("typeof(TextBox)");
        source.Should().Contain("typeof(Button)");
        source.Should().Contain("new Binding(nameof(ConditionalFormat.AppliesTo))");
        source.Should().Contain("new AppliesToRangeConverter(_sheet.Id)");
        source.Should().Contain("ToolTipProperty, UiText.Get(\"ManageConditionalFormats_CollapseDialogAndSelectAppliesToRange\")");
        source.Should().Contain("AutomationProperties.NameProperty, UiText.Get(\"ManageConditionalFormats_SelectAppliesToRange\")");
        source.Should().Contain("AutomationProperties.HelpTextProperty, UiText.Get(\"ManageConditionalFormats_SelectAppliesToRangeHelpText\")");
        source.Should().Contain("RangePickerButton_Click");
        source.Should().Contain(".CreateAppliesToRangeSelectionRequest(rule.Id, rangeBox.Text)");
        source.Should().Contain("_requestAppliesToRangeSelection?.Invoke(AppliesToRangeSelectionRequest)");
        source.Should().Contain("RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1");
        source.Should().Contain("SetBinding(UIElement.IsEnabledProperty, new Binding(\"IsSelected\")");
    }

    [Fact]
    public void CreateAppliesToRangeSelectionRequest_UsesExcelCollapseIntent()
    {
        var ruleId = Guid.NewGuid();

        ManageConditionalFormatsPlanner.CreateAppliesToRangeSelectionRequest(ruleId, " $A$1:$C$5 ")
            .Should()
            .Be(new ConditionalFormatAppliesToRangeSelectionRequest(ruleId, "$A$1:$C$5", CollapseDialog: true));
    }

    [Fact]
    public void TryParseAppliesToText_AcceptsExcelAbsoluteRangeText()
    {
        var sheetId = SheetId.New();
        var fallback = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

        ManageConditionalFormatsDialog.TryParseAppliesToText("$B$2:$D$5", sheetId, fallback)
            .Should().Be(new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 5, 4)));
    }

    [Fact]
    public void AppliesToRangeConverter_InvalidTextRejectsEditInsteadOfFallingBackToA1()
    {
        var sheetId = SheetId.New();
        var converter = new AppliesToRangeConverter(sheetId);

        converter.ConvertBack("not a range", typeof(GridRange), parameter: null!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeSameAs(Binding.DoNothing);
    }

    [Fact]
    public void StopIfTrueText_ShowsEnabledRules()
    {
        var rule = new ConditionalFormat { StopIfTrue = true };

        ManageConditionalFormatsDialog.StopIfTrueText(rule).Should().Be("Yes");
    }

    [Fact]
    public void StopIfTrueColumn_UsesEditableTwoWayCheckbox()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("typeof(CheckBox)");
        source.Should().Contain("nameof(ConditionalFormat.StopIfTrue)");
        source.Should().Contain("BindingMode.TwoWay");
        source.Should().Contain("UpdateSourceTrigger.PropertyChanged");
    }

    [Fact]
    public void FormatPreviewColumn_ShowsExcelStyleSampleText()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("Header = UiText.Get(\"ManageConditionalFormats_FormatColumn\")");
        source.Should().Contain("typeof(Border)");
        source.Should().Contain("typeof(TextBlock)");
        source.Should().Contain("UiText.Get(ManageConditionalFormatsPlanner.FormatPreviewSampleKey)");
        source.Should().Contain("new PreviewForegroundBrushConverter()");
        source.Should().Contain("new PreviewFontWeightConverter()");
        source.Should().Contain("new PreviewFontStyleConverter()");
        source.Should().Contain("new PreviewTextDecorationsConverter()");
    }

    [Fact]
    public void PreviewForegroundBrush_UsesConditionalFormatFontColor()
    {
        var sheetId = SheetId.New();
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            FormatIfTrue = new CellStyle { FontColor = new CellColor(12, 34, 56) }
        };

        var brush = ManageConditionalFormatsDialog.PreviewForegroundBrush(rule)
            .Should()
            .BeOfType<SolidColorBrush>()
            .Subject;
        brush.Color.Should().Be(Color.FromRgb(12, 34, 56));
    }
}
