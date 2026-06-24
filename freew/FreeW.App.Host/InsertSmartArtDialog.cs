using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Modal "Insert SmartArt" dialog: a <see cref="SmartArtKind"/> picker plus an editable list of node
/// texts. Returns a <see cref="SmartArt"/> on OK, or null if the user cancels. A sensible Process
/// default is pre-populated so clicking OK with no edits inserts a working diagram.
/// A seed <see cref="SmartArt"/> may be passed to pre-populate from an existing diagram (Edit Text reuse).
/// </summary>
internal sealed class InsertSmartArtDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    // ── Controls ────────────────────────────────────────────────────────────────────────────────
    private readonly ComboBox _kindBox;
    private readonly ListBox _nodeList;
    private readonly TextBox _nodeTextBox;
    private SmartArt? _result;

    // ── Defaults ─────────────────────────────────────────────────────────────────────────────────
    private static readonly string[] DefaultNodeTexts = ["First", "Second", "Third"];

    // ── Constructor ──────────────────────────────────────────────────────────────────────────────
    private InsertSmartArtDialog(Window? owner, SmartArt? seed)
    {
        Owner = owner;
        Title = seed is null ? "Insert SmartArt" : "Edit SmartArt Text";
        Width = 440;
        MinHeight = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };

        // ── Layout picker ────────────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock { Text = "Layout:", Margin = new Thickness(0, 0, 0, 4) });
        _kindBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (SmartArtKind kind in Enum.GetValues<SmartArtKind>())
            _kindBox.Items.Add(KindLabel(kind));
        _kindBox.SelectedIndex = (int)(seed?.Kind ?? SmartArtKind.Process);
        panel.Children.Add(_kindBox);

        // ── Node list + editing ──────────────────────────────────────────────────────────────────
        panel.Children.Add(new TextBlock
        {
            Text = "Diagram text  (one item per node — use Add/Remove to manage):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _nodeList = new ListBox
        {
            Height = 130,
            Margin = new Thickness(0, 0, 0, 6),
            SelectionMode = SelectionMode.Single
        };
        foreach (var text in NodeTextsFrom(seed))
            _nodeList.Items.Add(text);
        if (_nodeList.Items.Count > 0)
            _nodeList.SelectedIndex = 0;
        panel.Children.Add(_nodeList);

        // Edit box for the selected item
        _nodeTextBox = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 6),
            IsEnabled = _nodeList.SelectedItem is not null
        };
        if (_nodeList.SelectedItem is string first)
            _nodeTextBox.Text = first;
        panel.Children.Add(_nodeTextBox);

        // Wire selection → edit box
        _nodeList.SelectionChanged += (_, _) =>
        {
            if (_nodeList.SelectedItem is string selected)
            {
                _nodeTextBox.IsEnabled = true;
                _nodeTextBox.Text = selected;
            }
            else
            {
                _nodeTextBox.IsEnabled = false;
                _nodeTextBox.Text = string.Empty;
            }
        };

        // Wire edit box → list item
        _nodeTextBox.TextChanged += (_, _) =>
        {
            if (_nodeList.SelectedIndex >= 0)
            {
                _nodeList.Items[_nodeList.SelectedIndex] = _nodeTextBox.Text;
                _nodeList.SelectedIndex = _nodeList.SelectedIndex; // keep selection visible
            }
        };

        // ── Add / Remove buttons ─────────────────────────────────────────────────────────────────
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var addBtn = new Button { Content = "Add Shape", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
        var removeBtn = new Button { Content = "Remove Shape", Padding = new Thickness(8, 3, 8, 3) };
        addBtn.Click += (_, _) =>
        {
            var idx = _nodeList.Items.Add("New Item");
            _nodeList.SelectedIndex = idx;
            _nodeTextBox.Focus();
            _nodeTextBox.SelectAll();
        };
        removeBtn.Click += (_, _) =>
        {
            var idx = _nodeList.SelectedIndex;
            if (idx < 0 || _nodeList.Items.Count <= 1) return;
            _nodeList.Items.RemoveAt(idx);
            _nodeList.SelectedIndex = Math.Min(idx, _nodeList.Items.Count - 1);
        };
        buttonRow.Children.Add(addBtn);
        buttonRow.Children.Add(removeBtn);
        panel.Children.Add(buttonRow);

        // ── OK / Cancel ──────────────────────────────────────────────────────────────────────────
        var okCancel = DialogButtonRowFactory.Create(Accept, buttonWidth: 72, rowMargin: new Thickness(0, 4, 0, 0));
        panel.Children.Add(okCancel);

        Content = panel;
        DialogFocus.FocusAndSelect(_nodeTextBox);
    }

    // ── Accept logic ─────────────────────────────────────────────────────────────────────────────
    private void Accept()
    {
        // Flush current edit box into the selected item
        if (_nodeList.SelectedIndex >= 0)
            _nodeList.Items[_nodeList.SelectedIndex] = _nodeTextBox.Text;

        var texts = _nodeList.Items.Cast<string>()
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList();

        if (texts.Count == 0)
        {
            DialogMessageHelper.ShowWarning(this, "Enter at least one node text.");
            return;
        }

        var kindIndex = _kindBox.SelectedIndex;
        var kind = kindIndex >= 0 && kindIndex < Enum.GetValues<SmartArtKind>().Length
            ? (SmartArtKind)kindIndex
            : SmartArtKind.Process;

        _result = SmartArt.Create(kind, texts);
        Close();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────
    private static string KindLabel(SmartArtKind kind) => kind switch
    {
        SmartArtKind.List      => "List",
        SmartArtKind.Process   => "Process",
        SmartArtKind.Hierarchy => "Hierarchy",
        _                      => kind.ToString()
    };

    private static IEnumerable<string> NodeTextsFrom(SmartArt? seed)
    {
        if (seed is null)
            return DefaultNodeTexts;
        // For Hierarchy, flatten breadth-first so all node texts are visible
        var texts = new List<string>();
        foreach (var node in seed.Nodes)
        {
            texts.Add(node.Text);
            foreach (var child in node.Children)
                texts.Add(child.Text);
        }
        return texts.Count > 0 ? texts : DefaultNodeTexts;
    }

    // ── Public API ───────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Show the Insert SmartArt dialog, optionally seeded from an existing diagram (for Edit Text reuse).
    /// Returns the configured <see cref="SmartArt"/>, or null if the user cancelled.
    /// </summary>
    public static SmartArt? Prompt(Window? owner, SmartArt? seed = null)
    {
        var dialog = new InsertSmartArtDialog(owner, seed);
        dialog.ShowDialog();
        return dialog._result;
    }
}
