using System.Globalization;
using System.Reflection;
using System.Threading;
using Xunit.Sdk;

[assembly: FreeX.Core.Formula.Tests.ExcelReferenceCulture]

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// The reference corpus uses US Excel formula, date and currency expectations. Establish that
/// oracle for each test, independent of the runner's regional settings. Tests that exercise another
/// locale still override this baseline with their method-local culture scopes.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ExcelReferenceCultureAttribute : BeforeAfterTestAttribute
{
    // An assembly attribute may be reused by concurrent invocations. Keep immutable saved frames
    // in each execution context so async tests and parallel collections cannot restore one another's
    // cultures. No process-wide default cultures or thread defaults are changed.
    private readonly AsyncLocal<SavedCulture?> _savedCulture = new();

    public override void Before(MethodInfo methodUnderTest)
    {
        var referenceCulture = CultureInfo.GetCultureInfo("en-US");
        _savedCulture.Value = new SavedCulture(CultureInfo.CurrentCulture, _savedCulture.Value);
        CultureInfo.CurrentCulture = referenceCulture;
    }

    public override void After(MethodInfo methodUnderTest)
    {
        var saved = _savedCulture.Value
            ?? throw new InvalidOperationException("The Excel reference culture scope was not initialized.");
        CultureInfo.CurrentCulture = saved.PreviousCulture;
        _savedCulture.Value = saved.Parent;
    }

    private sealed record SavedCulture(CultureInfo PreviousCulture, SavedCulture? Parent);
}
