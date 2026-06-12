using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// CI pin for the ClosedXML reflection path in XlsxFileAdapter.
///
/// XlsxFileAdapter uses a compiled delegate that calls the internal ClosedXML
/// XLStylizedBase.SetStyle(XLStyleValue, bool) method via reflection to apply
/// a complete immutable style key to a cell in a single call — much faster than
/// the ~15 individual property setter calls that the public API requires.
///
/// If a ClosedXML package bump renames or removes that method, the delegate silently
/// degrades to null and every save falls back to the slow per-property path.
/// This test pins that reflection to the current ClosedXML package version so any
/// future bump that breaks the fast path fails CI loudly instead of silently.
/// </summary>
public sealed class XlsxFileAdapterClosedXmlReflectionTests
{
    [Fact]
    public void ClosedXmlSetStyleDelegate_ResolvesSuccessfully()
    {
        // XlsxFileAdapter.ClosedXmlSetStyleDelegateResolved is an internal probe that returns
        // XlCellSetStyleValueAction is not null — i.e., reflection found XLCell.SetStyle(XLStyleValue, bool).
        // A ClosedXML package bump that removes or renames this method will fail here.
        XlsxFileAdapter.ClosedXmlSetStyleDelegateResolved.Should().BeTrue(
            "ClosedXML's XLStylizedBase.SetStyle(XLStyleValue, bool) must be resolvable via reflection " +
            "for the fast per-cell style-replay path; if this fails, update CreateXlCellSetStyleValueAction " +
            "in XlsxFileAdapter.cs to match the new ClosedXML API.");
    }
}
