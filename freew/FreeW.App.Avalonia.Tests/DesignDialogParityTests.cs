using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class DesignDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public void AvaloniaDesignDialogs_KeepDialogPolicyInPresentationPlanners()
    {
        var source = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogParity.cs"));
        source.Should().Contain("CustomizeThemeColorsDialogPlanner.BuildInitialState(current)");
        source.Should().Contain("CustomizeThemeColorsDialogPlanner.TryBuildResult(");
        source.Should().Contain("CustomizeThemeFontsDialogPlanner.CreateSession(current)");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().Contain("acceptance.FocusField == CustomizeThemeFontsDialogField.BodyFont");
        source.Should().NotContain("CustomizeThemeFontsDialogPlanner.BuildInitialState(");
        source.Should().NotContain("CustomizeThemeFontsDialogPlanner.TryBuildResult(");
        source.Should().Contain("PageColorDialogPlanner.TryBuildResult(");
        source.Should().Contain("SetAsDefaultConfirmationPlanner.BuildState()");

        var spacingSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "PageLayoutDialogs.cs"));
        spacingSource.Should().Contain("CustomParagraphSpacingDialogSession");
        spacingSource.Should().Contain("_session.PlanAcceptance(");
        spacingSource.Should().NotContain("CustomParagraphSpacingDialogPlanner.BuildInitialState(");
        spacingSource.Should().NotContain("CustomParagraphSpacingDialogPlanner.TryBuildResult(");

        var borderSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "DesignDialogs.cs"));
        borderSource.Should().Contain("BordersAndShadingDialogPlanner.TryBuildResult(");
        borderSource.Should().Contain("BordersAndShadingDialogPlanner.ArtBorders");

        var styleSource = File.ReadAllText(RepositoryFile("freew", "FreeW.App.Avalonia", "StyleDialog.cs"));
        styleSource.Should().Contain("ControlHeight = StyleDialogMetrics.ComboBoxHeight");
        styleSource.Should().Contain("ButtonHeight = StyleDialogMetrics.ButtonHeight");
        styleSource.Should().Contain("other dialogs retain their own shared density contracts");
    }

    [Fact]
    public void StyleDialog_UsesWpfCompactControlHeight()
    {
        StyleDialog.ControlHeightForTests.Should().Be(22);
        StyleDialog.ButtonHeightForTests.Should().Be(20);
        StyleDialogMetrics.ComboBoxHeight.Should().Be(22);
        StyleDialog.CheckBoxHeightForTests.Should().Be(15);
        StyleDialogMetrics.CheckBoxHeight.Should().Be(15);
        StyleDialogMetrics.ButtonHeight.Should().Be(20);
    }

    [Fact]
    public async Task ThemeColorsDialog_AcceptsPresetAndCustomName()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CustomizeThemeColorsDialog(DocumentTheme.Default);
            dialog.AcceptForTests().Should().BeTrue();
            dialog.Result.Should().NotBeNull();
            dialog.Result!.Name.Should().Be("Office");
            dialog.Result.ColorScheme.Should().Be(DocumentTheme.Default.ColorScheme);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThemeColorsDialog_UsesWpfGeometryAndActionSemantics()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CustomizeThemeColorsDialog(DocumentTheme.Default);

            dialog.Width.Should().Be(CustomizeThemeColorsDialog.WpfWidthForTests);
            var grids = dialog.GetLogicalDescendants().OfType<Grid>().ToArray();
            grids.Should().Contain(grid => grid.RowDefinitions.Count == CustomizeThemeColorsDialogPlanner.Slots.Count);
            grids.Should().Contain(grid => grid.RowDefinitions.Count == 1);
            grids.Where(grid => grid.RowDefinitions.Count is 12 or 1)
                .SelectMany(grid => grid.RowDefinitions)
                .Should().OnlyContain(row => row.Height.Value == CustomizeThemeColorsDialog.WpfColorRowHeightForTests);

            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
            buttons.Should().OnlyContain(button => button.MinWidth == CustomizeThemeColorsDialog.WpfButtonWidthForTests);
            buttons.Single(button => button.IsDefault).Content.Should().Be("OK");
            buttons.Single(button => button.IsCancel).Content.Should().Be("Cancel");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThemeFontsAndSpacingDialogs_AcceptWpfSeededDefaults()
    {
        await Session.Dispatch(() =>
        {
            var fonts = new CustomizeThemeFontsDialog(DocumentFontSet.Default);
            fonts.AcceptForTests().Should().BeTrue();
            fonts.Result.Should().Be(new DocumentFontSet("Custom", "Calibri", "Calibri"));

            var spacing = new CustomParagraphSpacingDialog(DocumentParagraphSpacingSet.Default);
            spacing.AcceptForTests().Should().BeTrue();
            spacing.Result.Should().Be(new DocumentParagraphSpacingSet("Custom", 0, 6, 1.15));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThemeFontsDialog_UsesWpfGeometryAndActionSemantics()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CustomizeThemeFontsDialog(DocumentFontSet.Default);

            dialog.Width.Should().Be(CustomizeThemeFontsDialogPlanner.DialogWidth);
            var grid = dialog.GetLogicalDescendants().OfType<Grid>()
                .Single(candidate => candidate.RowDefinitions.Count == 4);
            grid.Margin.Should().Be(new Thickness(0, 0, 0, CustomizeThemeFontsDialogPlanner.DialogMargin));
            grid.ColumnDefinitions[0].Width.Value.Should().Be(CustomizeThemeFontsDialogPlanner.LabelColumnWidth);

            var fields = grid.Children.OfType<Control>()
                .Where(control => control is ComboBox or TextBox)
                .ToArray();
            fields.Should().HaveCount(3);
            fields.Should().OnlyContain(field => field.MinWidth == CustomizeThemeFontsDialogPlanner.FieldMinWidth);
            fields.Should().OnlyContain(field => field.Margin == new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, 0, CustomizeThemeFontsDialogPlanner.RowMargin));
            grid.Children.OfType<TextBlock>()
                .Where(label => label.Text is "Heading font:" or "Body font:" or "Name:")
                .Should().OnlyContain(label => label.Margin == new Thickness(0, CustomizeThemeFontsDialogPlanner.RowMargin, CustomizeThemeFontsDialogPlanner.LabelRightMargin, CustomizeThemeFontsDialogPlanner.RowMargin));

            grid.Children.OfType<Border>().Should().ContainSingle(separator =>
                separator.Height == CustomizeThemeFontsDialogPlanner.SeparatorHeight
                && separator.Background == AvaloniaCompactDialogChrome.DialogSeparatorBrush
                && separator.Margin == new Thickness(0, CustomizeThemeFontsDialogPlanner.SeparatorTopMargin, 0, CustomizeThemeFontsDialogPlanner.SeparatorBottomMargin));

            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            buttons.Select(button => button.Content?.ToString()).Should().Equal("OK", "Cancel");
            buttons.Should().OnlyContain(button => button.MinWidth == CustomizeThemeFontsDialogPlanner.ActionButtonWidth);
            buttons.Single(button => button.IsDefault).Content.Should().Be("OK");
            buttons.Single(button => button.IsCancel).Content.Should().Be("Cancel");
            dialog.GetLogicalDescendants().OfType<StackPanel>()
                .Single(row => row.Children.OfType<Button>().Count() == 2)
                .Margin.Should().Be(new Thickness(0, CustomizeThemeFontsDialogPlanner.ActionRowTopMargin, 0, 0));
            dialog.GetLogicalDescendants().OfType<TextBlock>().Should().NotContain(text => text.IsVisible && text.Text == "Enter a heading font name.");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ThemeFontsDialog_ValidationUsesInlineAvaloniaStatusAndFocusesInvalidField()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new CustomizeThemeFontsDialog(DocumentFontSet.Default);
            var heading = dialog.GetLogicalDescendants().OfType<ComboBox>().First();
            heading.Text = string.Empty;

            dialog.AcceptForTests().Should().BeFalse();
            dialog.Result.Should().BeNull();
            dialog.GetLogicalDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Enter a heading font name.")
                .IsVisible.Should().BeTrue();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PageColorDialog_AcceptsCustomAndNoColorResults()
    {
        await Session.Dispatch(() =>
        {
            var custom = new PageColorDialog(null);
            custom.SelectCustomColorForTests(" d9ead3 ");
            custom.AcceptForTests().Should().BeTrue();
            custom.Result.Should().Be("#D9EAD3");

            var none = new PageColorDialog(null);
            none.AcceptForTests().Should().BeTrue();
            none.Result.Should().BeNull();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EffectsAndDefaultConfirmationDialogs_ReturnExplicitResults()
    {
        await Session.Dispatch(() =>
        {
            var effects = new ThemeEffectsDialog("Moderate");
            effects.AcceptForTests().Should().BeTrue();
            effects.Result!.Name.Should().Be("Moderate");

            var styleSets = new StyleSetDialog("Elegant");
            styleSets.AcceptForTests().Should().BeTrue();
            styleSets.Result!.Name.Should().Be("Elegant");

            var confirmation = new SetAsDefaultConfirmationDialog();
            confirmation.Confirmed.Should().BeFalse();
        }, CancellationToken.None);
    }

    private static string RepositoryFile(params string[] parts)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            var candidate = Path.Combine(new[] { directory }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
