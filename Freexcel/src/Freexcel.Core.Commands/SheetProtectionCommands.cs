using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Protect a worksheet with undo support.</summary>
public sealed class ProtectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string? _password;
    private bool _previousProtected;
    private string? _previousPassword;

    public string Label => "Protect Sheet";

    public ProtectSheetCommand(SheetId sheetId, string? password)
    {
        _sheetId = sheetId;
        _password = password;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousProtected = sheet.IsProtected;
        _previousPassword = sheet.ProtectionPassword;
        sheet.IsProtected = true;
        sheet.ProtectionPassword = string.IsNullOrEmpty(_password) ? null : _password;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.IsProtected = _previousProtected;
        sheet.ProtectionPassword = _previousPassword;
    }
}

/// <summary>Remove worksheet protection with undo support.</summary>
public sealed class UnprotectSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private bool _previousProtected;
    private string? _previousPassword;

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
        sheet.IsProtected = false;
        sheet.ProtectionPassword = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.IsProtected = _previousProtected;
        sheet.ProtectionPassword = _previousPassword;
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
