using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
        var options = CreateOptions();
        var selected = NormalizePreset(currentPreset);
        return new ShapeEffectsDialogPlan(options, selected);
    }

    public static DrawingShapeEffectPreset NormalizePreset(DrawingShapeEffectPreset preset) =>
        Enum.IsDefined(preset)
            ? preset
            : DrawingShapeEffectPreset.None;

    public static IReadOnlyList<ShapeEffectsDialogOption> CreateOptions() =>
    [
        new(
            DrawingShapeEffectPreset.None,
            UiText.Get("ShapeEffects_None"),
            UiText.Get("ShapeEffects_NoneDescription")),
        new(
            DrawingShapeEffectPreset.Shadow,
            UiText.Get("ShapeEffects_Shadow"),
            UiText.Get("ShapeEffects_ShadowDescription")),
        new(
            DrawingShapeEffectPreset.InnerShadow,
            UiText.Get("ShapeEffects_InnerShadow"),
            UiText.Get("ShapeEffects_InnerShadowDescription")),
        new(
            DrawingShapeEffectPreset.Reflection,
            UiText.Get("ShapeEffects_Reflection"),
            UiText.Get("ShapeEffects_ReflectionDescription")),
        new(
            DrawingShapeEffectPreset.Glow,
            UiText.Get("ShapeEffects_Glow"),
            UiText.Get("ShapeEffects_GlowDescription")),
        new(
            DrawingShapeEffectPreset.SoftEdges,
            UiText.Get("ShapeEffects_SoftEdges"),
            UiText.Get("ShapeEffects_SoftEdgesDescription")),
        new(
            DrawingShapeEffectPreset.Bevel,
            UiText.Get("ShapeEffects_Bevel"),
            UiText.Get("ShapeEffects_BevelDescription")),
        new(
            DrawingShapeEffectPreset.ThreeDRotation,
            UiText.Get("ShapeEffects_ThreeDRotation"),
            UiText.Get("ShapeEffects_ThreeDRotationDescription"))
    ];
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
        if (!Enum.IsDefined(preset))
            return false;

        result = new ShapeEffectsDialogResult(preset);
        return true;
    }

    private ShapeEffectsDialogOption FindOption(DrawingShapeEffectPreset preset) =>
        _options.FirstOrDefault(option => option.Preset == preset) ?? _options[0];

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
