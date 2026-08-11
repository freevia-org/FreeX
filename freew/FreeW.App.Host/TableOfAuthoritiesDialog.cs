using System.Windows;
using System.Windows.Controls;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word's "Table of Authorities" dialog (References &gt; Table of Authorities &gt; Insert Table of
/// Authorities). Mirrors the settings panel that Word shows:
/// <list type="bullet">
/// <item><b>Category</b> — All categories or one specific category.</item>
/// <item><b>Use passim</b> — replace 5+ page references with the word <c>passim</c>.</item>
/// <item><b>Keep original formatting</b> — carry the source run's character formatting.</item>
/// <item><b>Tab leader</b> — the fill character between citation text and page number.</item>
/// </list>
/// Returns a <see cref="Result"/> carrying the chosen <see cref="ToaOptions"/>, or null when cancelled.
/// </summary>
internal sealed class TableOfAuthoritiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    /// <summary>The options the user configured.</summary>
    internal sealed record Result(ToaOptions Options);

    private readonly ComboBox _categoryCombo;
    private readonly CheckBox _passimBox;
    private readonly CheckBox _keepFormattingBox;
    private readonly ComboBox _leaderCombo;
    private Result? _result;

    private TableOfAuthoritiesDialog(Window? owner, ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var metrics = TableOfAuthoritiesDialogPlanner.VisualMetrics;
        Owner = owner;
        Title = TableOfAuthoritiesDialogPlanner.Title;
        Width = metrics.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var state = TableOfAuthoritiesDialogPlanner.BuildInitialState(options);
        var categories = TableOfAuthoritiesDialogPlanner.BuildCategoryChoices();
        var leaders = TableOfAuthoritiesDialogPlanner.BuildTabLeaderChoices();

        _categoryCombo = new ComboBox
        {
            Height = metrics.ComboBoxHeight,
            Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin)
        };
        foreach (var choice in categories)
            _categoryCombo.Items.Add(choice);
        _categoryCombo.SelectedIndex = TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(categories, state.CategoryFilter);

        _passimBox = new CheckBox
        {
            Content = TableOfAuthoritiesDialogPlanner.UsePassimLabel,
            IsChecked = state.UsePassim,
            Margin = new Thickness(0, 0, 0, metrics.PassimBottomMargin)
        };
        _keepFormattingBox = new CheckBox
        {
            Content = TableOfAuthoritiesDialogPlanner.KeepOriginalFormattingLabel,
            IsChecked = state.KeepOriginalFormatting,
            Margin = new Thickness(0, 0, 0, metrics.KeepFormattingBottomMargin)
        };

        _leaderCombo = new ComboBox
        {
            Height = metrics.ComboBoxHeight,
            Margin = new Thickness(0, 0, 0, metrics.ComboBottomMargin)
        };
        foreach (var choice in leaders)
            _leaderCombo.Items.Add(choice);
        _leaderCombo.SelectedIndex = TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(leaders, state.TabLeader);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: metrics.ActionButtonWidth,
            rowMargin: new Thickness(0, metrics.ActionTopMargin, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(metrics.OuterInset) };
        panel.Children.Add(MakeLabel(TableOfAuthoritiesDialogPlanner.CategoryLabel));
        panel.Children.Add(_categoryCombo);
        panel.Children.Add(_passimBox);
        panel.Children.Add(_keepFormattingBox);
        panel.Children.Add(MakeLabel(TableOfAuthoritiesDialogPlanner.TabLeaderLabel));
        panel.Children.Add(_leaderCombo);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _categoryCombo.Focus();
    }

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Margin = new Thickness(
                0,
                0,
                0,
                TableOfAuthoritiesDialogPlanner.VisualMetrics.LabelBottomMargin)
        };

    private void Accept()
    {
        var categoryFilter = (_categoryCombo.SelectedItem as TableOfAuthoritiesCategoryChoice)?.Category;
        var leader = (_leaderCombo.SelectedItem as TableOfAuthoritiesTabLeaderChoice)?.Leader ?? ToaTabLeader.Dots;
        var state = new TableOfAuthoritiesDialogState(
            _passimBox.IsChecked == true,
            _keepFormattingBox.IsChecked == true,
            categoryFilter,
            leader);

        _result = new Result(TableOfAuthoritiesDialogPlanner.BuildOptions(state));
        Close();
    }

    // -----------------------------------------------------------------------
    // Test seam
    // -----------------------------------------------------------------------

    /// <summary>
    /// Test seam: construct the dialog without showing it so STA tests can exercise the control wiring.
    /// Seed values default to: All categories, passim=false, keep-formatting=false, Dots leader.
    /// </summary>
    internal static TableOfAuthoritiesDialog CreateForTest(
        bool passim = false,
        bool keepFormatting = false,
        CitationCategory? categoryFilter = null,
        ToaTabLeader leader = ToaTabLeader.Dots)
    {
        var dlg = new TableOfAuthoritiesDialog(
            owner: null,
            options: new ToaOptions
            {
                UsePassim = passim,
                KeepOriginalFormatting = keepFormatting,
                CategoryFilter = categoryFilter,
                TabLeader = leader
            });

        dlg._categoryCombo.SelectedIndex = TableOfAuthoritiesDialogPlanner.SelectCategoryIndex(
            dlg._categoryCombo.Items.OfType<TableOfAuthoritiesCategoryChoice>().ToList(),
            categoryFilter);

        dlg._leaderCombo.SelectedIndex = TableOfAuthoritiesDialogPlanner.SelectTabLeaderIndex(
            dlg._leaderCombo.Items.OfType<TableOfAuthoritiesTabLeaderChoice>().ToList(),
            leader);

        return dlg;
    }

    /// <summary>
    /// Test seam: run Accept logic and return the produced <see cref="Result"/> without closing the window.
    /// </summary>
    internal Result? AcceptForTest()
    {
        Accept();
        return _result;
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Show the Table of Authorities options dialog. Returns the chosen <see cref="Result"/>, or null if
    /// cancelled.
    /// </summary>
    public static Result? Prompt(Window? owner, ToaOptions? options = null)
    {
        var dlg = new TableOfAuthoritiesDialog(owner, options ?? ToaOptions.Default);
        dlg.ShowDialog();
        return dlg._result;
    }

}
