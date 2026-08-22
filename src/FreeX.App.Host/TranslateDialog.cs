using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Free.Shared.Shell.Wpf;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// WPF surface for Review ▸ Translate. Translation remains intentionally manual: the portable
/// planner validates the user's entry and the owning window performs the protected, undoable write.
/// </summary>
public sealed class TranslateDialog : DialogWindow
{
    private readonly ComboBox _fromLanguageBox = new();
    private readonly ComboBox _toLanguageBox = new();
    private readonly TextBox _translationBox = new();
    private readonly TextBox _targetBox = new();

    public TranslateDialog(CellAddress source, string sourceText)
    {
        Title = UiText.Get("WfTranslate_Title");
        Width = 440;
        Height = 470;
        MinWidth = 400;
        MinHeight = 420;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(16) };
        var insert = new Button
        {
            Content = UiText.Get("WfTranslate_InsertButton"),
            MinWidth = 130,
            IsDefault = true,
        };
        AutomationProperties.SetName(insert, UiText.Get("WfTranslate_InsertButton"));
        AutomationProperties.SetAutomationId(insert, "WfTranslateInsertButton");
        insert.Click += (_, _) => Accept();

        var close = new Button
        {
            Content = UiText.Get("WfTranslate_CloseButton"),
            MinWidth = 90,
            IsCancel = true,
        };
        AutomationProperties.SetName(close, UiText.Get("WfTranslate_CloseButton"));
        AutomationProperties.SetAutomationId(close, "WfTranslateCloseButton");

        var actions = DialogButtonRowFactory.Create(insert, close, new Thickness(0, 12, 0, 0));
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var body = new StackPanel();
        root.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = UiText.Get("WfTranslate_ManualNote"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = SystemColors.GrayTextBrush,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var languageItems = TranslateDialogPlanner.Languages
            .Select(option => new TranslateLanguageItem(option.Code, UiText.Get(option.DisplayKey)))
            .ToList();
        ConfigureLanguageBox(
            _fromLanguageBox,
            languageItems,
            TranslateDialogPlanner.DefaultFromCode,
            "WfTranslateFromLanguage",
            UiText.Get("WfTranslate_FromLabel"));
        ConfigureLanguageBox(
            _toLanguageBox,
            languageItems,
            TranslateDialogPlanner.DefaultToCode,
            "WfTranslateToLanguage",
            UiText.Get("WfTranslate_ToLabel"));

        var languages = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddLabeledControl(languages, 0, UiText.Get("WfTranslate_FromLabel"), _fromLanguageBox);
        AddLabeledControl(languages, 2, UiText.Get("WfTranslate_ToLabel"), _toLanguageBox);
        body.Children.Add(languages);

        var sourceBox = new TextBox
        {
            Text = string.IsNullOrEmpty(sourceText) ? UiText.Get("WfTranslate_EmptyCell") : sourceText,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 46,
            MaxHeight = 84,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10),
        };
        AutomationProperties.SetName(sourceBox, UiText.Get("WfTranslate_SourceLabel"));
        AutomationProperties.SetAutomationId(sourceBox, "WfTranslateSourceBox");
        AddLabeledControl(body, UiText.Get("WfTranslate_SourceLabel"), sourceBox);

        _translationBox.AcceptsReturn = true;
        _translationBox.TextWrapping = TextWrapping.Wrap;
        _translationBox.MinHeight = 70;
        _translationBox.Height = 82;
        _translationBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _translationBox.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetName(_translationBox, UiText.Get("WfTranslate_TranslationLabel"));
        AutomationProperties.SetAutomationId(_translationBox, "WfTranslateTranslationBox");
        AddLabeledControl(body, UiText.Get("WfTranslate_TranslationLabel"), _translationBox);

        _targetBox.Text = TranslateDialogPlanner.SuggestTargetReference(source);
        AutomationProperties.SetName(_targetBox, UiText.Get("WfTranslate_TargetLabel"));
        AutomationProperties.SetAutomationId(_targetBox, "WfTranslateTargetBox");
        AddLabeledControl(body, UiText.Get("WfTranslate_TargetLabel"), _targetBox);

        Content = root;
        Loaded += (_, _) => DialogFocus.Focus(_translationBox);
    }

    public TranslateDialogResult Result { get; private set; } = new(
        string.Empty,
        string.Empty,
        TranslateDialogPlanner.DefaultFromCode,
        TranslateDialogPlanner.DefaultToCode);

    private void Accept()
    {
        Result = new TranslateDialogResult(
            _translationBox.Text,
            _targetBox.Text,
            (_fromLanguageBox.SelectedItem as TranslateLanguageItem)?.Code ?? TranslateDialogPlanner.DefaultFromCode,
            (_toLanguageBox.SelectedItem as TranslateLanguageItem)?.Code ?? TranslateDialogPlanner.DefaultToCode);
        DialogResult = true;
    }

    private static void ConfigureLanguageBox(
        ComboBox box,
        IReadOnlyList<TranslateLanguageItem> items,
        string defaultCode,
        string automationId,
        string automationName)
    {
        box.ItemsSource = items;
        box.DisplayMemberPath = nameof(TranslateLanguageItem.Label);
        box.SelectedItem = items.FirstOrDefault(item => item.Code == defaultCode) ?? items.FirstOrDefault();
        AutomationProperties.SetName(box, automationName);
        AutomationProperties.SetAutomationId(box, automationId);
    }

    private static void AddLabeledControl(Panel parent, string label, Control control)
    {
        parent.Children.Add(new Label
        {
            Content = label,
            Target = control,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        parent.Children.Add(control);
    }

    private static void AddLabeledControl(Grid parent, int column, string label, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new Label
        {
            Content = label,
            Target = control,
            Padding = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(control);
        Grid.SetColumn(panel, column);
        parent.Children.Add(panel);
    }

    private sealed record TranslateLanguageItem(string Code, string Label);
}

public sealed record TranslateDialogResult(
    string Translation,
    string TargetReference,
    string FromLanguageCode,
    string ToLanguageCode);
