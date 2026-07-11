using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

file static class ProtectionCommandPasswordHashing
{
    /// <summary>
    /// Hashes a freshly-typed plaintext password (as supplied to Protect Sheet/Workbook) before it
    /// is stored on <see cref="Sheet.ProtectionPassword"/>/<see cref="Workbook.StructureProtectionPassword"/>,
    /// so those properties always hold a verifiable hash rather than cleartext -- see
    /// <see cref="ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash"/>.
    /// </summary>
    public static string? HashTypedPassword(string? typedPassword) =>
        string.IsNullOrEmpty(typedPassword)
            ? null
            : ProtectionPasswordHelper.ToVerifiedLegacyPasswordHash(typedPassword);
}

file static class WorkbookProtectionMetadataCleaner
{
    /// <summary>
    /// Names of the workbookProtection attributes that ProtectWorkbookCommand/UnprotectWorkbookCommand
    /// directly manage: the legacy structure lock/password plus the modern ISO 29500 hash quartet a
    /// prior Protect Workbook password may have left behind (see
    /// XlsxWorkbookMetadataWriter.ApplyProtection, which re-applies whatever survives in the preserved
    /// bag verbatim underneath a freshly-set password). Everything else the bag may carry --
    /// lockWindows, lockRevision, revisionsPassword, the revisions hash quartet, ... -- is unrelated to
    /// Structure protection and Core doesn't model it, so it must survive a Protect/Unprotect Workbook
    /// unchanged (see R22-protection-security-3).
    /// </summary>
    private static readonly string[] ManagedAttributeNames =
    [
        "lockStructure",
        "workbookPassword",
        "workbookAlgorithmName",
        "workbookHashValue",
        "workbookSaltValue",
        "workbookSpinCount",
    ];

    /// <summary>
    /// Returns a copy of <paramref name="metadata"/> with only the structure-password attributes this
    /// command manages removed from the preserved workbookProtection bag, leaving unrelated preserved
    /// attributes (and any preserved child elements) intact. Never mutates <paramref name="metadata"/>
    /// itself, so a command's captured "previous" bag (used by Revert/undo) stays untouched.
    /// </summary>
    public static NativeXmlPreserveBag? ClearManagedAttributes(NativeXmlPreserveBag? metadata)
    {
        if (metadata is null)
            return null;

        var raw = metadata.Get("workbookProtection");
        if (string.IsNullOrWhiteSpace(raw))
            return metadata;

        XElement element;
        try
        {
            element = XElement.Parse(raw);
        }
        catch
        {
            // Malformed preserved payload from an older save; leave it untouched rather than risk
            // losing content we don't understand.
            return metadata;
        }

        foreach (var name in ManagedAttributeNames)
            element.Attribute(name)?.Remove();

        var clone = metadata.Clone();
        clone.Set(
            "workbookProtection",
            element.Attributes().Any() || element.HasElements
                ? element.ToString(SaveOptions.DisableFormatting)
                : null);

        // If nothing unrelated survived, drop the bag entirely so the writer's
        // "nothing to preserve" fast path (ApplyProtection) still applies, instead of leaving a
        // functionally-empty bag object that would make it emit a stray empty workbookProtection
        // element.
        return clone.All.Count > 0 ? clone : null;
    }
}

/// <summary>Protect a worksheet with undo support.</summary>
public sealed class ProtectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string? _password;
    private readonly IReadOnlyList<SheetProtectionPermission> _permissions;
    private bool _previousProtected;
    private string? _previousPassword;
    private List<SheetProtectionPermission>? _previousPermissions;
    private NativeXmlPreserveBag? _previousProtectionMetadata;

    public string Label => "Protect Sheet";

    public ProtectSheetCommand(SheetId sheetId, string? password)
        : this(
            sheetId,
            password,
            [SheetProtectionPermission.SelectLockedCells, SheetProtectionPermission.SelectUnlockedCells])
    {
    }

    public ProtectSheetCommand(
        SheetId sheetId,
        string? password,
        IReadOnlyList<SheetProtectionPermission> permissions)
    {
        _sheetId = sheetId;
        _password = password;
        _permissions = permissions;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousProtected = sheet.IsProtected;
        _previousPassword = sheet.ProtectionPassword;
        _previousPermissions = sheet.ProtectionPermissions.ToList();
        _previousProtectionMetadata = sheet.ProtectionMetadata;
        sheet.IsProtected = true;
        sheet.ProtectionPassword = ProtectionCommandPasswordHashing.HashTypedPassword(_password);
        sheet.ProtectionPermissions.Clear();
        foreach (var permission in _permissions.Where(Enum.IsDefined).Distinct())
            sheet.ProtectionPermissions.Add(permission);

        // A freshly-typed password (or re-protecting with no password at all) supersedes whatever
        // modern ISO 29500 verifier a prior Protect Sheet password left behind in the preserved
        // metadata bag (see XlsxWorksheetProtectionMetadataWriter.Save). Without clearing it here,
        // the writer would re-apply the OLD password's algorithmName/hashValue/saltValue/spinCount
        // quartet from the preserved bag alongside (or instead of) the NEW password's legacy hash,
        // leaving a stale verifier so Excel (which trusts the modern hash) still unlocks with the
        // revoked password while the new one silently does nothing -- mirrors
        // ProtectWorkbookCommand.Apply.
        sheet.ProtectionMetadata = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.IsProtected = _previousProtected;
        sheet.ProtectionPassword = _previousPassword;
        sheet.ProtectionPermissions.Clear();
        foreach (var permission in _previousPermissions ?? [])
            sheet.ProtectionPermissions.Add(permission);
        sheet.ProtectionMetadata = _previousProtectionMetadata;
    }
}

