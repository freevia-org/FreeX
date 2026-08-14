namespace FreeP.App.Compositor;

/// <summary>
/// Maps the three native input control categories used by presentation dialogs to the
/// renderer-neutral field value contract. Framework adapters provide property access only.
/// </summary>
public sealed class PresentationDialogNativeBinding<TControl, TText, TChoice, TToggle>
    where TControl : class
    where TText : class
    where TChoice : class
    where TToggle : class
{
    private readonly Func<TText, string?> _readText;
    private readonly Action<TText, string> _writeText;
    private readonly Func<TChoice, int> _readSelectedIndex;
    private readonly Action<TChoice, int> _writeSelectedIndex;
    private readonly Func<TToggle, bool?> _readChecked;
    private readonly Action<TToggle, bool?> _writeChecked;

    public PresentationDialogNativeBinding(
        Func<TText, string?> readText,
        Action<TText, string> writeText,
        Func<TChoice, int> readSelectedIndex,
        Action<TChoice, int> writeSelectedIndex,
        Func<TToggle, bool?> readChecked,
        Action<TToggle, bool?> writeChecked)
    {
        _readText = readText ?? throw new ArgumentNullException(nameof(readText));
        _writeText = writeText ?? throw new ArgumentNullException(nameof(writeText));
        _readSelectedIndex = readSelectedIndex ?? throw new ArgumentNullException(nameof(readSelectedIndex));
        _writeSelectedIndex = writeSelectedIndex ?? throw new ArgumentNullException(nameof(writeSelectedIndex));
        _readChecked = readChecked ?? throw new ArgumentNullException(nameof(readChecked));
        _writeChecked = writeChecked ?? throw new ArgumentNullException(nameof(writeChecked));
    }

    public PresentationDialogFieldValue CaptureValue(TControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control switch
        {
            TText text => new(Text: _readText(text) ?? string.Empty),
            TChoice choice => new(SelectedIndex: _readSelectedIndex(choice)),
            TToggle toggle => new(IsChecked: _readChecked(toggle)),
            _ => throw Unsupported(control),
        };
    }

    public void ApplyValue(TControl control, PresentationDialogFieldValue value)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(value);

        switch (control)
        {
            case TText text:
                _writeText(text, value.Text ?? string.Empty);
                break;
            case TChoice choice:
                _writeSelectedIndex(choice, value.SelectedIndex);
                break;
            case TToggle toggle:
                _writeChecked(toggle, value.IsChecked);
                break;
            default:
                throw Unsupported(control);
        }
    }

    private static InvalidOperationException Unsupported(TControl control) =>
        new($"Unsupported presentation dialog control: {control.GetType().Name}.");
}

/// <summary>Applies chart field plans through the three native input control categories.</summary>
public sealed class ChartOptionsDialogNativeFieldBinding<TControl, TText, TChoice, TToggle>
    where TControl : class
    where TText : class
    where TChoice : class
    where TToggle : class
{
    private readonly Action<TControl, bool> _setEnabled;
    private readonly Action<TText, string> _setText;
    private readonly Action<TChoice, IReadOnlyList<string>> _setChoices;
    private readonly Action<TChoice, int> _setSelectedIndex;
    private readonly Action<TToggle, bool?> _setChecked;

    public ChartOptionsDialogNativeFieldBinding(
        Action<TControl, bool> setEnabled,
        Action<TText, string> setText,
        Action<TChoice, IReadOnlyList<string>> setChoices,
        Action<TChoice, int> setSelectedIndex,
        Action<TToggle, bool?> setChecked)
    {
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _setChoices = setChoices ?? throw new ArgumentNullException(nameof(setChoices));
        _setSelectedIndex = setSelectedIndex ?? throw new ArgumentNullException(nameof(setSelectedIndex));
        _setChecked = setChecked ?? throw new ArgumentNullException(nameof(setChecked));
    }

    public void ApplyPlan(TControl control, ChartOptionsDialogFieldPlan field)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(field);
        _setEnabled(control, field.IsEnabled);
        switch (control)
        {
            case TText text:
                _setText(text, field.Text);
                break;
            case TChoice choice:
                _setChoices(choice, field.ChoiceLabels);
                _setSelectedIndex(choice, field.SelectedIndex);
                break;
            case TToggle toggle:
                _setChecked(toggle, field.IsChecked);
                break;
        }
    }
}

public static class PresentationDialogNativeSemanticBinding
{
    public static void Apply<TControl, TField>(
        TControl control,
        PresentationDialogFieldPlan<TField> field,
        Action<TControl, string, string, string?> writeSemantic,
        string automationSuffix = "")
        where TControl : class
        where TField : notnull
    {
        ArgumentNullException.ThrowIfNull(field);
        Apply(
            control,
            field.AccessibleName,
            field.AutomationId + automationSuffix,
            field.HelpText,
            writeSemantic);
    }

    public static void Apply<TControl>(
        TControl control,
        string? accessibleName,
        string automationId,
        string? helpText,
        Action<TControl, string, string, string?> writeSemantic)
        where TControl : class
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(writeSemantic);
        writeSemantic(control, accessibleName ?? string.Empty, automationId, helpText);
    }
}

/// <summary>Shared accessibility session and projection dispatch for native pane writers.</summary>
public sealed class PresentationPaneAccessibilityNativeSession<TControl>
    where TControl : class
{
    private readonly PresentationPaneAccessibilitySession _session = new();
    private readonly Action<TControl, PresentationPaneAccessibilityPaneProjection> _writePane;

    public PresentationPaneAccessibilityNativeSession(
        Action<TControl, PresentationPaneAccessibilityPaneProjection> writePane)
    {
        _writePane = writePane ?? throw new ArgumentNullException(nameof(writePane));
    }

    public void ApplyPane(
        TControl control,
        string paneId,
        bool isVisible,
        int itemCount = 0,
        int selectedIndex = -1) =>
        _writePane(control, _session.UpdatePane(paneId, isVisible, itemCount, selectedIndex));

    public IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> BuildSnapshot() =>
        _session.BuildSnapshot();

    public string SerializeSnapshot() => _session.SerializeSnapshot();

    public static void ApplyPaneMetadata(
        TControl control,
        string paneId,
        bool isVisible,
        int itemCount,
        int selectedIndex,
        Action<TControl, PresentationPaneAccessibilityPaneProjection> writePane) =>
        writePane(
            control,
            PresentationPaneAccessibilityPlanner.ProjectPane(
                paneId,
                isVisible,
                itemCount,
                selectedIndex));

    public static void ApplyItem(
        TControl control,
        PresentationPaneAccessibilityItemPlan plan,
        Action<TControl, PresentationPaneAccessibilityItemProjection> writeItem) =>
        writeItem(control, PresentationPaneAccessibilityPlanner.ProjectItem(plan));
}
