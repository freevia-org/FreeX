namespace FreeP.App.Compositor;

/// <summary>
/// Owns the renderer-neutral mapping between native text, choice, and toggle controls
/// and <see cref="PresentationDialogFieldValue"/>. Renderers provide only native property accessors.
/// </summary>
public sealed class PresentationDialogControlValueBridge<
    TControl,
    TTextControl,
    TChoiceControl,
    TToggleControl>
    where TControl : class
    where TTextControl : TControl
    where TChoiceControl : TControl
    where TToggleControl : TControl
{
    private readonly Func<TTextControl, string?> _getText;
    private readonly Action<TTextControl, string> _setText;
    private readonly Func<TChoiceControl, int> _getSelectedIndex;
    private readonly Action<TChoiceControl, int> _setSelectedIndex;
    private readonly Func<TToggleControl, bool?> _getIsChecked;
    private readonly Action<TToggleControl, bool?> _setIsChecked;

    public PresentationDialogControlValueBridge(
        Func<TTextControl, string?> getText,
        Action<TTextControl, string> setText,
        Func<TChoiceControl, int> getSelectedIndex,
        Action<TChoiceControl, int> setSelectedIndex,
        Func<TToggleControl, bool?> getIsChecked,
        Action<TToggleControl, bool?> setIsChecked)
    {
        _getText = getText ?? throw new ArgumentNullException(nameof(getText));
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _getSelectedIndex = getSelectedIndex ?? throw new ArgumentNullException(nameof(getSelectedIndex));
        _setSelectedIndex = setSelectedIndex ?? throw new ArgumentNullException(nameof(setSelectedIndex));
        _getIsChecked = getIsChecked ?? throw new ArgumentNullException(nameof(getIsChecked));
        _setIsChecked = setIsChecked ?? throw new ArgumentNullException(nameof(setIsChecked));
    }

    public PresentationDialogFieldValue Capture(TControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (control is TTextControl textControl)
            return new PresentationDialogFieldValue(Text: _getText(textControl) ?? string.Empty);
        if (control is TChoiceControl choiceControl)
            return new PresentationDialogFieldValue(SelectedIndex: _getSelectedIndex(choiceControl));
        if (control is TToggleControl toggleControl)
            return new PresentationDialogFieldValue(IsChecked: _getIsChecked(toggleControl));

        throw UnsupportedControl(control);
    }

    public void Apply(TControl control, PresentationDialogFieldValue value)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(value);

        if (control is TTextControl textControl)
        {
            _setText(textControl, value.Text ?? string.Empty);
            return;
        }

        if (control is TChoiceControl choiceControl)
        {
            _setSelectedIndex(choiceControl, value.SelectedIndex);
            return;
        }

        if (control is TToggleControl toggleControl)
        {
            _setIsChecked(toggleControl, value.IsChecked);
            return;
        }

        throw UnsupportedControl(control);
    }

    private static InvalidOperationException UnsupportedControl(TControl control) => new(
        $"Unsupported presentation dialog control: {control.GetType().Name}.");
}