/// <summary>Remove worksheet protection with undo support.</summary>
public sealed class UnprotectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string? _password;
    private bool _previousProtected;
    private string? _previousPassword;
    private List<SheetProtectionPermission>? _previousPermissions;
    private NativeXmlPreserveBag? _previousProtectionMetadata;

    public string Label => "Unprotect Sheet";

    public UnprotectSheetCommand(SheetId sheetId, string? password = null)
    {
        _sheetId = sheetId;
        _password = password;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (!ProtectionPasswordHelper.VerifyStoredPassword(sheet.ProtectionPassword, _password))
            return new CommandOutcome(false, "The password you supplied is not correct.");

        _previousProtected = sheet.IsProtected;
        _previousPassword = sheet.ProtectionPassword;
        _previousPermissions = sheet.ProtectionPermissions.ToList();
        _previousProtectionMetadata = sheet.ProtectionMetadata;
        sheet.IsProtected = false;
        sheet.ProtectionPassword = null;
        sheet.ProtectionPermissions.Clear();

        // Clear the preserved modern-hash verifier along with the password: a later Protect Sheet
        // with a new password must not have this stale bag re-applied underneath it (see
        // ProtectSheetCommand.Apply).
        sheet.ProtectionMetadata = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.IsProtected = _previousProtected;
        sheet.ProtectionPassword = _previousPassword;
        sheet.ProtectionPermissions.Clear();
        foreach (var permission in _previousPermissions ?? [])
            sheet.ProtectionPermissions.Add(permission);
        sheet.ProtectionMetadata = _previousProtectionMetadata;
    }
}

/// <summary>Allow edits in a protected worksheet range with undo support.</summary>
public sealed class AllowEditRangeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private bool _added;

    public string Label => "Allow Edit Range";

    public AllowEditRangeCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return CommandGuards.RejectAllowedEditRangeOnTargetSheet();

        var sheet = ctx.GetSheet(_sheetId);
        if (!sheet.AllowEditRanges.Contains(_range))
        {
            sheet.AllowEditRanges.Add(_range);
            _added = true;
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_added)
            ctx.GetSheet(_sheetId).AllowEditRanges.Remove(_range);
    }
}

/// <summary>Remove an allowed edit range from a protected worksheet with undo support.</summary>
public sealed class RemoveAllowEditRangeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private int _removedIndex = -1;

    public string Label => "Remove Allow Edit Range";

    public RemoveAllowEditRangeCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_range.Start.Sheet != _sheetId || _range.End.Sheet != _sheetId)
            return CommandGuards.RejectAllowedEditRangeOnTargetSheet();

        var ranges = ctx.GetSheet(_sheetId).AllowEditRanges;
        _removedIndex = ranges.IndexOf(_range);
        if (_removedIndex < 0)
            return new CommandOutcome(false, "Allowed edit range was not found.");

        ranges.RemoveAt(_removedIndex);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedIndex < 0)
            return;

        var ranges = ctx.GetSheet(_sheetId).AllowEditRanges;
        var index = Math.Min(_removedIndex, ranges.Count);
        ranges.Insert(index, _range);
    }
}

/// <summary>Clear all allowed edit ranges from a protected worksheet with undo support.</summary>
public sealed class ClearAllowEditRangesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private List<GridRange>? _previousRanges;

    public string Label => "Clear Allow Edit Ranges";

    public ClearAllowEditRangesCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var ranges = ctx.GetSheet(_sheetId).AllowEditRanges;
        _previousRanges = [.. ranges];
        ranges.Clear();
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRanges is null)
            return;

        var ranges = ctx.GetSheet(_sheetId).AllowEditRanges;
        ranges.Clear();
        ranges.AddRange(_previousRanges);
    }
}

