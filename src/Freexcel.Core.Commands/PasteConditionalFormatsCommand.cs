using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class PasteConditionalFormatsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _sourceRange;
    private readonly CellAddress _destination;
    private readonly bool _transpose;
    private List<ConditionalFormat>? _previousRules;

    public string Label => "Paste Conditional Formats";

    public PasteConditionalFormatsCommand(SheetId sheetId, GridRange sourceRange, CellAddress destination, bool transpose)
    {
        _sheetId = sheetId;
        _sourceRange = sourceRange;
        _destination = destination;
        _transpose = transpose;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_sourceRange.Start.Sheet != _sourceRange.End.Sheet || _destination.Sheet != _sheetId)
            return new CommandOutcome(false, "Paste conditional formats source range or destination is invalid.");

        var sourceSheet = ctx.GetSheet(_sourceRange.Start.Sheet);
        var targetSheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtectedWithoutPermission(targetSheet, SheetProtectionPermission.FormatCells) is { } protectedOutcome)
            return protectedOutcome;

        var pastedRules = sourceSheet.ConditionalFormats
            .Where(rule => rule.AppliesTo.Overlaps(_sourceRange))
            .Select(CloneRuleForDestination)
            .ToList();

        _previousRules = [.. targetSheet.ConditionalFormats];
        targetSheet.ConditionalFormats.AddRange(pastedRules);

        return new CommandOutcome(true, AffectedCells: pastedRules.SelectMany(rule => rule.AppliesTo.AllCells()).Distinct().ToList());
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousRules is null)
            return;

        var targetSheet = ctx.GetSheet(_sheetId);
        targetSheet.ConditionalFormats.Clear();
        targetSheet.ConditionalFormats.AddRange(_previousRules);
        _previousRules = null;
    }

    private ConditionalFormat CloneRuleForDestination(ConditionalFormat source)
    {
        var start = MapDestination(source.AppliesTo.Start);
        var end = MapDestination(source.AppliesTo.End);
        var clone = new ConditionalFormat
        {
            AppliesTo = new GridRange(start, end),
            Priority = source.Priority,
            RuleType = source.RuleType,
            Operator = source.Operator,
            Value1 = source.Value1,
            Value2 = source.Value2,
            FormatIfTrue = source.FormatIfTrue?.Clone(),
            MinColor = source.MinColor,
            MidColor = source.MidColor,
            MaxColor = source.MaxColor,
            UseThreeColorScale = source.UseThreeColorScale,
            MinThresholdType = source.MinThresholdType,
            MinThresholdValue = source.MinThresholdValue,
            MidThresholdType = source.MidThresholdType,
            MidThresholdValue = source.MidThresholdValue,
            MaxThresholdType = source.MaxThresholdType,
            MaxThresholdValue = source.MaxThresholdValue,
            DataBarColor = source.DataBarColor,
            DataBarMinThresholdType = source.DataBarMinThresholdType,
            DataBarMinThresholdValue = source.DataBarMinThresholdValue,
            DataBarMaxThresholdType = source.DataBarMaxThresholdType,
            DataBarMaxThresholdValue = source.DataBarMaxThresholdValue,
            DataBarShowValue = source.DataBarShowValue,
            DataBarMinLength = source.DataBarMinLength,
            DataBarMaxLength = source.DataBarMaxLength,
            AboveAverage = source.AboveAverage,
            FormulaText = source.FormulaText,
            IconSetStyle = source.IconSetStyle,
            IconSetShowValue = source.IconSetShowValue,
            IconSetReverse = source.IconSetReverse,
            TopBottomRank = source.TopBottomRank,
            TopBottomPercent = source.TopBottomPercent,
            TextRuleText = source.TextRuleText,
            DateOccurringPeriod = source.DateOccurringPeriod,
            StopIfTrue = source.StopIfTrue,
            NativeAttributes = source.NativeAttributes,
            NativeChildXmls = source.NativeChildXmls,
            NativePayloadAttributes = source.NativePayloadAttributes,
            NativePayloadChildXmls = source.NativePayloadChildXmls,
            NativeContainerAttributes = source.NativeContainerAttributes,
            NativeContainerChildXmls = source.NativeContainerChildXmls
        };
        clone.IconSetThresholds.AddRange(source.IconSetThresholds);
        return clone;
    }

    private CellAddress MapDestination(CellAddress source)
    {
        var rowOffset = source.Row - _sourceRange.Start.Row;
        var colOffset = source.Col - _sourceRange.Start.Col;
        return _transpose
            ? new CellAddress(_sheetId, _destination.Row + colOffset, _destination.Col + rowOffset)
            : new CellAddress(_sheetId, _destination.Row + rowOffset, _destination.Col + colOffset);
    }
}

