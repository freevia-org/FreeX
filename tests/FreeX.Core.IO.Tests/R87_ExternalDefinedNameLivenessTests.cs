using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for R87-io-external-links-5-1:
/// <see cref="XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo"/> scanned for an
/// unanchored <c>'['</c> anywhere in a defined name's RefersTo text to decide whether it is an
/// external-workbook reference FreeX can never model. But <c>LoadDefinedNames</c> /
/// <c>LoadWorkbookDefinedNameFormulasFromPackageXml</c> route ANY body containing an
/// operator/paren/brace outside a quoted sheet name (<c>IsFormulaExpression</c>) into
/// <c>NamedFormulas</c>/<c>ScopedNamedFormulas</c> as a live, opaque formula -- including a
/// formula that also happens to embed a genuine external-book reference, e.g.
/// <c>=[1]Sheet1!$B$2*2</c> or <c>=SUM([1]Sheet1!A1:A10)+Local!B1</c>. Such a name IS modeled and
/// IS live, so <c>IsUnmodelableDefinedNameRefersTo</c> must not report it as unmodelable -- doing
/// so made <c>XlsxWorkbookMetadataPreserver.MergeDefinedNames</c> and
/// <c>XlsxFileAdapter.SourcePackageSnapshot</c>'s <c>RestorePatchWorkbookDefinedNames</c>
/// unconditionally resurrect the pristine definedName element from the source snapshot on every
/// save, silently undoing the user's deletion of the name from the Name Manager.
/// </summary>
public sealed class R87_ExternalDefinedNameLivenessTests
{
    [Theory]
    [InlineData("=[1]Sheet1!$B$2*2")]
    [InlineData("=SUM([1]Sheet1!A1:A10)+Local!B1")]
    public void IsUnmodelableDefinedNameRefersTo_ReturnsFalse_ForFormulaThatEmbedsExternalReference(
        string refersTo)
    {
        // This body contains an operator/paren outside any quoted sheet name, so
        // LoadDefinedNames's IsFormulaExpression check routes it into NamedFormulas as a live,
        // modeled, opaque formula -- it must therefore NOT be classified as "unmodelable", or the
        // liveness gate in MergeDefinedNames/RestorePatchWorkbookDefinedNames will resurrect a
        // genuinely-deleted live name from the pristine source on every save.
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(refersTo).Should().BeFalse(
            "a formula body containing an external-reference bracket is still a live, modeled " +
            "formula (per IsFormulaExpression) and must not be misclassified as unmodelable");
    }

    [Theory]
    [InlineData("=[1]Sheet1!$B$2")]
    [InlineData("='[1]Sheet1'!$B$2")]
    public void IsUnmodelableDefinedNameRefersTo_StillReturnsTrue_ForBareExternalReference(
        string refersTo)
    {
        // No-regression sibling: a BARE external-workbook reference (no operators at all, so
        // IsFormulaExpression is false and the name was never loaded into NamedFormulas/
        // NamedRanges in the first place) must still be classified as unmodelable, exactly as
        // before this fix -- otherwise a genuinely un-representable external reference would be
        // wrongly treated as "live" and dropped instead of being preserved verbatim.
        XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(refersTo).Should().BeTrue(
            "a bare external-workbook reference with no operators was never loaded into the " +
            "model and must remain classified as unmodelable so it is preserved verbatim");
    }
}