/// <summary>Protect workbook structure with undo support.</summary>
public sealed class ProtectWorkbookCommand : IWorkbookCommand
{
    private readonly string? _password;
    private readonly bool _structureProtected;
    private bool _previousProtected;
    private string? _previousPassword;
    private NativeXmlPreserveBag? _previousProtectionMetadata;

    public string Label => "Protect Workbook";

    /// <param name="password">The optional password to protect with.</param>
    /// <param name="structureProtected">
    /// Whether the "Structure" checkbox was checked in the dialog. Defaults to <c>true</c> to preserve
    /// prior behavior for callers that don't model the checkbox. Window protection is not modelled by
    /// Core, so this is the only protection flag the command carries.
    /// </param>
    public ProtectWorkbookCommand(string? password = null, bool structureProtected = true)
    {
        _password = password;
        _structureProtected = structureProtected;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousProtected = ctx.Workbook.IsStructureProtected;
        _previousPassword = ctx.Workbook.StructureProtectionPassword;
        _previousProtectionMetadata = ctx.Workbook.ProtectionMetadata;
        ctx.Workbook.IsStructureProtected = _structureProtected;
        ctx.Workbook.StructureProtectionPassword = ProtectionCommandPasswordHashing.HashTypedPassword(_password);

        // A freshly-typed password (or re-protecting with no password at all) supersedes whatever
        // modern ISO 29500 verifier a prior Protect Workbook password left behind in the preserved
        // metadata bag (see XlsxWorkbookMetadataWriter.ApplyProtection). Without clearing it here,
        // the writer would re-apply the OLD password's workbookAlgorithmName/workbookHashValue/
        // workbookSaltValue/workbookSpinCount quartet alongside the NEW password's legacy
        // workbookPassword hash, leaving two conflicting verifiers so Excel (which trusts the
        // modern hash) still unlocks with the revoked password while FreeX's own reader (legacy
        // hash first) requires the new one. Only that managed quartet/structure-password slice of
        // the bag is cleared -- unrelated preserved attributes Core doesn't model (lockWindows,
        // lockRevision, revisionsPassword, ...) must survive untouched.
        ctx.Workbook.ProtectionMetadata =
            WorkbookProtectionMetadataCleaner.ClearManagedAttributes(ctx.Workbook.ProtectionMetadata);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.IsStructureProtected = _previousProtected;
        ctx.Workbook.StructureProtectionPassword = _previousPassword;
        ctx.Workbook.ProtectionMetadata = _previousProtectionMetadata;
    }
}

/// <summary>Remove workbook structure protection with undo support.</summary>
public sealed class UnprotectWorkbookCommand : IWorkbookCommand
{
    private readonly string? _password;
    private bool _previousProtected;
    private string? _previousPassword;
    private NativeXmlPreserveBag? _previousProtectionMetadata;

    public string Label => "Unprotect Workbook";

    public UnprotectWorkbookCommand(string? password = null)
    {
        _password = password;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!ProtectionPasswordHelper.VerifyStoredPassword(ctx.Workbook.StructureProtectionPassword, _password))
            return new CommandOutcome(false, "The password you supplied is not correct.");

        _previousProtected = ctx.Workbook.IsStructureProtected;
        _previousPassword = ctx.Workbook.StructureProtectionPassword;
        _previousProtectionMetadata = ctx.Workbook.ProtectionMetadata;
        ctx.Workbook.IsStructureProtected = false;
        ctx.Workbook.StructureProtectionPassword = null;

        // Clear the preserved modern-hash verifier along with the password: a later Protect
        // Workbook with a new password must not have this stale bag re-applied underneath it (see
        // ProtectWorkbookCommand.Apply). Only that managed quartet/structure-password slice of the
        // bag is cleared -- unrelated preserved attributes Core doesn't model (lockWindows,
        // lockRevision, revisionsPassword, ...) must survive untouched.
        ctx.Workbook.ProtectionMetadata =
            WorkbookProtectionMetadataCleaner.ClearManagedAttributes(ctx.Workbook.ProtectionMetadata);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.IsStructureProtected = _previousProtected;
        ctx.Workbook.StructureProtectionPassword = _previousPassword;
        ctx.Workbook.ProtectionMetadata = _previousProtectionMetadata;
    }
}
