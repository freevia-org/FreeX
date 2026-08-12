using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Dialogs;

using AvaloniaGrid = Avalonia.Controls.Grid;

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

    private void InsertFunction() => _ = ShowInsertFunctionDialogAsync();

    private async Task ShowInsertFunctionDialogAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var selected = await ShowInsertFunctionPickerDialogAsync();
        if (selected is null)
            return;

        var formula = await ShowFunctionArgumentsDialogAsync(selected);
        if (formula is null)
            return;

        InsertComposedFunctionFormula(selected.Name, formula);
    }

    private async Task<InsertFunctionCatalogEntry?> ShowInsertFunctionPickerDialogAsync()
    {
        var catalog = InsertFunctionCatalogPlanner.BuildCatalog();
        InsertFunctionCatalogEntry? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("MainWindow_Content_InsertFunction"),
            Width = 560,
            Height = 470,
            MinWidth = 460,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertFunctionDialog");

        var searchBox = new TextBox { MinWidth = 260 };
        ApplyFnTextBoxChrome(searchBox);
        AutomationProperties.SetName(searchBox, "Search for a function");
        AutomationProperties.SetAutomationId(searchBox, "InsertFunctionSearchBox");
        AutomationProperties.SetHelpText(searchBox, "Type to filter functions by name or description.");

        var categoryBox = new ComboBox
        {
            ItemsSource = InsertFunctionCatalogPlanner.BuildCategoryChoices(catalog),
            SelectedItem = InsertFunctionCatalogPlanner.MostRecentlyUsedCategory,
            MinWidth = 220,
        };
        ApplyFnComboBoxChrome(categoryBox);
        AutomationProperties.SetName(categoryBox, "Or select a category");
        AutomationProperties.SetAutomationId(categoryBox, "InsertFunctionCategoryBox");

        var listBox = new ListBox { MinHeight = 160 };
        ApplyFnListBoxStyle(listBox);
        AutomationProperties.SetName(listBox, "Select a function");
        AutomationProperties.SetAutomationId(listBox, "InsertFunctionListBox");

        var syntaxText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(syntaxText, "Function syntax");
        AutomationProperties.SetAutomationId(syntaxText, "InsertFunctionSyntaxText");

        var descriptionText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            Foreground = Brush(96, 96, 96),
        };
        AutomationProperties.SetName(descriptionText, "Function description");
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
        listBox.SelectionChanged += (_, _) => UpdateHelp();

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

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [cancelButton, okButton],
            new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var helpPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { syntaxText, descriptionText },
        };
        DockPanel.SetDock(helpPanel, Dock.Bottom);

        var header = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CreateInsertFunctionField("Search for a function", searchBox),
                CreateInsertFunctionField("Or select a category", categoryBox),
                new TextBlock { Text = "Select a function:", FontSize = 12 },
            },
        };
        DockPanel.SetDock(header, Dock.Top);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children = { header, buttonRow, helpPanel, listBox },
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
            Width = 520,
            Height = Math.Max(300, Math.Min(620, 220 + (arguments.Count * 58))),
            MinWidth = 440,
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

        var argumentStack = new StackPanel { Spacing = 8 };
        for (var argumentIndex = 0; argumentIndex < arguments.Count; argumentIndex++)
        {
            var argument = arguments[argumentIndex];
            var box = new TextBox();
            ApplyFnTextBoxChrome(box);
            box.TextChanged += (_, _) => UpdatePreview();
            argumentBoxes.Add(box);
            AutomationProperties.SetName(box, argument.Name);
            AutomationProperties.SetAutomationId(box, $"FunctionArgumentBox{argumentIndex}");

            var referencePicker = new Button
            {
                Content = "...",
                Width = 30,
                MinWidth = 30,
                Margin = new Thickness(6, 0, 0, 0),
            };
            ApplyFnButtonChrome(referencePicker, minWidth: 30);
            AutomationProperties.SetName(referencePicker, $"Select worksheet reference for {argument.Name}");
            AutomationProperties.SetAutomationId(referencePicker, $"FunctionArgumentReferencePicker{argumentIndex}");

            var editorRow = new AvaloniaGrid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            editorRow.Children.Add(box);
            AvaloniaGrid.SetColumn(referencePicker, 1);
            editorRow.Children.Add(referencePicker);
            AttachDialogRangePicker(
                dialog,
                referencePicker,
                box,
                "range.function-argument.reference");

            var label = argument.Optional ? $"{argument.Name} (optional):" : $"{argument.Name}:";
            argumentStack.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.Medium },
                    editorRow,
                    new TextBlock
                    {
                        Text = argument.Description,
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 11,
                        Foreground = Brush(96, 96, 96),
                    },
                },
            });
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

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [cancelButton, okButton],
            new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        UpdatePreview();
        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock { Text = function.Name, FontSize = 12, FontWeight = FontWeight.SemiBold },
                            new TextBlock
                            {
                                Text = function.Description,
                                FontSize = 12,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = Brush(96, 96, 96),
                            },
                            argumentStack,
                            new TextBlock { Text = UiText.Get("FunctionArguments_FormulaResultLabel"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) },
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

    private static StackPanel CreateInsertFunctionField(string label, Control control) =>
        new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = StripDisplayMnemonic(label), FontSize = 12 },
                control,
            },
        };

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
