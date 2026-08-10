namespace FreeP.Core.Model;

internal static class PresentationModelCloneHelper
{
    internal static TableShape? FindTable(Presentation presentation, int slideIndex, uint shapeId)
    {
        if (slideIndex < 0 || slideIndex >= presentation.Slides.Count)
            return null;

        var shape = ShapeHelper.Find(presentation, slideIndex, shapeId);
        return shape?.Table;
    }

    internal static int GridColumnToCellIndex(TableRow row, int targetGridCol)
    {
        int gridPos = 0;
        for (int i = 0; i < row.Cells.Count; i++)
        {
            int span = Math.Max(1, row.Cells[i].GridSpan);
            if (targetGridCol < gridPos + span)
                return i;

            gridPos += span;
        }

        return -1;
    }

    internal static int CellGridStart(TableRow row, int cellIdx)
    {
        int gridPos = 0;
        for (int i = 0; i < cellIdx && i < row.Cells.Count; i++)
            gridPos += Math.Max(1, row.Cells[i].GridSpan);

        return gridPos;
    }

    internal static int RowGridWidth(TableRow row) =>
        row.Cells.Sum(c => Math.Max(1, c.GridSpan));

    internal static TableShape CloneTable(TableShape source)
    {
        var copy = new TableShape
        {
            Flags = CloneTableStyleFlags(source.Flags),
            TableStyleId = source.TableStyleId,
            StyleData = CloneTableStyleData(source.StyleData),
            RichTextLeftIndentPt = source.RichTextLeftIndentPt,
            RichTextCellSpacingPt = source.RichTextCellSpacingPt,
        };

        foreach (var width in source.ColumnWidthsEmu)
            copy.ColumnWidthsEmu.Add(width);

        foreach (var row in source.Rows)
            copy.Rows.Add(CloneTableRow(row));

        return copy;
    }

    internal static TableRow CloneTableRow(TableRow source)
    {
        var copy = new TableRow
        {
            HeightEmu = source.HeightEmu,
            HeightRule = source.HeightRule,
            HorizontalAlignment = source.HorizontalAlignment,
        };
        foreach (var cell in source.Cells)
            copy.Cells.Add(CloneTableCell(cell));

        return copy;
    }

    internal static TableCell CloneTableCell(TableCell source) => new()
    {
        TextBody = TextBodyModelCloner.CloneTextBody(source.TextBody),
        Fill = source.Fill,
        Borders = CloneTableCellBorders(source.Borders),
        GridSpan = source.GridSpan,
        RowSpan = source.RowSpan,
        HMerge = source.HMerge,
        VMerge = source.VMerge,
        InsetLeftPt = source.InsetLeftPt,
        InsetRightPt = source.InsetRightPt,
        InsetTopPt = source.InsetTopPt,
        InsetBottomPt = source.InsetBottomPt,
        Anchor = source.Anchor,
    };

    internal static ShapeEffects? CloneShapeEffects(ShapeEffects? source)
    {
        if (source is null)
            return null;

        return new ShapeEffects
        {
            HasOuterShadow = source.HasOuterShadow,
            OuterShadowColor = source.OuterShadowColor,
            OuterShadowAlpha = source.OuterShadowAlpha,
            OuterShadowBlurRadEmu = source.OuterShadowBlurRadEmu,
            OuterShadowDistEmu = source.OuterShadowDistEmu,
            OuterShadowDirDeg = source.OuterShadowDirDeg,
            HasInnerShadow = source.HasInnerShadow,
            InnerShadowColor = source.InnerShadowColor,
            InnerShadowAlpha = source.InnerShadowAlpha,
            InnerShadowBlurRadEmu = source.InnerShadowBlurRadEmu,
            InnerShadowDistEmu = source.InnerShadowDistEmu,
            InnerShadowDirDeg = source.InnerShadowDirDeg,
            HasGlow = source.HasGlow,
            GlowColor = source.GlowColor,
            GlowAlpha = source.GlowAlpha,
            GlowRadiusEmu = source.GlowRadiusEmu,
            HasSoftEdge = source.HasSoftEdge,
            SoftEdgeRadEmu = source.SoftEdgeRadEmu,
            BevelTop = CloneBevel(source.BevelTop),
            BevelBottom = CloneBevel(source.BevelBottom),
            ExtrusionHeightEmu = source.ExtrusionHeightEmu,
            ContourWidthEmu = source.ContourWidthEmu,
            PrstMaterial = source.PrstMaterial,
            ExtrusionColor = source.ExtrusionColor,
            ContourColor = source.ContourColor,
            Scene3d = source.Scene3d is null ? null : new Scene3dInfo
            {
                CameraPreset = source.Scene3d.CameraPreset,
                LightRig = source.Scene3d.LightRig,
                LightRigDir = source.Scene3d.LightRigDir,
            },
        };
    }

    private static BevelInfo? CloneBevel(BevelInfo? source) =>
        source is null ? null : new BevelInfo
        {
            WidthEmu = source.WidthEmu,
            HeightEmu = source.HeightEmu,
            PresetName = source.PresetName,
        };

    internal static Hyperlink? CloneHyperlink(Hyperlink? source) =>
        TextBodyModelCloner.CloneHyperlink(source);

    internal static void RestoreTableState(TableShape table, TableShape snapshot)
    {
        table.ColumnWidthsEmu.Clear();
        foreach (var width in snapshot.ColumnWidthsEmu)
            table.ColumnWidthsEmu.Add(width);

        table.Rows.Clear();
        foreach (var row in snapshot.Rows)
            table.Rows.Add(CloneTableRow(row));

        table.Flags = CloneTableStyleFlags(snapshot.Flags);
        table.TableStyleId = snapshot.TableStyleId;
        table.StyleData = CloneTableStyleData(snapshot.StyleData);
    }

    internal static TableCellBorders? CloneTableCellBorders(TableCellBorders? source) =>
        source is null
            ? null
            : new TableCellBorders
            {
                Left = source.Left,
                Right = source.Right,
                Top = source.Top,
                Bottom = source.Bottom,
                DiagonalDown = source.DiagonalDown,
                DiagonalUp = source.DiagonalUp,
            };

    private static TableStyleFlags CloneTableStyleFlags(TableStyleFlags source) => new()
    {
        FirstRow = source.FirstRow,
        LastRow = source.LastRow,
        FirstCol = source.FirstCol,
        LastCol = source.LastCol,
        BandRow = source.BandRow,
        BandCol = source.BandCol,
    };

    private static TableStyleData? CloneTableStyleData(TableStyleData? source) =>
        source is null
            ? null
            : new TableStyleData
            {
                StyleId = source.StyleId,
                WholeTbl = CloneTableStyleEntry(source.WholeTbl),
                FirstRow = CloneTableStyleEntry(source.FirstRow),
                LastRow = CloneTableStyleEntry(source.LastRow),
                FirstCol = CloneTableStyleEntry(source.FirstCol),
                LastCol = CloneTableStyleEntry(source.LastCol),
                Band1H = CloneTableStyleEntry(source.Band1H),
                Band2H = CloneTableStyleEntry(source.Band2H),
                Band1V = CloneTableStyleEntry(source.Band1V),
                Band2V = CloneTableStyleEntry(source.Band2V),
            };

    private static TableStyleEntry? CloneTableStyleEntry(TableStyleEntry? source) =>
        source is null
            ? null
            : new TableStyleEntry
            {
                Fill = source.Fill,
                BorderOutline = source.BorderOutline,
                TextColor = source.TextColor,
            };

}
