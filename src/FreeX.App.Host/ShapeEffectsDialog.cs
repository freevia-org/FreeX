using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record ShapeEffectsDialogOption(
    DrawingShapeEffectPreset Preset,
    string Label,
    string Description);

internal sealed record ShapeEffectsDialogPlan(
    IReadOnlyList<ShapeEffectsDialogOption> Options,
    DrawingShapeEffectPreset SelectedPreset);

internal static class ShapeEffectsDialogPlanner
{
    public static ShapeEffectsDialogPlan CreatePlan(DrawingShapeEffectPreset currentPreset)
    {
        var plan = ShapeEffectsPlanner.CreatePlan(currentPreset);
        return new ShapeEffectsDialogPlan(
            plan.Options.Select(ToDialogOption).ToArray(),
            plan.SelectedPreset);
    }

    public static DrawingShapeEffectPreset NormalizePreset(DrawingShapeEffectPreset preset) =>
        ShapeEffectsPlanner.NormalizePreset(preset);

    public static IReadOnlyList<ShapeEffectsDialogOption> CreateOptions() =>
        ShapeEffectsPlanner.CreateOptions()
            .Select(ToDialogOption)
            .ToArray();

    private static ShapeEffectsDialogOption ToDialogOption(ShapeEffectsPlanner.ShapeEffectOption option) =>
        new(option.Preset, UiText.Get(option.LabelKey), UiText.Get(option.DescriptionKey));
}

public sealed record ShapeEffectsDialogResult(DrawingShapeEffectPreset Preset);

public sealed class ShapeEffectsDialog : Window
{
    private readonly ComboBox _effectBox = new();
    private readonly TextBlock _descriptionText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly IReadOnlyList<ShapeEffectsDialogOption> _options;

    public ShapeEffectsDialogResult Result { get; private set; }

    public ShapeEffectsDialog(DrawingShapeEffectPreset currentPreset)
    {
        var plan = ShapeEffectsDialogPlanner.CreatePlan(currentPreset);
        _options = plan.Options;
        Result = new ShapeEffectsDialogResult(plan.SelectedPreset);

        Title = UiText.Get("ShapeEffects_Title");
        Width = 380;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _effectBox.ItemsSource = _options;
        _effectBox.DisplayMemberPath = nameof(ShapeEffectsDialogOption.Label);
        _effectBox.SelectedItem = FindOption(plan.SelectedPreset);
        _effectBox.SelectionChanged += (_, _) => UpdateDescription();
        AutomationProperties.SetName(_effectBox, UiText.Get("ShapeEffects_EffectAutomationName"));
        AutomationProperties.SetAutomationId(_effectBox, "ShapeEffectsPresetBox");
        AutomationProperties.SetHelpText(_effectBox, UiText.Get("ShapeEffects_EffectHelpText"));
        AutomationProperties.SetName(_descriptionText, UiText.Get("ShapeEffects_DescriptionAutomationName"));
        AutomationProperties.SetAutomationId(_descriptionText, "ShapeEffectsDescriptionText");

        Content = CreateContent();
        UpdateDescription();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static bool TryCreateResult(
        DrawingShapeEffectPreset preset,
        out ShapeEffectsDialogResult result)
    {
        result = new ShapeEffectsDialogResult(DrawingShapeEffectPreset.None);
        if (ShapeEffectsDialogPlanner.NormalizePreset(preset) != preset)
            return false;

        result = new ShapeEffectsDialogResult(preset);
        return true;
    }

    private ShapeEffectsDialogOption FindOption(DrawingShapeEffectPreset preset)
    {
        foreach (var option in _options)
        {
            if (option.Preset == preset)
                return option;
        }

        return _options[0];
    }

    private ShapeEffectsDialogOption SelectedOption =>
        _effectBox.SelectedItem as ShapeEffectsDialogOption ?? _options[0];

    private void Accept()
    {
        Result = new ShapeEffectsDialogResult(SelectedOption.Preset);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _effectBox.Focus();
        Keyboard.Focus(_effectBox);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new Label
        {
            Content = UiText.Get("ShapeEffects_EffectLabel"),
            Target = _effectBox,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 4)
        });

        _effectBox.Margin = new Thickness(0, 0, 0, 10);
        stack.Children.Add(_effectBox);

        _descriptionText.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_descriptionText);
        stack.Children.Add(DialogButtonRowFactory.Create(Accept, 72));
        return stack;
    }

    private void UpdateDescription()
    {
        _descriptionText.Text = SelectedOption.Description;
    }
}
