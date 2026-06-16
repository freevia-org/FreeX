namespace FreeX.App.Services;

/// <summary>
/// Generates Excel-style default workbook names (<c>Book1</c>, <c>Book2</c>, …) for the running
/// session. The startup workbook consumes <c>Book1</c>, so the first <see cref="Next"/> call
/// returns <c>Book2</c>. Registered as a singleton so every File &gt; New across the session keeps
/// advancing the counter instead of repeatedly producing <c>Book1</c> (Issue 121).
/// <para>
/// UI-thread affine; all calls happen on the dispatcher thread, matching the rest of the
/// workbook-lifecycle state.
/// </para>
/// </summary>
public sealed class NewWorkbookNameSequence
{
    // The startup workbook is Book1, so the last-issued number starts at 1.
    private int _lastIssuedNumber = 1;

    /// <summary>The 1-based number most recently issued (1 == the startup Book1).</summary>
    public int LastIssuedNumber => _lastIssuedNumber;

    /// <summary>Advances the sequence and returns the next default workbook name (e.g. <c>Book2</c>).</summary>
    public string Next() => WorkbookFactory.DefaultWorkbookNamePrefix + ++_lastIssuedNumber;
}
