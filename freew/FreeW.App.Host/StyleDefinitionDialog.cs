using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FreeW.App.Presentation.Dialogs;
using FreeW.Core.Model;
using ModelTextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Host;

/// <summary>
/// Word's Create New Style from Formatting / Modify Style dialog. Everything it decides lives in
/// <see cref="StyleDefinitionDialogSession"/>; this class only projects that session onto WPF controls,
/// forwards edits back, and opens the existing Font, Paragraph, Tabs and Borders dialogs from the
/// Format menu against the style's own formatting.
/// </summary>
internal static class StyleDefinitionDialog
{
    public static StyleDefinition? AskNew(
        Window? owner,
        TextDocument document,
        string? defaultBasedOnId) =>
        Show(owner, document, StyleDefinitionDialogPlanner.CreateNewSession(document, defaultBasedOnId));

    public static StyleDefinition? AskModify(
        Window? owner,
        TextDocument document,
        DocumentStyle existing) =>
        Show(owner, document, StyleDefinitionDialogPlanner.CreateModifySession(document, existing));

    private static StyleDefinition? Show(Window? owner, TextDocument document, StyleDefinitionDialogSession session)
    {
        var view = Create(owner, document, session);
        return view.Window.ShowDialog() == true ? view.Result : null;
    }

    /// <summary>The built dialog plus its acceptance, separated from <c>ShowDialog</c> so it can be driven without a modal loop.</summary>
    internal sealed class View(Window window)
    {
        public Window Window { get; } = window;

        public StyleDefinition? Result { get; set; }

        /// <summary>Validates the dialog; on success stores <see cref="Result"/> and returns true.</summary>
        public Func<bool> TryAccept { get; set; } = () => false;
    }

