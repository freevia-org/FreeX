using System.Windows;
using System.Windows.Controls;
using Free.Shared.Shell;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Thin WPF projection of the shared proofing-language catalog and selection plan.
/// </summary>
internal sealed class ProofingLanguageDialog : Free.Shared.Ribbon.Wpf.DialogWindow
{
    private readonly ListBox _languages = new();

    // Kept parameterless so the visual-evidence harness can construct the production surface.
    internal ProofingLanguageDialog()
        : this(currentTag: null)
    {
    }

    private ProofingLanguageDialog(string? currentTag)
    {
        var plan = ProofingLanguageDialogPlanner.Build(currentTag, UiText.Get);
        Title = plan.Text.Title;
        Width = 320;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;

        foreach (var choice in plan.Choices)
            _languages.Items.Add(new ListBoxItem { Content = choice.DisplayText, Tag = choice.Tag });
        _languages.SelectedIndex = plan.SelectedIndex;
        _languages.MouseDoubleClick += (_, _) => Accept();

        var scroll = new ScrollViewer
        {
            Content = _languages,
            Height = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var outer = new StackPanel { Margin = new Thickness(12) };
        outer.Children.Add(new TextBlock
        {
            Text = plan.Text.Instruction,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        outer.Children.Add(scroll);
        outer.Children.Add(DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: 80,
            acceptContent: plan.Text.OkLabel,
            cancelContent: plan.Text.CancelLabel));
        Content = outer;

        Loaded += (_, _) => _languages.Focus();
    }

    public string? Result { get; private set; }

    public static string? Choose(Window? owner, string? currentTag)
    {
        var dialog = new ProofingLanguageDialog(currentTag) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Accept()
    {
        if (_languages.SelectedItem is not ListBoxItem selected)
            return;

        Result = selected.Tag as string ?? string.Empty;
        DialogResult = true;
    }
}
