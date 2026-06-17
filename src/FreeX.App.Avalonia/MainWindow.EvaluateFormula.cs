using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using FreeX.Core.Model;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // Formulas ▸ Evaluate Formula (parity gap: the button was a no-op). A read-only diagnostics dialog
    // that shows the active cell's reference, its input (formula or literal text), and its current
    // computed value. v1 is read-only — no stepping. Precedent listing is intentionally skipped: the
    // dependency graph is owned internally by the calc engine and is not exposed through the session as a
    // clean read-only call, so there is no safe way to enumerate precedents from here.

    private async Task ShowEvaluateFormulaDialogAsync()
    {
        var address = _session.ActiveCell;
        var cell = _session.ActiveSheet.GetCell(address);

        var reference = $"{_session.ActiveSheet.Name}!{FormatCellReference(address)}";
        var input = FormatEditText(cell, address);
        var hasFormula = cell?.HasFormula == true;
        var computed = FormatScalarValue(cell?.Value);

        if (string.IsNullOrEmpty(input))
            input = "(empty)";
        if (string.IsNullOrEmpty(computed))
            computed = "(empty)";

        var layout = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
        };

        layout.Children.Add(new TextBlock
        {
            Text = "Cell reference",
            FontWeight = FontWeight.SemiBold,
        });
        layout.Children.Add(new TextBlock
        {
            Text = reference,
            TextWrapping = TextWrapping.Wrap,
        });

        layout.Children.Add(new TextBlock
        {
            Text = hasFormula ? "Formula" : "Cell input",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        });
        layout.Children.Add(new SelectableTextBlock
        {
            Text = input,
            FontFamily = new FontFamily("Consolas, Menlo, Monospace"),
            TextWrapping = TextWrapping.Wrap,
        });

        layout.Children.Add(new TextBlock
        {
            Text = "Evaluation result",
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0),
        });
        layout.Children.Add(new SelectableTextBlock
        {
            Text = computed,
            FontFamily = new FontFamily("Consolas, Menlo, Monospace"),
            TextWrapping = TextWrapping.Wrap,
        });

        var dialog = new Window
        {
            Title = "Evaluate Formula",
            Width = 420,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var closeButton = new Button
        {
            Content = "Close",
            Width = 90,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
        };
        closeButton.Click += (_, _) => dialog.Close();
        layout.Children.Add(closeButton);

        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = layout,
        };

        await dialog.ShowDialog(this);
    }
}
