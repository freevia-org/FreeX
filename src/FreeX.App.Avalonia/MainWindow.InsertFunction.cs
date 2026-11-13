using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Dialogs;

using AvaloniaGrid = Avalonia.Controls.Grid;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert Function + Function Arguments dialogs for the Avalonia/macOS shell. Insert Function lets the
/// user search/filter the built-in catalog and pick a function; choosing one opens Function Arguments,
/// which composes <c>=FUNC(a, b)</c> from one labeled box per argument (with a live preview) and commits
/// it into the active cell through the same formula edit/commit path the formula bar uses.
/// </summary>
public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle InsertFunctionDialogChromeStyle => new(FormulaBarFontFamily);

    // The Most Recently Used list shown in the Insert Function category dropdown. Promoted whenever a
    // function is inserted (most recent first) and seeded from the catalog defaults.
    private IReadOnlyList<string> _insertFunctionMostRecentlyUsed = InsertFunctionCatalogPlanner.DefaultMostRecentlyUsed;

    private void InsertFunction() => RunGuarded(() => ShowInsertFunctionDialogAsync());

    // Formulas > Function Library category buttons: open Insert Function on that category, so the
    // list shows every function Excel's button would list.
    private Action InsertFunctionInCategory(string category) =>
        () => RunGuarded(() => ShowInsertFunctionDialogAsync(category));

    private async Task ShowInsertFunctionDialogAsync(string? initialCategory = null)
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var selected = await ShowInsertFunctionPickerDialogAsync(initialCategory);
        if (selected is null)
            return;

        var formula = await ShowFunctionArgumentsDialogAsync(selected);
        if (formula is null)
            return;

        InsertComposedFunctionFormula(selected.Name, formula);
    }

    private async Task<InsertFunctionCatalogEntry?> ShowInsertFunctionPickerDialogAsync(string? initialCategory = null)
    {
        var catalog = InsertFunctionCatalogPlanner.BuildCatalog();
        InsertFunctionCatalogEntry? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("MainWindow_Content_InsertFunction"),
            // WPF InsertFunctionDialog: Width = 560; Height = 460 (outer, resizable).
            Width = 560 - WpfResizableFrameWidth,
            Height = 460 - WpfResizableFrameHeight,
            MinWidth = 460,
            MinHeight = 380 - WpfResizableFrameHeight,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertFunctionDialog");

        // WPF: _searchBox { MinWidth = 260, Margin = (4, 0, 8, 6) } with the search button in the
        // trailing Auto column.
        var searchBox = new TextBox { MinWidth = 260, Margin = new Thickness(4, 0, 8, 6) };
        ApplyFnTextBoxChrome(searchBox);
        AutomationProperties.SetName(searchBox, ResolveInsertFunctionLabel("InsertFunction_SearchForAFunction"));
        AutomationProperties.SetAutomationId(searchBox, "InsertFunctionSearchBox");
        AutomationProperties.SetHelpText(searchBox, UiText.Get("InsertFunction_SearchHelpText"));

        var categoryBox = new ComboBox
        {
            ItemsSource = InsertFunctionCatalogPlanner.BuildCategoryChoices(catalog),
            SelectedItem = initialCategory ?? InsertFunctionCatalogPlanner.MostRecentlyUsedCategory,
            // WPF: _categoryBox { Margin = (4, 0, 0, 0) } fills the editor column.
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyFnComboBoxChrome(categoryBox);
        AutomationProperties.SetName(categoryBox, ResolveInsertFunctionLabel("InsertFunction_OrSelectACategory"));
        AutomationProperties.SetAutomationId(categoryBox, "InsertFunctionCategoryBox");

        var listBox = new ListBox { MinHeight = 80 };
        ApplyFnListBoxStyle(listBox);
        AutomationProperties.SetName(listBox, ResolveInsertFunctionLabel("InsertFunction_SelectAFunction"));
        AutomationProperties.SetAutomationId(listBox, "InsertFunctionListBox");

        var syntaxText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = HeaderForeground,
            // WPF: _syntaxText.Margin = (0, 0, 0, 4).
            Margin = new Thickness(0, 0, 0, 4),
        };
        AutomationProperties.SetName(syntaxText, UiText.Get("InsertFunction_FunctionSyntaxAutomationName"));
        AutomationProperties.SetAutomationId(syntaxText, "InsertFunctionSyntaxText");

        var descriptionText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            // WPF: Brushes.DimGray.
            Foreground = Brush(105, 105, 105),
        };
        AutomationProperties.SetName(descriptionText, UiText.Get("InsertFunction_FunctionDescriptionAutomationName"));
        AutomationProperties.SetAutomationId(descriptionText, "InsertFunctionDescriptionText");

        void RefreshList()
        {
            var filtered = InsertFunctionCatalogPlanner.FilterCatalog(
                catalog,
                categoryBox.SelectedItem?.ToString(),
                searchBox.Text,
                _insertFunctionMostRecentlyUsed);
            listBox.ItemsSource = filtered.Select(entry => entry.Name).ToArray();
            listBox.SelectedIndex = filtered.Count > 0 ? 0 : -1;
        }

        InsertFunctionCatalogEntry? CurrentEntry() =>
            listBox.SelectedItem is string name
                ? catalog.FirstOrDefault(entry => entry.Name == name)
                : null;

        void UpdateHelp()
        {
            var entry = CurrentEntry();
            syntaxText.Text = entry is null ? "" : FormatFunctionSyntax(entry.Name);
            descriptionText.Text = entry?.Description ?? "";
        }

        searchBox.TextChanged += (_, _) => RefreshList();
        categoryBox.SelectionChanged += (_, _) => RefreshList();

        // WPF: go { Width = 64, Height = 24, Margin = (0, 0, 0, 6) } re-runs the search.
        var goButton = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("InsertFunction_Go")),
            Margin = new Thickness(0, 0, 0, 6),
        };
        ApplyFnButtonChrome(goButton, minWidth: 64);
        goButton.Width = 64;
        goButton.Height = 24;
        goButton.MinHeight = 24;
        goButton.MaxHeight = 24;
        AutomationProperties.SetName(goButton, UiText.CreateAutomationName(UiText.Get("InsertFunction_Go")));
        AutomationProperties.SetAutomationId(goButton, "InsertFunctionGoButton");
        goButton.Click += (_, _) => RefreshList();

        // WPF: help { Width = 146 } leads the button row and shows the selected function's help.
        var helpButton = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("InsertFunction_HelpOnThisFunction")),
        };
        ApplyFnButtonChrome(helpButton, minWidth: 146);
        helpButton.Width = 146;
        AutomationProperties.SetName(helpButton, UiText.CreateAutomationName(UiText.Get("InsertFunction_HelpOnThisFunction")));
        AutomationProperties.SetAutomationId(helpButton, "InsertFunctionHelpButton");
        helpButton.Click += (_, _) => RunGuarded(() => ShowFunctionHelpDialogAsync(
            dialog,
            UiText.Get("InsertFunction_FunctionHelpTitle"),
            BuildInsertFunctionHelpMessage(CurrentEntry())));
        listBox.SelectionChanged += (_, _) => UpdateHelp();

        // WPF: OK and Cancel are Width = 80, 8px apart, OK first.
        var okButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            MinWidth = 80,
            IsEnabled = false,
        };
        ApplyFnButtonChrome(okButton, minWidth: 80, isDefault: true);
        AutomationProperties.SetName(okButton, UiText.CreateAutomationName(UiText.Get("Common_Ok")));
        AutomationProperties.SetAutomationId(okButton, "InsertFunctionOkButton");

        var cancelButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            MinWidth = 80,
        };
        ApplyFnButtonChrome(cancelButton, minWidth: 80);
        AutomationProperties.SetName(cancelButton, UiText.CreateAutomationName(UiText.Get("Common_Cancel")));
        AutomationProperties.SetAutomationId(cancelButton, "InsertFunctionCancelButton");

        void Accept()
        {
            var entry = CurrentEntry();
            if (entry is null)
                return;

            result = entry;
            dialog.Close();
        }

        listBox.SelectionChanged += (_, _) => okButton.IsEnabled = CurrentEntry() is not null;
        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        listBox.DoubleTapped += (_, _) => Accept();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        // WPF: btnRow.Margin = (0, 8, 0, 0), docked to the bottom.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [helpButton, okButton, cancelButton],
            new Thickness(0, 8, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        // WPF: the syntax/help GroupBox carries Margin = (0, 8, 0, 0), Padding = 8 and sits
        // above the button row (a plain Window, so a black caption).
        var helpPanel = new GroupBox
        {
            Header = UiText.Get("InsertFunction_FormulaSyntaxAndHelp"),
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(8),
            Content = new StackPanel { Children = { syntaxText, descriptionText } },
        };
        ApplyPlainWindowGroupBoxChrome(helpPanel, InsertFunctionDialogChromeStyle);
        DockPanel.SetDock(helpPanel, Dock.Bottom);

        // WPF searchPanel: Grid { Margin = (0, 0, 0, 8) } with the labels (5px Label padding) in an
        // Auto column, the search box / category combo in the star column and the search button in a
        // trailing Auto column.
        var header = new AvaloniaGrid
        {
            Margin = new Thickness(0, 0, 0, 8),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto, MinHeight = 26 },
                new RowDefinition { Height = GridLength.Auto, MinHeight = 26 },
            },
        };
        var searchLabel = CreateInsertFunctionLabel("InsertFunction_SearchForAFunction");
        searchLabel.Margin = new Thickness(5, 0, 5, 6);
        header.Children.Add(searchLabel);
        AvaloniaGrid.SetColumn(searchBox, 1);
        header.Children.Add(searchBox);
        AvaloniaGrid.SetColumn(goButton, 2);
        header.Children.Add(goButton);
        var categoryLabel = CreateInsertFunctionLabel("InsertFunction_OrSelectACategory");
        AvaloniaGrid.SetRow(categoryLabel, 1);
        header.Children.Add(categoryLabel);
        AvaloniaGrid.SetRow(categoryBox, 1);
        AvaloniaGrid.SetColumn(categoryBox, 1);
        header.Children.Add(categoryBox);
        DockPanel.SetDock(header, Dock.Top);

        // WPF: listLabel { Padding = (0, 0, 0, 4) } docked above the list.
        var listLabel = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("InsertFunction_SelectAFunction")),
            FontSize = 12,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 4),
        };
        DockPanel.SetDock(listLabel, Dock.Top);

        // WPF: DockPanel { Margin = 12 } -- search panel, button row, help group, list label, list.
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children = { header, buttonRow, helpPanel, listLabel, listBox },
        };

        dialog.Opened += (_, _) =>
        {
            RefreshList();
            UpdateHelp();
            okButton.IsEnabled = CurrentEntry() is not null;
            searchBox.Focus();
            searchBox.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<string?> ShowFunctionArgumentsDialogAsync(InsertFunctionCatalogEntry function)
    {
        var arguments = FunctionArgumentCatalog.GetArgumentSpecs(function.Name);
        var argumentBoxes = new List<TextBox>();
        string? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("FunctionArguments_Title"),
            // WPF FunctionArgumentsDialog: Width = 520, Height = clamp(220 + 58 per argument, 300, 620)
            // (outer), ResizeMode.NoResize.
            Width = 520 - WpfNoResizeFrameWidth,
            Height = Math.Max(300, Math.Min(620, 220 + (arguments.Count * 58))) - WpfNoResizeClientFrameHeight,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FunctionArgumentsDialog");

        var previewText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(previewText, UiText.Get("FunctionArguments_FormulaResultAutomationName"));
        AutomationProperties.SetAutomationId(previewText, "FunctionArgumentsPreviewText");
        AutomationProperties.SetHelpText(previewText, UiText.Get("FunctionArguments_FormulaResultHelpText"));

        void UpdatePreview() =>
            previewText.Text = FunctionArgumentCatalog.BuildPreview(
                function.Name,
                argumentBoxes.Select(box => box.Text));

        // WPF AddArgumentRow: Grid { Margin = (0, 0, 0, 8) } with a 120px label column; the editor
        // (a 28px "..." picker docked left, 6px gap, then the box with Margin = (8, 0, 0, 2)) and the
        // 11px grey help line (8px left margin) share the star column.
        var argumentStack = new StackPanel();
        for (var argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            var argument = arguments[argumentIndex];
            var box = new TextBox { Margin = new Thickness(8, 0, 0, 2) };
            ApplyFnTextBoxChrome(box);
            box.TextChanged += (_, _) => UpdatePreview();
            argumentBoxes.Add(box);
            AutomationProperties.SetName(box, argument.Name);
            AutomationProperties.SetAutomationId(box, $"FunctionArgumentBox{argumentIndex}");

            var referencePicker = new Button
            {
                Content = "...",
                Width = 28,
                MinWidth = 28,
                Margin = new Thickness(0, 0, 6, 2),
                HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center,
            };
            ApplyFnButtonChrome(referencePicker, minWidth: 28);
            referencePicker.Padding = new Thickness(0);
            AutomationProperties.SetName(
                referencePicker,
                UiText.Format("FunctionArguments_SelectWorksheetReferenceAutomationNameFormat", argument.Name));
            AutomationProperties.SetAutomationId(referencePicker, $"FunctionArgumentReferencePicker{argumentIndex}");

            var editorRow = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            editorRow.Children.Add(referencePicker);
            AvaloniaGrid.SetColumn(box, 1);
            editorRow.Children.Add(box);
            AttachDialogRangePicker(
                dialog,
                referencePicker,
                box,
                "range.function-argument.reference");

            var argumentLabel = new TextBlock
            {
                Text = $"{argument.Name}:",
                FontSize = 12,
                Foreground = HeaderForeground,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = AvaloniaVerticalAlignment.Center,
            };
            var argumentHelp = new TextBlock
            {
                Text = argument.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = Brush(109, 109, 109),
                Margin = new Thickness(8, 0, 0, 0),
            };
            var argumentRow = new AvaloniaGrid
            {
                Margin = new Thickness(0, 0, 0, 8),
                ColumnDefinitions = new ColumnDefinitions("120,*"),
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            argumentRow.Children.Add(argumentLabel);
            AvaloniaGrid.SetColumn(editorRow, 1);
            argumentRow.Children.Add(editorRow);
            AvaloniaGrid.SetRow(argumentHelp, 1);
            AvaloniaGrid.SetColumn(argumentHelp, 1);
            argumentRow.Children.Add(argumentHelp);
            argumentStack.Children.Add(argumentRow);
        }

        var okButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            MinWidth = 76,
        };
        ApplyFnButtonChrome(okButton, minWidth: 76, isDefault: true);
        AutomationProperties.SetName(okButton, UiText.CreateAutomationName(UiText.Get("Common_Ok")));
        AutomationProperties.SetAutomationId(okButton, "FunctionArgumentsOkButton");

        var cancelButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Cancel")),
            MinWidth = 76,
        };
        ApplyFnButtonChrome(cancelButton, minWidth: 76);
        AutomationProperties.SetName(cancelButton, UiText.CreateAutomationName(UiText.Get("Common_Cancel")));
        AutomationProperties.SetAutomationId(cancelButton, "FunctionArgumentsCancelButton");

        void Accept()
        {
            result = FunctionArgumentCatalog.BuildPreview(
                function.Name,
                argumentBoxes.Select(box => box.Text));
            dialog.Close();
        }

        okButton.Click += (_, _) => Accept();
        cancelButton.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                dialog.Close();
            }
        };

        // WPF CreateButtonRow: help { Width = 146 } first; its message lists the function and every
        // argument with its description.
        var helpButton = new Button
        {
            Content = StripDisplayMnemonic(UiText.Get("FunctionArguments_HelpButton")),
        };
        ApplyFnButtonChrome(helpButton, minWidth: 146);
        helpButton.Width = 146;
        AutomationProperties.SetName(helpButton, UiText.CreateAutomationName(UiText.Get("FunctionArguments_HelpButton")));
        AutomationProperties.SetAutomationId(helpButton, "FunctionArgumentsHelpButton");
        helpButton.Click += (_, _) => RunGuarded(() => ShowFunctionHelpDialogAsync(
            dialog,
            UiText.Get("FunctionArguments_HelpTitle"),
            BuildFunctionArgumentsHelpMessage(function.Name, arguments)));

        // WPF CreateButtonRow: Margin = (0, 12, 0, 0); help, OK then Cancel (76px wide), 8px apart.
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [helpButton, okButton, cancelButton],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        UpdatePreview();
        // WPF: DockPanel { Margin = 12 }; the body stacks the function name (4px), its grey
        // description (12px), the argument rows, the formula-result label (12px above, 2px below) and the
        // semi-bold preview.
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = function.Name,
                                FontSize = 12,
                                FontWeight = FontWeight.SemiBold,
                                Foreground = HeaderForeground,
                                Margin = new Thickness(0, 0, 0, 4),
                            },
                            new TextBlock
                            {
                                Text = function.Description,
                                FontSize = 12,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = Brush(109, 109, 109),
                                Margin = new Thickness(0, 0, 0, 12),
                            },
                            argumentStack,
                            new TextBlock
                            {
                                Text = UiText.Get("FunctionArguments_FormulaResultLabel"),
                                FontSize = 12,
                                Foreground = HeaderForeground,
                                Margin = new Thickness(0, 12, 0, 2),
                            },
                            previewText,
                        },
                    },
                },
            },
        };

        dialog.Opened += (_, _) =>
        {
            var first = argumentBoxes.Count > 0 ? argumentBoxes[0] : null;
            first?.Focus();
            first?.SelectAll();
        };

        await dialog.ShowDialog(this);
        return result;
    }

    // WPF InsertFunctionDialog.ShowFunctionHelp: a prompt to pick a function when nothing is selected,
    // otherwise the function with its description. The resource keeps its line breaks as a
    // backtick-n marker, which is expanded here.
    internal static string BuildInsertFunctionHelpMessage(InsertFunctionCatalogEntry? entry)
    {
        var message = entry is null
            ? UiText.Get("InsertFunction_SelectFunctionForHelpMessage")
            : UiText.Format("InsertFunction_FunctionHelpMessage", entry.Name, entry.Description);
        return message.Replace("`n", Environment.NewLine, StringComparison.Ordinal);
    }

    // WPF FunctionArgumentsDialog.ShowFunctionHelp.
    internal static string BuildFunctionArgumentsHelpMessage(
        string functionName,
        IReadOnlyList<FunctionArgumentSpec> arguments) =>
        $"{functionName}(){Environment.NewLine}{Environment.NewLine}" +
        string.Join(
            Environment.NewLine,
            arguments.Select(argument => $"{argument.Name}: {argument.Description}"));

    /// <summary>The information box WPF shows for the function help buttons, owned by the dialog.</summary>
    private static async Task ShowFunctionHelpDialogAsync(Window owner, string title, string message)
    {
        var okButton = new Button
        {
            Content = UiText.CreateAutomationName(UiText.Get("Common_Ok")),
            IsDefault = true,
            IsCancel = true,
        };
        ApplyFnButtonChrome(okButton, minWidth: 80, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "FunctionHelpOkButton");

        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = HeaderForeground,
        };
        AutomationProperties.SetAutomationId(messageText, "FunctionHelpMessageText");

        var helpDialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            FontFamily = FormulaBarFontFamily,
            FontSize = 12,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    messageText,
                    AvaloniaCompactDialogChrome.CreateActionRow([okButton]),
                },
            },
        };
        AutomationProperties.SetAutomationId(helpDialog, "FunctionHelpDialog");
        okButton.Click += (_, _) => helpDialog.Close();
        helpDialog.Opened += (_, _) => okButton.Focus();
        await helpDialog.ShowDialog(owner);
    }

    private void InsertComposedFunctionFormula(string functionName, string formula)
    {
        BeginFormulaEdit(_session.ActiveCell, formula);
        if (!CommitFormulaBox())
            return;

        _insertFunctionMostRecentlyUsed =
            InsertFunctionCatalogPlanner.UpdateMostRecentlyUsed(_insertFunctionMostRecentlyUsed, functionName);
    }

    private static string FormatFunctionSyntax(string functionName)
    {
        var specs = FunctionArgumentCatalog.GetArgumentSpecs(functionName);
        var parameters = specs.Select(spec => spec.Optional ? $"[{spec.Name}]" : spec.Name);
        return $"{functionName.ToUpperInvariant()}({string.Join(", ", parameters)})";
    }

    private static TextBlock CreateInsertFunctionLabel(string resourceKey) =>
        new()
        {
            Text = StripDisplayMnemonic(UiText.Get(resourceKey)),
            FontSize = 12,
            Foreground = HeaderForeground,
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(5, 0, 5, 0),
        };

    private static string ResolveInsertFunctionLabel(string resourceKey) =>
        StripDisplayMnemonic(UiText.Get(resourceKey)).TrimEnd(':');

    // ── Visual chrome helpers (InsertFunction / FunctionArguments dialogs) ────

    /// <summary>
    /// Applies standard Function-dialog button chrome (Height=24, FontSize=12, white background, grey/blue border).
    /// <paramref name="minWidth"/> sets MinWidth; <paramref name="isDefault"/> uses blue border for the OK button.
    /// </summary>
    private static void ApplyFnButtonChrome(Button button, double minWidth = 80, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, InsertFunctionDialogChromeStyle, minWidth, isDefault);

    /// <summary>
    /// Applies standard Function-dialog text-box chrome (Height=24, Padding=(4,1), FontSize=12, grey border).
    /// </summary>
    private static void ApplyFnTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, InsertFunctionDialogChromeStyle);

    /// <summary>
    /// Applies standard Function-dialog combo-box chrome (Height=24, Padding=(5,0,4,0), FontSize=12, grey border).
    /// </summary>
    private static void ApplyFnComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, InsertFunctionDialogChromeStyle);

    /// <summary>
    /// Applies standard Function-dialog list-box row chrome (MinHeight=24 per row, FontSize=12).
    /// </summary>
    private static void ApplyFnListBoxStyle(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, InsertFunctionDialogChromeStyle);
}
