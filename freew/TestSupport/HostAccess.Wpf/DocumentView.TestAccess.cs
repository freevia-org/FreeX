namespace FreeW.App.Host.Editing;

public sealed partial class DocumentView
{
    internal static bool SuppressNativeSpellCheckForTests { get; set; }

    internal bool NativeSpellCheckEnabledForTest => SpellCheck.IsEnabled;

    static partial void ApplyNativeSpellCheckOverride(ref bool suppressed) =>
        suppressed = SuppressNativeSpellCheckForTests;
}
