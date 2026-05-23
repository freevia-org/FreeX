using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Protect a worksheet with undo support.</summary>
public sealed class ProtectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string? _password;
    private readonly IReadOnlyList<SheetProtectionPermission> _permissions;
    private bool _previousProtected;
    private string? _previousPassword;
    private List<SheetProtectionPermission>? _previousPermissions;

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
        sheet.IsProtected = true;
        sheet.ProtectionPassword = string.IsNullOrEmpty(_password) ? null : _password;
        sheet.ProtectionPermissions.Clear();
        foreach (var permission in _permissions.Where(Enum.IsDefined).Distinct())
            sheet.ProtectionPermissions.Add(permission);
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
    }
}

/// <summary>Remove worksheet protection with undo support.</summary>
public sealed class UnprotectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private bool _previousProtected;
    private string? _previousPassword;
    private List<SheetProtectionPermission>? _previousPermissions;

    public string Label => "Unprotect Sheet";

    public UnprotectSheetCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousProtected = sheet.IsProtected;
        _previousPassword = sheet.ProtectionPassword;
        _previousPermissions = sheet.ProtectionPermissions.ToList();
        sheet.IsProtected = false;
        sheet.ProtectionPassword = null;
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
            return new CommandOutcome(false, "Allowed edit range must be on the target sheet.");

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
            return new CommandOutcome(false, "Allowed edit range must be on the target sheet.");

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
    private bool _previousProtected;
    private string? _previousPassword;

    public string Label => "Protect Workbook";

    public ProtectWorkbookCommand(string? password = null)
    {
        _password = password;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousProtected = ctx.Workbook.IsStructureProtected;
        _previousPassword = ctx.Workbook.StructureProtectionPassword;
        ctx.Workbook.IsStructureProtected = true;
        ctx.Workbook.StructureProtectionPassword = _password;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.IsStructureProtected = _previousProtected;
        ctx.Workbook.StructureProtectionPassword = _previousPassword;
    }
}

/// <summary>Remove workbook structure protection with undo support.</summary>
public sealed class UnprotectWorkbookCommand : IWorkbookCommand
{
    private bool _previousProtected;
    private string? _previousPassword;

    public string Label => "Unprotect Workbook";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousProtected = ctx.Workbook.IsStructureProtected;
        _previousPassword = ctx.Workbook.StructureProtectionPassword;
        ctx.Workbook.IsStructureProtected = false;
        ctx.Workbook.StructureProtectionPassword = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.Workbook.IsStructureProtected = _previousProtected;
        ctx.Workbook.StructureProtectionPassword = _previousPassword;
    }
}
