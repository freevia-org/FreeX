using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round 78 finding io-dv-showmessages: two DV writers wrote
/// showInputMessage/showErrorMessage="0" (the OOXML-default, no-op case) and OMITTED the
/// attribute entirely for the true case (the case that actually needs to be written), so a
/// true ShowInputMessage/ShowErrorMessage flag was silently read back as false by Excel:
/// <list type="bullet">
///   <item>R78-io-dv-advanced-5-1: XlsxX14DataValidationWriter.BuildX14DataValidationElement
///     never emitted showInputMessage/showErrorMessage="1" for x14-promoted (e.g. cross-sheet
///     List) DV rules.</item>
///   <item>R78-io-dv-advanced-5-2: XlsxDataValidationNativeMetadataMapper.TryCreateValidationElement
///     dropped showInputMessage/showErrorMessage="1" for every rule on a sheet whenever any one
///     rule needed native-attribute passthrough (e.g. imeMode), because the whole
///     &lt;dataValidations&gt; block gets rebuilt from scratch in that path.</item>
/// </list>
/// </summary>
public sealed class R78_dv_showmessages_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string X14DvUri = "{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}";
    private const string WorksheetPath = "xl/worksheets/sheet1.xml";

    /// <summary>
    /// R78-io-dv-advanced-5-1: a cross-sheet List DV (which forces IsX14 = true) with the default
    /// ShowInputMessage/ShowErrorMessage = true and custom prompt/error text must re-emit
    /// showInputMessage="1"/showErrorMessage="1" on the x14 block. Before the fix, the inverted
    /// guard silently omitted both attributes for the true case, so Excel would read them back
    /// as false and never display the input prompt or error alert.
    /// </summary>
    [Fact]
    public void X14Writer_CrossSheetListDvWithMessagesEnabled_EmitsShowInputAndErrorMessage()
    {
        var wb = new Workbook("R78X14ShowMessagesTest");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true,
            ShowInputMessage = true,
            PromptTitle = "Pick one",
            PromptMessage = "Choose a value from the list",
            ShowErrorMessage = true,
            AlertStyle = DvAlertStyle.Warning,
            ErrorTitle = "Invalid",
            ErrorMessage = "Please choose a listed value",
        };
        sheet.DataValidations.Add(dv);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var x14DvElement = ReadX14DataValidationElement(stream);
        x14DvElement.Should().NotBeNull("the x14 DV element must be written for a cross-sheet List source");
        ((string?)x14DvElement!.Attribute("showInputMessage")).Should().Be(
            "1", "ShowInputMessage=true must be re-emitted so Excel displays the input prompt");
        ((string?)x14DvElement.Attribute("showErrorMessage")).Should().Be(
            "1", "ShowErrorMessage=true must be re-emitted so Excel displays the error alert");
    }

    /// <summary>
    /// No-regression sibling: when ShowInputMessage/ShowErrorMessage are false (the OOXML
    /// default), the x14 writer must NOT write the attributes at all -- matching the
    /// AllowBlank/ShowDropdown convention of only writing the non-default value.
    /// </summary>
    [Fact]
    public void X14Writer_CrossSheetListDvWithMessagesDisabled_OmitsShowInputAndErrorMessage()
    {
        var wb = new Workbook("R78X14ShowMessagesDisabledTest");
        var sheet = wb.AddSheet("Sheet1");
        wb.AddSheet("Sheet2");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(1));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            Type = DvType.List,
            Formula1 = "Sheet2!$A$1:$A$5",
            IsX14 = true,
            ShowInputMessage = false,
            ShowErrorMessage = false,
        };
        sheet.DataValidations.Add(dv);

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var x14DvElement = ReadX14DataValidationElement(stream);
        x14DvElement.Should().NotBeNull();
        x14DvElement!.Attribute("showInputMessage").Should().BeNull(
            "the default-false case must not write a redundant showInputMessage attribute");
        x14DvElement.Attribute("showErrorMessage").Should().BeNull(
            "the default-false case must not write a redundant showErrorMessage attribute");
    }

    /// <summary>
    /// R78-io-dv-advanced-5-2: when one DV rule on a sheet carries native (imeMode) metadata,
    /// XlsxDataValidationNativeMetadataMapper.Save rebuilds the WHOLE &lt;dataValidations&gt;
    /// block for that sheet. An unrelated rule on the same sheet with
    /// ShowInputMessage/ShowErrorMessage = true must keep those flags after the rebuild. Before
    /// the fix, the inverted guard dropped both attributes for every rule in the rebuilt block.
    /// </summary>
    [Fact]
    public void NativeMetadataMapper_UnrelatedRuleOnSheetWithImeModeRule_KeepsShowMessagesEnabled()
    {
        var wb = new Workbook("R78NativeMapperShowMessagesTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(1));

        // B2: carries opaque native metadata (imeMode) -- triggers HasNativeMetadata(sheet).
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["imeMode"] = "fullKatakana",
            },
        });

        // C3: unrelated Custom-formula rule with messages enabled -- must survive the rebuild.
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)),
            Type = DvType.Custom,
            Formula1 = "C3>0",
            ShowInputMessage = true,
            PromptTitle = "Enter value",
            PromptMessage = "Enter a positive number",
            ShowErrorMessage = true,
            AlertStyle = DvAlertStyle.Warning,
            ErrorTitle = "Invalid",
            ErrorMessage = "Must be positive",
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var c3Element = ReadLegacyDataValidationElement(stream, "C3");
        c3Element.Should().NotBeNull("C3's legacy dataValidation element must survive the sheet-wide native-metadata rebuild");
        ((string?)c3Element!.Attribute("showInputMessage")).Should().Be(
            "1", "the unrelated C3 rule's ShowInputMessage=true must not be dropped by the imeMode-triggered rebuild");
        ((string?)c3Element.Attribute("showErrorMessage")).Should().Be(
            "1", "the unrelated C3 rule's ShowErrorMessage=true must not be dropped by the imeMode-triggered rebuild");
    }

    /// <summary>
    /// No-regression sibling: the same imeMode-triggered rebuild must NOT write
    /// showInputMessage/showErrorMessage for a rule whose flags are false (the OOXML default).
    /// </summary>
    [Fact]
    public void NativeMetadataMapper_UnrelatedRuleOnSheetWithImeModeRule_OmitsShowMessagesWhenDisabled()
    {
        var wb = new Workbook("R78NativeMapperShowMessagesDisabledTest");
        var sheet = wb.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheetId, 3, 3), new NumberValue(1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            Formula1 = "0",
            NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["imeMode"] = "fullKatakana",
            },
        });

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)),
            Type = DvType.Custom,
            Formula1 = "C3>0",
            ShowInputMessage = false,
            ShowErrorMessage = false,
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(wb, stream);
        stream.Position = 0;

        var c3Element = ReadLegacyDataValidationElement(stream, "C3");
        c3Element.Should().NotBeNull();
        c3Element!.Attribute("showInputMessage").Should().BeNull(
            "the default-false case must not write a redundant showInputMessage attribute");
        c3Element.Attribute("showErrorMessage").Should().BeNull(
            "the default-false case must not write a redundant showErrorMessage attribute");
    }

    private static XElement? ReadX14DataValidationElement(MemoryStream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        return doc.Root!
            .Elements(WorksheetNs + "extLst")
            .SelectMany(extLst => extLst.Elements(WorksheetNs + "ext"))
            .Where(e => (string?)e.Attribute("uri") == X14DvUri)
            .SelectMany(e => e.Elements(X14Ns + "dataValidations"))
            .SelectMany(e => e.Elements(X14Ns + "dataValidation"))
            .FirstOrDefault();
    }

    private static XElement? ReadLegacyDataValidationElement(MemoryStream stream, string sqref)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetEntry = archive.GetEntry(WorksheetPath)!;
        XDocument doc;
        using (var xmlStream = worksheetEntry.Open())
            doc = XDocument.Load(xmlStream);

        return doc.Root!
            .Elements(WorksheetNs + "dataValidations")
            .SelectMany(e => e.Elements(WorksheetNs + "dataValidation"))
            .FirstOrDefault(e => (string?)e.Attribute("sqref") == sqref);
    }
}
