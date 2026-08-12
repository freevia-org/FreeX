using Free.Shared.Ribbon.KeyTips;

namespace FreeX.App.Presentation.Ribbon;

public enum FreeXRibbonKeyTipInputScope
{
    None,
    TopLevel,
    Commands,
    Menu,
    Catalog,
    QuickAccess,
}

public enum FreeXRibbonLegacyKeyTipSequence
{
    None,
    DataFilter,
    EditPasteSpecial,
}

public enum FreeXRibbonKeyTipInputIntent
{
    Ignored,
    Route,
    WaitForContinuation,
    EnterLegacyDataFilter,
    EnterLegacyEditPasteSpecial,
    InvokeLegacyDataFilter,
    InvokeLegacyEditPasteSpecial,
    Cancel,
}

public readonly record struct FreeXRibbonKeyTipInputStep(
    FreeXRibbonKeyTipInputIntent Intent,
    FreeXRibbonKeyTipInputScope Scope,
    string Input)
{
    public bool Handled => Intent != FreeXRibbonKeyTipInputIntent.Ignored;

    public static FreeXRibbonKeyTipInputStep Ignored { get; } =
        new(FreeXRibbonKeyTipInputIntent.Ignored, FreeXRibbonKeyTipInputScope.None, "");
}

/// <summary>
/// Owns renderer-neutral key-tip input state and legacy access-key transitions. Native shells retain
/// key translation, live-target matching, overlay/focus behavior, and command invocation.
/// </summary>
public sealed class FreeXRibbonKeyTipInputSession
{
    public bool IsActive => Scope != FreeXRibbonKeyTipInputScope.None;
    public FreeXRibbonKeyTipInputScope Scope { get; private set; }
    public FreeXRibbonLegacyKeyTipSequence LegacySequence { get; private set; }
    public string Input { get; private set; } = "";

    public void Enter(FreeXRibbonKeyTipInputScope scope)
    {
        if (scope == FreeXRibbonKeyTipInputScope.None)
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Use Cancel to leave key-tip mode.");

        Scope = scope;
        LegacySequence = FreeXRibbonLegacyKeyTipSequence.None;
        Input = "";
    }

    public void EnterScope(FreeXRibbonKeyTipInputScope scope)
    {
        if (!IsActive)
            throw new InvalidOperationException("Key-tip mode must be active before changing scope.");
        if (scope == FreeXRibbonKeyTipInputScope.None)
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Use Cancel to leave key-tip mode.");

        Scope = scope;
        Input = "";
    }

    public void ResetInput() => Input = "";

    public void Cancel()
    {
        Scope = FreeXRibbonKeyTipInputScope.None;
        LegacySequence = FreeXRibbonLegacyKeyTipSequence.None;
        Input = "";
    }

    public FreeXRibbonKeyTipInputStep HandleEscape()
    {
        if (!IsActive)
            return FreeXRibbonKeyTipInputStep.Ignored;

        var scope = Scope;
        Cancel();
        return new(FreeXRibbonKeyTipInputIntent.Cancel, scope, "");
    }

    public FreeXRibbonKeyTipInputStep HandleToken(
        string? token,
        bool recognizeLegacyTopLevel = true)
    {
        if (!IsActive)
            return FreeXRibbonKeyTipInputStep.Ignored;

        var normalized = RibbonKeyTipText.Normalize(token);
        if (normalized is null)
            return CancelStep();

        if (LegacySequence != FreeXRibbonLegacyKeyTipSequence.None)
            return HandleLegacyContinuation(normalized);

        var isFirstToken = Input.Length == 0;
        Input += normalized;
        if (recognizeLegacyTopLevel &&
            isFirstToken &&
            Scope is FreeXRibbonKeyTipInputScope.TopLevel or FreeXRibbonKeyTipInputScope.Catalog)
        {
            if (string.Equals(Input, "D", StringComparison.OrdinalIgnoreCase))
                return BeginLegacy(FreeXRibbonLegacyKeyTipSequence.DataFilter);
            if (string.Equals(Input, "E", StringComparison.OrdinalIgnoreCase))
                return BeginLegacy(FreeXRibbonLegacyKeyTipSequence.EditPasteSpecial);
        }

        return new(FreeXRibbonKeyTipInputIntent.Route, Scope, Input);
    }

    private FreeXRibbonKeyTipInputStep BeginLegacy(FreeXRibbonLegacyKeyTipSequence sequence)
    {
        LegacySequence = sequence;
        Scope = FreeXRibbonKeyTipInputScope.Commands;
        Input = "";
        return new(
            sequence == FreeXRibbonLegacyKeyTipSequence.DataFilter
                ? FreeXRibbonKeyTipInputIntent.EnterLegacyDataFilter
                : FreeXRibbonKeyTipInputIntent.EnterLegacyEditPasteSpecial,
            Scope,
            Input);
    }

    private FreeXRibbonKeyTipInputStep HandleLegacyContinuation(string token)
    {
        Input += token;
        var expected = LegacySequence == FreeXRibbonLegacyKeyTipSequence.DataFilter ? "FF" : "S";
        if (!expected.StartsWith(Input, StringComparison.OrdinalIgnoreCase))
            return CancelStep();

        if (!string.Equals(expected, Input, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                FreeXRibbonKeyTipInputIntent.WaitForContinuation,
                Scope,
                Input);
        }

        var intent = LegacySequence == FreeXRibbonLegacyKeyTipSequence.DataFilter
            ? FreeXRibbonKeyTipInputIntent.InvokeLegacyDataFilter
            : FreeXRibbonKeyTipInputIntent.InvokeLegacyEditPasteSpecial;
        var completedInput = Input;
        var scope = Scope;
        Cancel();
        return new(intent, scope, completedInput);
    }

    private FreeXRibbonKeyTipInputStep CancelStep()
    {
        var input = Input;
        var scope = Scope;
        Cancel();
        return new(FreeXRibbonKeyTipInputIntent.Cancel, scope, input);
    }
}