    internal static View Create(Window? owner, TextDocument document, StyleDefinitionDialogSession session)
    {
        var syncing = false;

        var dialog = new FreeWDialogWindow
        {
            Title = session.Title,
            Width = StyleDefinitionDialogPlanner.DialogWidth,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
        };

        var name = new TextBox { Text = session.Name, IsReadOnly = !session.CanRename };
        var kind = new ComboBox { ItemsSource = session.KindChoices, IsEnabled = session.CanChangeKind };
        var basedOn = new ComboBox();
        var nextStyle = new ComboBox();
        var font = new ComboBox
        {
            IsEditable = true,
            MinWidth = 170,
            ItemsSource = Fonts.SystemFontFamilies
                .Select(family => family.Source)
                .OrderBy(source => source, StringComparer.CurrentCultureIgnoreCase)
                .ToArray(),
        };
        var size = new ComboBox { IsEditable = true, Width = 64, ItemsSource = session.FontSizeOptions };
        var bold = Toggle("B", StyleDefinitionDialogText.Bold, FontWeights.Bold, FontStyles.Normal, null);
        var italic = Toggle("I", StyleDefinitionDialogText.Italic, FontWeights.Normal, FontStyles.Italic, null);
        var underline = Toggle("U", StyleDefinitionDialogText.Underline, FontWeights.Normal, FontStyles.Normal, TextDecorations.Underline);
        var strike = Toggle("abc", StyleDefinitionDialogText.Strikethrough, FontWeights.Normal, FontStyles.Normal, TextDecorations.Strikethrough);
        var color = new ComboBox { MinWidth = 120, ItemsSource = session.FontColors };
        var highlight = new ComboBox { MinWidth = 120, ItemsSource = session.HighlightColors };
        var alignment = new ComboBox { MinWidth = 100, ItemsSource = session.AlignmentOptions };
        var addToGallery = new CheckBox
        {
            Content = StyleDefinitionDialogText.AddToGallery,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var previewHost = new Border
        {
            Height = StyleDefinitionDialogPlanner.PreviewHeight,
            Background = Brushes.White,
            BorderBrush = SystemColors.ActiveBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            ClipToBounds = true,
            Child = previewText,
        };
        var description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MinHeight = StyleDefinitionDialogPlanner.DescriptionHeight,
            Margin = new Thickness(0, 6, 0, 0),
        };

        Automate(name, StyleDefinitionDialogSession.NameAutomationId, StyleDefinitionDialogText.NameLabel);
        Automate(kind, StyleDefinitionDialogSession.KindAutomationId, StyleDefinitionDialogText.TypeLabel);
        Automate(basedOn, StyleDefinitionDialogSession.BasedOnAutomationId, StyleDefinitionDialogText.BasedOnLabel);
        Automate(nextStyle, StyleDefinitionDialogSession.NextStyleAutomationId, StyleDefinitionDialogText.NextStyleLabel);
        Automate(font, StyleDefinitionDialogSession.FontAutomationId, StyleDefinitionDialogText.FontLabel);
        Automate(size, StyleDefinitionDialogSession.SizeAutomationId, StyleDefinitionDialogText.SizeLabel);
        Automate(bold, StyleDefinitionDialogSession.BoldAutomationId, StyleDefinitionDialogText.Bold);
        Automate(italic, StyleDefinitionDialogSession.ItalicAutomationId, StyleDefinitionDialogText.Italic);
        Automate(underline, StyleDefinitionDialogSession.UnderlineAutomationId, StyleDefinitionDialogText.Underline);
        Automate(strike, StyleDefinitionDialogSession.StrikethroughAutomationId, StyleDefinitionDialogText.Strikethrough);
        Automate(color, StyleDefinitionDialogSession.FontColorAutomationId, StyleDefinitionDialogText.FontColorLabel);
        Automate(highlight, StyleDefinitionDialogSession.HighlightAutomationId, StyleDefinitionDialogText.HighlightLabel);
        Automate(alignment, StyleDefinitionDialogSession.AlignmentAutomationId, StyleDefinitionDialogText.AlignmentLabel);
        Automate(previewHost, StyleDefinitionDialogSession.PreviewAutomationId, StyleDefinitionDialogText.PreviewLabel);
        Automate(description, StyleDefinitionDialogSession.DescriptionAutomationId, StyleDefinitionDialogText.PreviewLabel);
        Automate(addToGallery, StyleDefinitionDialogSession.AddToGalleryAutomationId, StyleDefinitionDialogText.AddToGallery);

        // Push the session's state onto every control (after a type change or a Format-menu dialog).
        void SyncControls()
        {
            syncing = true;
            try
            {
                kind.SelectedIndex = session.KindIndex;
                basedOn.ItemsSource = session.BasedOnOptions;
                basedOn.SelectedIndex = session.BasedOnIndex;
                nextStyle.ItemsSource = session.NextStyleOptions;
                nextStyle.SelectedIndex = session.HasParagraphFormatting ? session.NextStyleIndex : -1;
                nextStyle.IsEnabled = session.HasParagraphFormatting;
                font.Text = session.FontFamilyText;
                size.Text = session.FontSizeText;
                bold.IsChecked = session.Run.Bold;
                italic.IsChecked = session.Run.Italic;
                underline.IsChecked = session.Run.Underline;
                strike.IsChecked = session.Run.Strikethrough || session.Run.DoubleStrikethrough;
                color.SelectedIndex = session.FontColorIndex;
                highlight.SelectedIndex = session.HighlightIndex;
                alignment.SelectedIndex = session.AlignmentIndex;
                alignment.IsEnabled = session.HasParagraphFormatting;
                addToGallery.IsChecked = session.AddToGallery;
            }
            finally
            {
                syncing = false;
            }
            RefreshPreview();
        }

        void RefreshPreview()
        {
            var preview = session.Preview;
            previewText.Text = preview.SampleText;
            previewText.FontFamily = new FontFamily(preview.FontFamily);
            previewText.FontSize = Math.Clamp(preview.FontSizePt, 1, 72) * 96.0 / 72.0;
            previewText.FontWeight = preview.Bold ? FontWeights.Bold : FontWeights.Normal;
            previewText.FontStyle = preview.Italic ? FontStyles.Italic : FontStyles.Normal;
            var decorations = new TextDecorationCollection();
            if (preview.Underline)
                decorations.Add(TextDecorations.Underline);
            if (preview.Strikethrough)
                decorations.Add(TextDecorations.Strikethrough);
            previewText.TextDecorations = decorations;
            previewText.Foreground = BrushFor(preview.ColorHex) ?? Brushes.Black;
            previewText.Background = BrushFor(preview.HighlightHex) ?? BrushFor(preview.ShadingHex);
            previewText.TextAlignment = preview.Alignment switch
            {
                ModelTextAlignment.Center => System.Windows.TextAlignment.Center,
                ModelTextAlignment.Right => System.Windows.TextAlignment.Right,
                ModelTextAlignment.Justify => System.Windows.TextAlignment.Justify,
                _ => System.Windows.TextAlignment.Left,
            };
            previewText.Margin = new Thickness(Math.Clamp(preview.IndentLeftPt, 0, 144) * 96.0 / 72.0, 0, 0, 0);
            description.Text = session.Description;
        }

        void Edit(Action change)
        {
            if (syncing)
                return;
            change();
            RefreshPreview();
        }

        name.TextChanged += (_, _) => Edit(() => session.Name = name.Text);
        kind.SelectionChanged += (_, _) =>
        {
            if (syncing)
                return;
            session.SelectKind(kind.SelectedIndex);
            SyncControls();
        };
        basedOn.SelectionChanged += (_, _) => Edit(() => session.SelectBasedOn(basedOn.SelectedIndex));
        nextStyle.SelectionChanged += (_, _) => Edit(() => session.SelectNextStyle(nextStyle.SelectedIndex));
        font.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(
            (_, _) => Edit(() => session.SetFontFamily(font.Text))));
        font.SelectionChanged += (_, _) => Edit(() => session.SetFontFamily(font.SelectedItem as string ?? font.Text));
        size.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(
            (_, _) => Edit(() => session.SetFontSizeText(size.Text))));
        size.SelectionChanged += (_, _) => Edit(() => session.SetFontSizeText(size.SelectedItem as string ?? size.Text));
        bold.Click += (_, _) => Edit(() => session.SetBold(bold.IsChecked == true));
        italic.Click += (_, _) => Edit(() => session.SetItalic(italic.IsChecked == true));
        underline.Click += (_, _) => Edit(() => session.SetUnderline(underline.IsChecked == true));
        strike.Click += (_, _) => Edit(() => session.SetStrikethrough(strike.IsChecked == true));
        color.SelectionChanged += (_, _) => Edit(() => session.SelectFontColor(color.SelectedIndex));
        highlight.SelectionChanged += (_, _) => Edit(() => session.SelectHighlight(highlight.SelectedIndex));
        alignment.SelectionChanged += (_, _) => Edit(() => session.SelectAlignment(alignment.SelectedIndex));
        addToGallery.Click += (_, _) => Edit(() => session.AddToGallery = addToGallery.IsChecked == true);

        // Format ▾: each entry opens the existing dialog seeded with the style's own formatting.
        var formatButton = new Button
        {
            Content = StyleDefinitionDialogText.FormatButton + " ▾",
            MinWidth = StyleDefinitionDialogPlanner.ActionButtonWidth,
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Automate(formatButton, StyleDefinitionDialogSession.FormatButtonAutomationId, StyleDefinitionDialogText.FormatButton);
        formatButton.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            menu.PlacementTarget = formatButton;
            menu.Placement = PlacementMode.Top;
            foreach (var entry in session.FormatMenu)
            {
                var item = new MenuItem { Header = entry.Label, IsEnabled = entry.IsEnabled };
                AutomationProperties.SetAutomationId(item, entry.AutomationId);
                var command = entry.Command;
                item.Click += (_, _) =>
                {
                    RunFormatCommand(dialog, document, session, command);
                    SyncControls();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };

        var view = new View(dialog);
        view.TryAccept = () =>
        {
            session.Name = name.Text;
            session.SetFontFamily(font.Text);
            session.SetFontSizeText(size.Text);
            var acceptance = session.PlanAcceptance();
            if (!acceptance.IsAccepted)
            {
                DialogMessageHelper.ShowWarning(dialog, acceptance.ErrorMessage ?? string.Empty, session.Title);
                if (acceptance.Error == StyleDefinitionValidationError.FontSize)
                    size.Focus();
                else
                    name.Focus();
                return false;
            }

            view.Result = acceptance.Definition;
            return true;
        };

        void Accept()
        {
            if (view.TryAccept())
                dialog.DialogResult = true;
        }

        var properties = new Grid();
        properties.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        properties.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddLabelledRow(properties, StyleDefinitionDialogText.NameLabel, name);
        AddLabelledRow(properties, StyleDefinitionDialogText.TypeLabel, kind);
        AddLabelledRow(properties, StyleDefinitionDialogText.BasedOnLabel, basedOn);
        AddLabelledRow(properties, StyleDefinitionDialogText.NextStyleLabel, nextStyle);

        var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        fontRow.Children.Add(Labelled(StyleDefinitionDialogText.FontLabel, font));
        fontRow.Children.Add(Labelled(StyleDefinitionDialogText.SizeLabel, size));
        var toggles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 0),
        };
        foreach (var toggle in new[] { bold, italic, underline, strike })
            toggles.Children.Add(toggle);
        fontRow.Children.Add(toggles);

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        colorRow.Children.Add(Labelled(StyleDefinitionDialogText.FontColorLabel, color));
        colorRow.Children.Add(Labelled(StyleDefinitionDialogText.HighlightLabel, highlight));
        colorRow.Children.Add(Labelled(StyleDefinitionDialogText.AlignmentLabel, alignment));

        var formatting = new StackPanel();
        formatting.Children.Add(fontRow);
        formatting.Children.Add(colorRow);
        formatting.Children.Add(previewHost);
        formatting.Children.Add(description);

        var buttons = DialogButtonRowFactory.Create(
            Accept,
            buttonWidth: StyleDefinitionDialogPlanner.ActionButtonWidth,
            rowMargin: new Thickness(0));
        var actionRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.Children.Add(formatButton);
        Grid.SetColumn(buttons, 1);
        actionRow.Children.Add(buttons);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new GroupBox
        {
            Header = StyleDefinitionDialogText.PropertiesGroup,
            Padding = new Thickness(8),
            Content = properties,
        });
        panel.Children.Add(new GroupBox
        {
            Header = StyleDefinitionDialogText.FormattingGroup,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Content = formatting,
        });
        panel.Children.Add(addToGallery);
        panel.Children.Add(actionRow);
        dialog.Content = panel;

        SyncControls();
        if (session.CanRename)
            name.Focus();
        else
            basedOn.Focus();

        return view;
    }

    private static void RunFormatCommand(
        Window owner,
        TextDocument document,
        StyleDefinitionDialogSession session,
        StyleDefinitionFormatCommand command)
    {
        switch (command)
        {
            case StyleDefinitionFormatCommand.Font:
                if (FontDialog.Prompt(owner, session.Run) is { } run)
                    session.ReplaceRun(run);
                break;

            case StyleDefinitionFormatCommand.Paragraph:
                if (ParagraphBreaksDialog.Prompt(owner, session.Paragraph) is { } paragraph)
                    session.ApplyParagraphDialogResult(paragraph);
                break;

            case StyleDefinitionFormatCommand.Tabs:
                if (TabsDialog.Prompt(owner, session.Paragraph.TabStops, document.Page.DefaultTabStopPt) is { } tabs)
                    session.ApplyTabStops(tabs.TabStops);
                break;

            case StyleDefinitionFormatCommand.Border:
                if (BordersAndShadingDialog.Prompt(owner, session.Paragraph, pageBorder: null) is { } border)
                    session.ApplyBorderAndShading(border.ParagraphBorder, border.ShadingHex, border.ShadingPattern);
                break;
        }
    }

    private static ToggleButton Toggle(
        string glyph,
        string toolTip,
        FontWeight weight,
        FontStyle style,
        TextDecorationCollection? decorations) => new()
    {
        Content = new TextBlock
        {
            Text = glyph,
            FontWeight = weight,
            FontStyle = style,
            TextDecorations = decorations,
        },
        ToolTip = toolTip,
        MinWidth = 26,
        Height = 22,
        Margin = new Thickness(0, 0, 2, 0),
    };

    private static void Automate(FrameworkElement element, string automationId, string name)
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name.TrimEnd(':', ' '));
    }

    private static FrameworkElement Labelled(string label, FrameworkElement field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(field);
        return panel;
    }

    private static void AddLabelledRow(Grid grid, string label, FrameworkElement field)
    {
        var row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 6),
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);
        field.Margin = new Thickness(0, 0, 0, 6);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static Brush? BrushFor(string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            return null;
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
