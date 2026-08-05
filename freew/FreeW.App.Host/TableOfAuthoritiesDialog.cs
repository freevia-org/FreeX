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
/// Returns the chosen <see cref="ToaOptions"/>, or null when cancelled.
/// </summary>
internal sealed class TableOfAuthoritiesDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly TableOfAuthoritiesDialogSession _session;
    private readonly ComboBox _categoryCombo;
    private readonly CheckBox _passimBox;
    private readonly CheckBox _keepFormattingBox;
    private readonly ComboBox _leaderCombo;
    private ToaOptions? _result;

    private TableOfAuthoritiesDialog(Window? owner, ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Owner = owner;
        Title = TableOfAuthoritiesDialogPlanner.Title;
        Width = TableOfAuthoritiesDialogPlanner.DialogWidth;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _session = TableOfAuthoritiesDialogPlanner.CreateSession(options);
        var state = _session.State;

        _categoryCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var choice in _session.Categories)
            _categoryCombo.Items.Add(choice);
        _categoryCombo.SelectedIndex = state.CategoryIndex;

        _passimBox = new CheckBox
        {
            Content = TableOfAuthoritiesDialogPlanner.UsePassimLabel,
            IsChecked = state.UsePassim,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _keepFormattingBox = new CheckBox
        {
            Content = TableOfAuthoritiesDialogPlanner.KeepOriginalFormattingLabel,
            IsChecked = state.KeepOriginalFormatting,
            Margin = new Thickness(0, 0, 0, 8)
        };

        _leaderCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var choice in _session.TabLeaders)
            _leaderCombo.Items.Add(choice);
        _leaderCombo.SelectedIndex = state.TabLeaderIndex;
        _categoryCombo.SelectionChanged += (_, _) => _session.UpdateCategory(_categoryCombo.SelectedIndex);
        _passimBox.Checked += (_, _) => _session.UpdateUsePassim(true);
        _passimBox.Unchecked += (_, _) => _session.UpdateUsePassim(false);
        _keepFormattingBox.Checked += (_, _) => _session.UpdateKeepOriginalFormatting(true);
        _keepFormattingBox.Unchecked += (_, _) => _session.UpdateKeepOriginalFormatting(false);
        _leaderCombo.SelectionChanged += (_, _) => _session.UpdateTabLeader(_leaderCombo.SelectedIndex);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: TableOfAuthoritiesDialogPlanner.ButtonWidth,
            rowMargin: new Thickness(0, 12, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(TableOfAuthoritiesDialogPlanner.OuterMargin) };
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
        new() { Text = text, Margin = new Thickness(0, 0, 0, 4) };

    private void Accept()
    {
        SynchronizeSession();
        var acceptance = _session.PlanAcceptance();
        if (!acceptance.IsAccepted)
        {
            FocusValidation(acceptance.Validation?.Field);
            return;
        }

        _result = acceptance.Options;
        Close();
    }

    private void SynchronizeSession()
    {
        _session.UpdateCategory(_categoryCombo.SelectedIndex);
        _session.UpdateUsePassim(_passimBox.IsChecked is true);
        _session.UpdateKeepOriginalFormatting(_keepFormattingBox.IsChecked is true);
        _session.UpdateTabLeader(_leaderCombo.SelectedIndex);
    }

    private void FocusValidation(TableOfAuthoritiesDialogField? field)
    {
        var target = field == TableOfAuthoritiesDialogField.TabLeader
            ? _leaderCombo
            : _categoryCombo;
        target.Focus();
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

        return dlg;
    }

    /// <summary>
    /// Test seam: run Accept logic and return the produced <see cref="ToaOptions"/> without closing the window.
    /// </summary>
    internal ToaOptions? AcceptForTest()
    {
        Accept();
        return _result;
    }

    // -----------------------------------------------------------------------
    // Entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Show the Table of Authorities options dialog. Returns the chosen <see cref="ToaOptions"/>, or null if
    /// cancelled.
    /// </summary>
    public static ToaOptions? Prompt(Window? owner, ToaOptions? options = null)
    {
        var dlg = new TableOfAuthoritiesDialog(owner, options ?? ToaOptions.Default);
        dlg.ShowDialog();
        return dlg._result;
    }

}
