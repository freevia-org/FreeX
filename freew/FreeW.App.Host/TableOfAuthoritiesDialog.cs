using System.Windows;
using System.Windows.Controls;
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

    private TableOfAuthoritiesDialog(Window? owner)
    {
        Owner = owner;
        Title = "Table of Authorities";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        // Category combo: "(All)" + one entry per category.
        _categoryCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _categoryCombo.Items.Add(new CategoryItem(null, "(All)"));
        foreach (var cat in Enum.GetValues<CitationCategory>())
            _categoryCombo.Items.Add(new CategoryItem(cat, TableOfAuthorities.CategoryHeading(cat)));
        _categoryCombo.SelectedIndex = 0;

        _passimBox = new CheckBox { Content = "Use passim", Margin = new Thickness(0, 0, 0, 6) };
        _keepFormattingBox = new CheckBox { Content = "Keep original formatting", Margin = new Thickness(0, 0, 0, 8) };

        // Tab leader combo.
        _leaderCombo = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _leaderCombo.Items.Add(new LeaderItem(ToaTabLeader.Dots, "Dots ......"));
        _leaderCombo.Items.Add(new LeaderItem(ToaTabLeader.Dashes, "Dashes ——————"));
        _leaderCombo.Items.Add(new LeaderItem(ToaTabLeader.Underline, "Underline ______"));
        _leaderCombo.Items.Add(new LeaderItem(ToaTabLeader.None, "(None)"));
        _leaderCombo.SelectedIndex = 0;

        var buttons = DialogButtonRowFactory.Create(Accept, buttonWidth: 80, rowMargin: new Thickness(0, 12, 0, 0));

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(MakeLabel("Category:"));
        panel.Children.Add(_categoryCombo);
        panel.Children.Add(_passimBox);
        panel.Children.Add(_keepFormattingBox);
        panel.Children.Add(MakeLabel("Tab leader:"));
        panel.Children.Add(_leaderCombo);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _categoryCombo.Focus();
    }

    private static TextBlock MakeLabel(string text) =>
        new() { Text = text, Margin = new Thickness(0, 0, 0, 4) };

    private void Accept()
    {
        var categoryFilter = (_categoryCombo.SelectedItem as CategoryItem)?.Category;
        var leader = (_leaderCombo.SelectedItem as LeaderItem)?.Leader ?? ToaTabLeader.Dots;

        _result = new Result(new ToaOptions
        {
            UsePassim = _passimBox.IsChecked == true,
            KeepOriginalFormatting = _keepFormattingBox.IsChecked == true,
            CategoryFilter = categoryFilter,
            TabLeader = leader
        });
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
        var dlg = new TableOfAuthoritiesDialog(owner: null);
        dlg._passimBox.IsChecked = passim;
        dlg._keepFormattingBox.IsChecked = keepFormatting;

        // Select the matching category item.
        foreach (CategoryItem item in dlg._categoryCombo.Items)
        {
            if (item.Category == categoryFilter)
            {
                dlg._categoryCombo.SelectedItem = item;
                break;
            }
        }

        // Select the matching leader.
        foreach (LeaderItem item in dlg._leaderCombo.Items)
        {
            if (item.Leader == leader)
            {
                dlg._leaderCombo.SelectedItem = item;
                break;
            }
        }

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
    public static Result? Prompt(Window? owner)
    {
        var dlg = new TableOfAuthoritiesDialog(owner);
        dlg.ShowDialog();
        return dlg._result;
    }

    // -----------------------------------------------------------------------
    // Helper items
    // -----------------------------------------------------------------------

    private sealed record CategoryItem(CitationCategory? Category, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LeaderItem(ToaTabLeader Leader, string Label)
    {
        public override string ToString() => Label;
    }
}
