using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Free.Shared.Ribbon;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Binds FreeW's ribbon command ids (declared in <see cref="FreeWRibbon"/>) to behavior over the
/// editing surface, implementing the shared <see cref="IRibbonCommandRegistry"/>. Formatting and
/// clipboard route through WPF's <see cref="EditingCommands"/>/<see cref="ApplicationCommands"/>
/// against the focused RichTextBox (inline edit + undo); bold/italic/underline are stateful so the
/// ribbon can reflect the selection.
/// </summary>
internal static class FreeWRibbonCommands
{
    public static RibbonCommandRegistry Build(DocumentView editor, RibbonStateStore stateStore)
    {
        var registry = new RibbonCommandRegistry();
        var stateful = new List<(RibbonCommandId Id, IRibbonStatefulCommand Command)>();

        void Routed(string id, RoutedCommand command) =>
            registry.Register(id, new RoutedEditCommand(editor, command));

        void Toggle(string id, RoutedCommand command, DependencyProperty property, Func<object?, bool> isOn)
        {
            var cmd = new ToggleFormatCommand(editor, command, property, isOn);
            registry.Register(id, cmd);
            stateful.Add((id, cmd));
        }

        Toggle("freew.bold", EditingCommands.ToggleBold, TextElement.FontWeightProperty,
            v => v is FontWeight w && w >= FontWeights.Bold);
        Toggle("freew.italic", EditingCommands.ToggleItalic, TextElement.FontStyleProperty,
            v => v is FontStyle s && s == FontStyles.Italic);
        Toggle("freew.underline", EditingCommands.ToggleUnderline, Inline.TextDecorationsProperty,
            v => v is TextDecorationCollection d && d.Count > 0);

        // Live ribbon state: when the caret/selection moves, recompute the toggle states and push
        // them into the shared RibbonStateStore, which the toggle buttons observe.
        editor.SelectionChanged += (_, _) =>
        {
            foreach (var (id, command) in stateful)
                stateStore.SetState(id, command.GetState());
        };

        Routed("freew.grow-font", EditingCommands.IncreaseFontSize);
        Routed("freew.shrink-font", EditingCommands.DecreaseFontSize);
        Routed("freew.align-left", EditingCommands.AlignLeft);
        Routed("freew.align-center", EditingCommands.AlignCenter);
        Routed("freew.align-right", EditingCommands.AlignRight);
        Routed("freew.bullets", EditingCommands.ToggleBullets);
        Routed("freew.numbering", EditingCommands.ToggleNumbering);
        Routed("freew.cut", ApplicationCommands.Cut);
        Routed("freew.copy", ApplicationCommands.Copy);
        Routed("freew.paste", ApplicationCommands.Paste);

        registry.Register("freew.font-family", new SelectionValueCommand(editor,
            (selection, value) => selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(value))));
        registry.Register("freew.font-size", new SelectionValueCommand(editor, (selection, value) =>
        {
            if (double.TryParse(value, out var points))
                selection.ApplyPropertyValue(TextElement.FontSizeProperty, points * 96.0 / 72.0);
        }));

        // Insert tab — Illustrations: pick an image file and insert it as an inline image run.
        registry.Register("freew.picture", new InsertPictureCommand(editor));

        registry.Register("freew.style-normal", new ApplyStyleCommand(editor, 11, bold: false, colorHex: null));
        registry.Register("freew.style-heading1", new ApplyStyleCommand(editor, 16, bold: true, colorHex: "#2F5496"));
        registry.Register("freew.style-title", new ApplyStyleCommand(editor, 28, bold: true, colorHex: null));

        // Layout tab — page settings (applied to the model; honoured by docx save + print).
        registry.Register("freew.orientation", new PageCommand(editor, page =>
        {
            (page.WidthPt, page.HeightPt) = (page.HeightPt, page.WidthPt);
            page.Landscape = !page.Landscape;
        }));
        registry.Register("freew.margins", new PageCommand(editor, page =>
        {
            var narrow = page.MarginLeftPt > 54;
            var margin = narrow ? 36.0 : 72.0;
            page.MarginLeftPt = page.MarginRightPt = page.MarginTopPt = page.MarginBottomPt = margin;
        }));
        registry.Register("freew.size", new PageCommand(editor, page =>
        {
            var isLetter = Math.Abs(page.WidthPt - 612) < 1 && Math.Abs(page.HeightPt - 792) < 1;
            (page.WidthPt, page.HeightPt) = isLetter ? (595.0, 842.0) : (612.0, 792.0); // toggle Letter <-> A4
        }));

        return registry;
    }

    // Applies a named paragraph style's formatting (size/weight/colour) to the current selection.
    private sealed class ApplyStyleCommand(DocumentView editor, double sizePt, bool bold, string? colorHex) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;

        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            var selection = editor.Selection;
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, sizePt * PxPerPoint);
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, bold ? FontWeights.Bold : FontWeights.Normal);
            var brush = colorHex is null ? Brushes.Black : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        }
    }

    private sealed class PageCommand(DocumentView editor, Action<PageSettings> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context) => apply(editor.Model.Page);
    }

    // Insert > Illustrations > Picture: pick an image, normalise to PNG, insert as an inline image run.
    private sealed class InsertPictureCommand(DocumentView editor) : IRibbonCommand
    {
        private const double PxPerPoint = 96.0 / 72.0;
        private const double MaxWidthPt = 400;

        public void Execute(RibbonCommandContext context)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
                Title = "Insert Picture"
            };
            if (dialog.ShowDialog(Window.GetWindow(editor)) != true)
                return;

            try
            {
                var image = LoadAsInlineImage(dialog.FileName);
                editor.Focus();
                editor.InsertImage(image);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(editor), $"Could not insert the image:\n{ex.Message}",
                    "FreeW", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Decode any supported format and re-encode to PNG so the docx writer only ever emits PNG.
        private static InlineImage LoadAsInlineImage(string path)
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.UriSource = new Uri(path);
            source.EndInit();
            source.Freeze();

            using var buffer = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(buffer);

            // Convert device-independent pixels to points, capping the width so large photos fit.
            var widthPt = source.PixelWidth / PxPerPoint;
            var heightPt = source.PixelHeight / PxPerPoint;
            if (widthPt > MaxWidthPt && widthPt > 0)
            {
                heightPt *= MaxWidthPt / widthPt;
                widthPt = MaxWidthPt;
            }
            return new InlineImage(buffer.ToArray(), widthPt, heightPt);
        }
    }

    // Applies a value chosen from a ribbon combo (font family/size) to the current selection.
    private sealed class SelectionValueCommand(DocumentView editor, Action<TextSelection, string> apply) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (context.Parameters.TryGetValue("value", out var raw) && raw is string value && value.Length > 0)
            {
                editor.Focus();
                apply(editor.Selection, value);
            }
        }
    }

    private sealed class RoutedEditCommand(DocumentView editor, RoutedCommand command) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }
    }

    private sealed class ToggleFormatCommand(
        DocumentView editor,
        RoutedCommand command,
        DependencyProperty property,
        Func<object?, bool> isOn) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            editor.Focus();
            if (command.CanExecute(null, editor))
                command.Execute(null, editor);
        }

        public RibbonCommandState GetState()
        {
            var value = editor.Selection.GetPropertyValue(property);
            return new RibbonCommandState(IsEnabled: true, IsChecked: value != DependencyProperty.UnsetValue && isOn(value));
        }
    }
}
