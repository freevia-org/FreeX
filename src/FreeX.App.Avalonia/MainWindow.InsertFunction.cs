using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Avalonia.Dialogs;
using FreeX.App.Presentation.Dialogs;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Insert Function + Function Arguments dialogs for the Avalonia/macOS shell. Insert Function lets the
/// user search/filter the built-in catalog and pick a function; choosing one opens Function Arguments,
/// which composes <c>=FUNC(a, b)</c> from one labeled box per argument (with a live preview) and commits
/// it into the active cell through the same formula edit/commit path the formula bar uses.
/// </summary>
public sealed partial class MainWindow
{
    // The Most Recently Used list shown in the Insert Function category dropdown. Promoted whenever a
    // function is inserted (most recent first) and seeded from the catalog defaults.
    private IReadOnlyList<string> _insertFunctionMostRecentlyUsed = InsertFunctionCatalog.DefaultMostRecentlyUsed;

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
        var catalog = InsertFunctionCatalog.BuildCatalog();
        InsertFunctionCatalogEntry? result = null;

        var dialog = new Window
        {
            Title = "Insert Function",
            Width = 560,
            Height = 470,
            MinWidth = 460,
            MinHeight = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "InsertFunctionDialog");

        var searchBox = new TextBox { MinWidth = 260 };
        AutomationProperties.SetName(searchBox, "Search for a function");
        AutomationProperties.SetAutomationId(searchBox, "InsertFunctionSearchBox");
        AutomationProperties.SetHelpText(searchBox, "Type to filter functions by name or description.");

        var categoryBox = new ComboBox
        {
            ItemsSource = InsertFunctionCatalog.BuildCategoryChoices(catalog),
            SelectedItem = InsertFunctionCatalog.MostRecentlyUsedCategory,
            MinWidth = 220,
        };
        AutomationProperties.SetName(categoryBox, "Or select a category");
        AutomationProperties.SetAutomationId(categoryBox, "InsertFunctionCategoryBox");

        var listBox = new ListBox { MinHeight = 160 };
        AutomationProperties.SetName(listBox, "Select a function");
        AutomationProperties.SetAutomationId(listBox, "InsertFunctionListBox");

        var syntaxText = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(syntaxText, "Function syntax");
        AutomationProperties.SetAutomationId(syntaxText, "InsertFunctionSyntaxText");

        var descriptionText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            Foreground = Brush(96, 96, 96),
        };
        AutomationProperties.SetName(descriptionText, "Function description");
        AutomationProperties.SetAutomationId(descriptionText, "InsertFunctionDescriptionText");

        void RefreshList()
        {
            var filtered = InsertFunctionCatalog.FilterCatalog(
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
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
            IsEnabled = false,
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "InsertFunctionOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, okButton },
        };
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
                new TextBlock { Text = "Select a function:" },
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
            Title = "Function Arguments",
            Width = 520,
            Height = Math.Max(260, Math.Min(620, 200 + (arguments.Count * 60))),
            MinWidth = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "FunctionArgumentsDialog");

        var previewText = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(previewText, "Formula result");
        AutomationProperties.SetAutomationId(previewText, "FunctionArgumentsPreviewText");
        AutomationProperties.SetHelpText(previewText, "The formula inserted when you choose OK.");

        void UpdatePreview() =>
            previewText.Text = FunctionArgumentCatalog.BuildPreview(
                function.Name,
                argumentBoxes.Select(box => box.Text));

        var argumentStack = new StackPanel { Spacing = 8 };
        foreach (var argument in arguments)
        {
            var box = new TextBox();
            box.TextChanged += (_, _) => UpdatePreview();
            argumentBoxes.Add(box);
            AutomationProperties.SetName(box, argument.Name);

            var label = argument.Optional ? $"{argument.Name} (optional):" : $"{argument.Name}:";
            argumentStack.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = label, FontWeight = FontWeight.Medium },
                    box,
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
            Content = "OK",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(okButton, "OK");
        AutomationProperties.SetAutomationId(okButton, "FunctionArgumentsOkButton");

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 84,
            Padding = new Thickness(10, 4),
        };
        AutomationProperties.SetName(cancelButton, "Cancel");
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

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, okButton },
        };
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
                            new TextBlock { Text = function.Name, FontWeight = FontWeight.SemiBold },
                            new TextBlock
                            {
                                Text = function.Description,
                                TextWrapping = TextWrapping.Wrap,
                                Foreground = Brush(96, 96, 96),
                            },
                            argumentStack,
                            new TextBlock { Text = "Formula result:", Margin = new Thickness(0, 8, 0, 0) },
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
            InsertFunctionCatalog.UpdateMostRecentlyUsed(_insertFunctionMostRecentlyUsed, functionName);
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
                new TextBlock { Text = label },
                control,
            },
        };
}
