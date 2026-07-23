using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using NPOI.HSSF.Record;
using NPOI.HSSF.Record.Common;
using NPOI.HSSF.UserModel;
using NPOI.SS.Util;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R79-io-protection-5-1 / R79-io-protection-5-2: LegacyXlsFileAdapter must preserve two Excel
/// binary (.xls) sheet-protection features that a real Protect Sheet dialog can produce and that
/// are otherwise silently lost on Save As .xlsx:
///  - The "Allow Users to Edit Objects"/"...Scenarios" checkboxes when left UNCHECKED (i.e.
///    editable) alongside cell locking, mapped into <see cref="Sheet.ProtectionPermissions"/> the
///    same way the xlsx path's XlsxSheetProtectionPermissionMapper does.
///  - "Allow Users to Edit Ranges" entries (binary BIFF FeatHdr/Feat "ISFPROTECTION" shared
///    feature records), mapped into <see cref="Sheet.AllowEditRanges"/>/
///    <see cref="Sheet.AllowEditRangePasswords"/> the same way XlsxAllowEditRangeMapper does for
///    OOXML's &lt;protectedRanges&gt;.
/// </summary>
public sealed class R79_LegacyXlsProtectionExtensionsTests
{
    [Fact]
    public void Load_SheetProtectedWithObjectsAndScenariosLeftEditable_GrantsEditObjectsAndEditScenariosPermissions()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = (HSSFSheet)hssf.CreateSheet("S1");
        // Protect Sheet with cells locked but Objects/Scenarios explicitly left UNCHECKED (i.e.
        // still editable) -- a real, valid Excel configuration.
        sheet.Sheet.ProtectionBlock.ProtectSheet("secret", false, false);

        var workbook = RoundTrip(hssf);
        var loadedSheet = workbook.Sheets[0];

        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPermissions.Should().Contain(SheetProtectionPermission.EditObjects);
        loadedSheet.ProtectionPermissions.Should().Contain(SheetProtectionPermission.EditScenarios);
    }

    [Fact]
    public void Load_SheetProtectedWithObjectsAndScenariosDenied_DoesNotGrantEditObjectsOrEditScenariosPermissions()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = (HSSFSheet)hssf.CreateSheet("S1");
        // Protect Sheet with Objects/Scenarios also protected (Excel's own dialog default) --
        // confirms the fix didn't flip the common/default case into an over-grant.
        sheet.Sheet.ProtectionBlock.ProtectSheet("secret", true, true);

        var workbook = RoundTrip(hssf);
        var loadedSheet = workbook.Sheets[0];

        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.EditObjects);
        loadedSheet.ProtectionPermissions.Should().NotContain(SheetProtectionPermission.EditScenarios);
    }

    [Fact]
    public void Load_SheetWithAllowEditRangeFeatRecord_PopulatesAllowEditRangeAndPassword()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = (HSSFSheet)hssf.CreateSheet("S1");
        sheet.Sheet.ProtectionBlock.ProtectSheet("secret", true, true);

        AddAllowEditRangeFeatRecord(sheet, new CellRangeAddress(1, 9, 1, 1), passwordVerifier: 0x1234);

        var workbook = RoundTrip(hssf);
        var loadedSheet = workbook.Sheets[0];

        loadedSheet.AllowEditRanges.Should().ContainSingle(range => range.ToString() == "B2:B10");
        var range = loadedSheet.AllowEditRanges.Single();
        loadedSheet.AllowEditRangePasswords.Should().ContainKey(range).WhoseValue.Should().Be("1234");
    }

    [Fact]
    public void Load_SheetWithoutAllowEditRangeFeatRecord_LeavesAllowEditRangesEmpty()
    {
        using var hssf = new HSSFWorkbook();
        var sheet = (HSSFSheet)hssf.CreateSheet("S1");
        sheet.Sheet.ProtectionBlock.ProtectSheet("secret", true, true);

        var workbook = RoundTrip(hssf);
        var loadedSheet = workbook.Sheets[0];

        loadedSheet.AllowEditRanges.Should().BeEmpty();
        loadedSheet.AllowEditRangePasswords.Should().BeEmpty();
    }

    /// <summary>
    /// Injects a binary BIFF "Feat" record (ISFPROTECTION shared feature) for an Allow-Users-to-
    /// Edit-Ranges entry directly into the sheet's record stream, mirroring what a real Protect
    /// Sheet + Allow Edit Ranges save from Excel produces. NPOI's public HSSFSheet/ISheet API has
    /// no higher-level method for this feature (unlike ObjectProtect/ScenarioProtect), so the
    /// record is built the same way NPOI itself builds one and inserted immediately before the
    /// sheet's EOFRecord (records appended after EOF fall outside the worksheet substream and are
    /// dropped on the next parse).
    /// </summary>
    private static void AddAllowEditRangeFeatRecord(HSSFSheet sheet, CellRangeAddress range, int passwordVerifier)
    {
        var featProtection = new FeatProtection();
        featProtection.SetPasswordVerifier(passwordVerifier);
        featProtection.SetTitle("FreeXAllowEditRange1");

        var featRecord = new FeatRecord
        {
            SharedFeature = featProtection,
            CellRefs = [range]
        };

        var records = sheet.Sheet.Records;
        var eofIndex = records.FindIndex(record => record is EOFRecord);
        records.Insert(eofIndex, featRecord);
    }

    private static Workbook RoundTrip(HSSFWorkbook hssf)
    {
        using var stream = new MemoryStream();
        hssf.Write(stream, leaveOpen: true);
        stream.Position = 0;

        var adapter = new LegacyXlsFileAdapter();
        return adapter.Load(stream);
    }
}
