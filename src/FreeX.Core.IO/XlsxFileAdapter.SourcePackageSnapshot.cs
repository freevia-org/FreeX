using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    public static void ForgetLoadedPackageSnapshot(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        SourcePackages.Remove(workbook);
    }

    public bool RebaseLoadedPackageSnapshot(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return false;

        SourcePackages.Remove(workbook);
        SourcePackages.Add(workbook, sourcePackage.Rebase(workbook));
        return true;
    }

    public static bool TryPrepareLoadedPackageSnapshotForEdit(Workbook workbook, out string? blockReason)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        blockReason = null;
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
        {
            blockReason = "no_source_package";
            return false;
        }

        if (!sourcePackage.TryEnsureCellPatchEligibility(workbook, out var preparedPackage, out blockReason))
        {
            if (!ReferenceEquals(preparedPackage, sourcePackage))
            {
                SourcePackages.Remove(workbook);
                SourcePackages.Add(workbook, preparedPackage);
            }

            return false;
        }

        if (!preparedPackage.TryEnsureCellPatchBaseline(workbook, out preparedPackage, out blockReason))
        {
            SourcePackages.Remove(workbook);
            SourcePackages.Add(workbook, preparedPackage);
            return false;
        }

        if (!ReferenceEquals(preparedPackage, sourcePackage))
        {
            SourcePackages.Remove(workbook);
            SourcePackages.Add(workbook, preparedPackage);
        }

        return true;
    }

    private static string CreateSourceModelFingerprint(Workbook workbook)
        => CreateModelFingerprint(workbook, forPatchValidation: false);

    private static string CreatePatchValidationModelFingerprint(Workbook workbook)
        => CreateModelFingerprint(workbook, forPatchValidation: true);

    private static string CreateDrawingModelFingerprint(Workbook workbook)
    {
        using var hash = SHA256.Create();
        using var cryptoStream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, leaveOpen: true);
        using var stream = new BufferedStream(cryptoStream, bufferSize: 16 * 1024);
        WriteFingerprintToken(stream, "\nfreex-drawing-model-fingerprint-v1\n");
        WriteFingerprintNumber(stream, workbook.Sheets.Count);
        WriteFingerprintToken(stream, "\n");

        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            WriteFingerprintToken(stream, "sheet\t");
            WriteFingerprintNumber(stream, sheetIndex);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintString(stream, sheet.Id.Value.ToString("D"));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintString(stream, sheet.Name);
            WriteFingerprintToken(stream, "\ncharts\t");
            WriteFingerprintNumber(stream, sheet.Charts.Count);
            WriteFingerprintToken(stream, "\n");
            foreach (var chart in sheet.Charts)
                WriteDrawingChartFingerprint(stream, chart);

            WriteFingerprintToken(stream, "pictures\t");
            WriteFingerprintNumber(stream, sheet.Pictures.Count);
            WriteFingerprintToken(stream, "\n");
            foreach (var picture in sheet.Pictures)
                WriteDrawingPictureFingerprint(stream, picture);

            WriteFingerprintToken(stream, "textBoxes\t");
            WriteFingerprintNumber(stream, sheet.TextBoxes.Count);
            WriteFingerprintToken(stream, "\n");
            foreach (var textBox in sheet.TextBoxes)
                WriteDrawingTextBoxFingerprint(stream, textBox);

            WriteFingerprintToken(stream, "shapes\t");
            WriteFingerprintNumber(stream, sheet.DrawingShapes.Count);
            WriteFingerprintToken(stream, "\n");
            foreach (var shape in sheet.DrawingShapes)
                WriteDrawingShapeFingerprint(stream, shape);
        }

        stream.Flush();
        cryptoStream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash ?? []);
    }

    private static void WriteDrawingChartFingerprint(Stream stream, ChartModel chart)
    {
        WriteFingerprintString(stream, chart.Name);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, (int)chart.Type);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintString(stream, chart.DataRange.ToString());
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintBoolean(stream, chart.IsVisible);
        WriteFingerprintBoolean(stream, chart.IsPivotChart);
        WriteFingerprintString(stream, chart.PivotSourceSheetName);
        WriteFingerprintString(stream, chart.PivotTableName);
        WriteFingerprintString(stream, chart.PivotCacheId?.ToString(CultureInfo.InvariantCulture));
        WriteFingerprintString(stream, chart.Title);
        WriteFingerprintString(stream, chart.XAxisTitle);
        WriteFingerprintString(stream, chart.YAxisTitle);
        WriteFingerprintNumber(stream, chart.Width);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, chart.Height);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, (int)chart.DrawingAnchorKind);
        WriteFingerprintToken(stream, "\n");
    }

    private static void WriteDrawingPictureFingerprint(Stream stream, PictureModel picture)
    {
        WriteFingerprintString(stream, picture.Name);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintAddress(stream, picture.Anchor);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.AnchorOffsetX);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.AnchorOffsetY);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, (int)picture.Kind);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintBoolean(stream, picture.IsSourceLoaded);
        WriteFingerprintBoolean(stream, picture.IsLinkedToSourceRange);
        WriteFingerprintBoolean(stream, picture.IsVisible);
        WriteFingerprintString(stream, picture.LinkedSourceRange?.ToString());
        WriteFingerprintString(stream, picture.LinkedSourceSheetName);
        WriteFingerprintString(stream, picture.ContentType);
        WriteFingerprintString(stream, picture.Title);
        WriteFingerprintString(stream, picture.AltText);
        WriteFingerprintNumber(stream, picture.Width);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.Height);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.RotationDegrees);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintBoolean(stream, picture.FlipHorizontal);
        WriteFingerprintBoolean(stream, picture.FlipVertical);
        WriteFingerprintNumber(stream, picture.CropLeft);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.CropTop);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.CropRight);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, picture.CropBottom);
        WriteFingerprintToken(stream, "\n");
    }

    private static void WriteDrawingTextBoxFingerprint(Stream stream, TextBoxModel textBox)
    {
        WriteFingerprintString(stream, textBox.Name);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintAddress(stream, textBox.Anchor);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, textBox.AnchorOffsetX);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, textBox.AnchorOffsetY);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintString(stream, textBox.Text);
        WriteFingerprintString(stream, textBox.Title);
        WriteFingerprintString(stream, textBox.AltText);
        WriteFingerprintNumber(stream, textBox.Width);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, textBox.Height);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, textBox.RotationDegrees);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintBoolean(stream, textBox.FlipHorizontal);
        WriteFingerprintBoolean(stream, textBox.FlipVertical);
        WriteFingerprintBoolean(stream, textBox.IsVisible);
        WriteFingerprintBoolean(stream, textBox.HasFill);
        WriteFingerprintBoolean(stream, textBox.IsSourceLoaded);
        WriteFingerprintNullableColor(stream, textBox.FillColor);
        WriteFingerprintNullableColor(stream, textBox.OutlineColor);
        WriteFingerprintNullableThemeColor(stream, textBox.FillThemeColor);
        WriteFingerprintNullableThemeColor(stream, textBox.OutlineThemeColor);
        WriteFingerprintToken(stream, "\n");
    }

    private static void WriteDrawingShapeFingerprint(Stream stream, DrawingShapeModel shape)
    {
        WriteFingerprintString(stream, shape.Name);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintAddress(stream, shape.Anchor);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, shape.AnchorOffsetX);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, shape.AnchorOffsetY);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, (int)shape.Kind);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, shape.Width);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, shape.Height);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintNumber(stream, shape.RotationDegrees);
        WriteFingerprintToken(stream, "\t");
        WriteFingerprintBoolean(stream, shape.FlipHorizontal);
        WriteFingerprintBoolean(stream, shape.FlipVertical);
        WriteFingerprintBoolean(stream, shape.IsVisible);
        WriteFingerprintBoolean(stream, shape.HasFill);
        WriteFingerprintBoolean(stream, shape.IsSourceLoaded);
        WriteFingerprintString(stream, shape.Title);
        WriteFingerprintString(stream, shape.AltText);
        WriteFingerprintNullableColor(stream, shape.FillColor);
        WriteFingerprintNullableColor(stream, shape.OutlineColor);
        WriteFingerprintNullableColor(stream, shape.GradientFillEndColor);
        WriteFingerprintNumber(stream, (int)shape.GetEffectiveGradientFillDirection());
        WriteFingerprintNullableThemeColor(stream, shape.FillThemeColor);
        WriteFingerprintNullableThemeColor(stream, shape.OutlineThemeColor);
        WriteFingerprintNumber(stream, (int)shape.GetEffectiveEffectPreset());
        WriteFingerprintNumber(stream, shape.OutlineWidthPoints);
        WriteFingerprintBoolean(stream, shape.OutlineHasNoFill);
        WriteFingerprintNumber(stream, (int)shape.OutlineDash);
        WriteFingerprintToken(stream, "\n");
    }

    private static void WriteFingerprintAddress(Stream stream, CellAddress address)
    {
        WriteFingerprintNumber(stream, address.Row);
        WriteFingerprintToken(stream, ",");
        WriteFingerprintNumber(stream, address.Col);
    }

    private static string CreateModelFingerprint(Workbook workbook, bool forPatchValidation)
    {
        using var hash = SHA256.Create();
        using var cryptoStream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, leaveOpen: true);
        using var stream = new BufferedStream(cryptoStream, bufferSize: 64 * 1024);
        var adapter = new NativeJsonAdapter();
        if (forPatchValidation)
        {
            adapter.SaveForPatchValidationFingerprint(workbook, stream);
            WriteCellStyleFingerprint(workbook, stream);
        }
        else
        {
            adapter.SaveForFingerprint(workbook, stream);
        }

        WriteStyleOnlyFingerprint(workbook, stream);
        // GAP 2: include legacy comment authors so that an author-only change is detected by the
        // fingerprint comparison and the source-copy path is NOT taken (which would silently
        // preserve the old author).
        WriteLegacyCommentAuthorFingerprint(workbook, stream);
        // Include ShownComments (pinned note state) so that a pin/unpin toggles the fingerprint,
        // forcing a full save rather than a source-copy that would silently preserve stale VML.
        WriteShownCommentsFingerprint(workbook, stream);
        stream.Flush();
        cryptoStream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash ?? []);
    }

    private static void WriteLegacyCommentAuthorFingerprint(Workbook workbook, Stream stream)
    {
        WriteFingerprintToken(stream, "\nfreex-legacy-comment-author-fingerprint-v1\n");
        WriteFingerprintNumber(stream, workbook.Sheets.Count);
        foreach (var sheet in workbook.Sheets)
        {
            WriteFingerprintToken(stream, "\nsheet:");
            WriteFingerprintString(stream, sheet.Name);
            WriteFingerprintNumber(stream, sheet.CommentAuthors.Count);
            // Sort by address for a deterministic hash regardless of insertion order.
            foreach (var (address, author) in sheet.CommentAuthors.OrderBy(p => p.Key))
            {
                WriteFingerprintNumber(stream, address.Row);
                WriteFingerprintToken(stream, ",");
                WriteFingerprintNumber(stream, address.Col);
                WriteFingerprintToken(stream, ":");
                WriteFingerprintString(stream, author);
            }
        }
    }

    private static void WriteShownCommentsFingerprint(Workbook workbook, Stream stream)
    {
        WriteFingerprintToken(stream, "\nfreex-shown-comments-fingerprint-v1\n");
        WriteFingerprintNumber(stream, workbook.Sheets.Count);
        foreach (var sheet in workbook.Sheets)
        {
            WriteFingerprintToken(stream, "\nsheet:");
            WriteFingerprintString(stream, sheet.Name);
            WriteFingerprintNumber(stream, sheet.ShownComments.Count);
            // Sort by address for a deterministic hash regardless of insertion order.
            foreach (var address in sheet.ShownComments.OrderBy(a => a))
            {
                WriteFingerprintNumber(stream, address.Row);
                WriteFingerprintToken(stream, ",");
                WriteFingerprintNumber(stream, address.Col);
                WriteFingerprintToken(stream, ";");
            }
        }
    }

    private static void WriteCellStyleFingerprint(Workbook workbook, Stream stream)
    {
        WriteFingerprintToken(stream, "\nfreex-cell-style-fingerprint-v1\n");
        WriteFingerprintNumber(stream, workbook.StyleCount);
        WriteFingerprintToken(stream, "\n");

        for (var styleIndex = 0; styleIndex < workbook.StyleCount; styleIndex++)
        {
            var style = workbook.GetStyle(new StyleId(styleIndex));
            WriteFingerprintNumber(stream, styleIndex);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintString(stream, string.IsNullOrWhiteSpace(style.FontName) ? CellStyle.Default.FontName : style.FontName);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, NativeJsonValueSanitizer.PositiveFiniteOrDefault(style.FontSize, CellStyle.Default.FontSize));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBoolean(stream, style.Bold);
            WriteFingerprintBoolean(stream, style.Italic);
            WriteFingerprintBoolean(stream, style.Underline);
            WriteFingerprintBoolean(stream, style.Strikethrough);
            WriteFingerprintBoolean(stream, style.Superscript);
            WriteFingerprintBoolean(stream, style.Subscript);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintColor(stream, style.FontColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNullableThemeColor(stream, style.FontThemeColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNullableColor(stream, style.FillColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNullableThemeColor(stream, style.FillThemeColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, (int)NativeJsonValueSanitizer.ValidEnumOrDefault(style.FillPatternStyle, CellFillPatternStyle.None));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNullableColor(stream, style.FillPatternColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNullableThemeColor(stream, style.FillPatternThemeColor);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBorder(stream, style.BorderTop);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBorder(stream, style.BorderRight);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBorder(stream, style.BorderBottom);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBorder(stream, style.BorderLeft);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintString(stream, string.IsNullOrWhiteSpace(style.NumberFormat) ? CellStyle.Default.NumberFormat : style.NumberFormat);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, (int)NativeJsonValueSanitizer.ValidEnumOrDefault(style.HorizontalAlignment, HorizontalAlignment.General));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, (int)NativeJsonValueSanitizer.ValidEnumOrDefault(style.VerticalAlignment, VerticalAlignment.Bottom));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBoolean(stream, style.WrapText);
            WriteFingerprintBoolean(stream, style.ShrinkToFit);
            WriteFingerprintBoolean(stream, style.DoubleUnderline);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, Math.Clamp(style.IndentLevel, 0, 15));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintNumber(stream, NativeJsonValueSanitizer.ValidTextRotationOrDefault(style.TextRotation));
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintBoolean(stream, style.Locked);
            WriteFingerprintBoolean(stream, style.Hidden);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintDictionary(stream, style.NativeDifferentialAttributes);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintList(stream, style.NativeDifferentialChildXmls);
            WriteFingerprintToken(stream, "\t");
            WriteFingerprintDictionary(stream, style.NativeDifferentialElementXmls);
            WriteFingerprintToken(stream, "\n");
        }
    }

    private static void WriteStyleOnlyFingerprint(Workbook workbook, Stream stream)
    {
        WriteFingerprintToken(stream, "\nfreex-style-only-fingerprint-v1\n");
        for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            var sheet = workbook.Sheets[sheetIndex];
            WriteFingerprintToken(stream, "sheet\t");
            WriteFingerprintNumber(stream, sheetIndex);
            WriteFingerprintToken(stream, "\tcount\t");
            WriteFingerprintNumber(stream, sheet.StyleOnlyCellCount);
            WriteFingerprintToken(stream, "\n");

            if (!sheet.HasStyleOnlyCells)
                continue;

            if (sheet.TryGetCompressedStyleOnlyRuns(out var runs))
            {
                WriteFingerprintToken(stream, "runs\t");
                WriteFingerprintNumber(stream, runs.Count);
                WriteFingerprintToken(stream, "\n");
                foreach (var run in runs)
                {
                    WriteFingerprintNumber(stream, run.Row);
                    WriteFingerprintToken(stream, "\t");
                    WriteFingerprintNumber(stream, run.StartCol);
                    WriteFingerprintToken(stream, "\t");
                    WriteFingerprintNumber(stream, run.EndCol);
                    WriteFingerprintToken(stream, "\t");
                    WriteFingerprintNumber(stream, run.StyleId.Value);
                    WriteFingerprintToken(stream, "\n");
                }

                continue;
            }

            WriteFingerprintToken(stream, "entries\n");
            foreach (var ((row, col), styleId) in sheet.GetStyleOnlyEntries()
                         .OrderBy(entry => entry.Key.Row)
                         .ThenBy(entry => entry.Key.Col))
            {
                WriteFingerprintNumber(stream, row);
                WriteFingerprintToken(stream, "\t");
                WriteFingerprintNumber(stream, col);
                WriteFingerprintToken(stream, "\t");
                WriteFingerprintNumber(stream, styleId.Value);
                WriteFingerprintToken(stream, "\n");
            }
        }
    }

    private static void WriteFingerprintBoolean(Stream stream, bool value)
        => stream.WriteByte(value ? (byte)'1' : (byte)'0');

    private static void WriteFingerprintColor(Stream stream, CellColor color)
    {
        WriteFingerprintNumber(stream, color.R);
        WriteFingerprintToken(stream, ",");
        WriteFingerprintNumber(stream, color.G);
        WriteFingerprintToken(stream, ",");
        WriteFingerprintNumber(stream, color.B);
    }

    private static void WriteFingerprintNullableColor(Stream stream, CellColor? color)
    {
        if (color is not { } value)
        {
            stream.WriteByte((byte)'n');
            return;
        }

        stream.WriteByte((byte)'v');
        WriteFingerprintColor(stream, value);
    }

    private static void WriteFingerprintNullableThemeColor(Stream stream, WorkbookThemeColorReference? color)
    {
        if (color is not { } value)
        {
            stream.WriteByte((byte)'n');
            return;
        }

        stream.WriteByte((byte)'v');
        WriteFingerprintNumber(stream, (int)value.Slot);
        WriteFingerprintToken(stream, ",");
        WriteFingerprintNumber(stream, value.Tint);
    }

    private static void WriteFingerprintBorder(Stream stream, CellBorder border)
    {
        WriteFingerprintNumber(stream, (int)NativeJsonValueSanitizer.ValidEnumOrDefault(border.Style, BorderStyle.None));
        WriteFingerprintToken(stream, ",");
        WriteFingerprintColor(stream, border.Color);
    }

    private static void WriteFingerprintDictionary(Stream stream, IReadOnlyDictionary<string, string>? dictionary)
    {
        if (dictionary is null)
        {
            stream.WriteByte((byte)'n');
            return;
        }

        stream.WriteByte((byte)'d');
        WriteFingerprintNumber(stream, dictionary.Count);
        foreach (var pair in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            WriteFingerprintToken(stream, ":");
            WriteFingerprintString(stream, pair.Key);
            WriteFingerprintToken(stream, "=");
            WriteFingerprintString(stream, pair.Value);
        }
    }

    private static void WriteFingerprintList(Stream stream, IReadOnlyList<string>? list)
    {
        if (list is null)
        {
            stream.WriteByte((byte)'n');
            return;
        }

        stream.WriteByte((byte)'l');
        WriteFingerprintNumber(stream, list.Count);
        foreach (var value in list)
        {
            WriteFingerprintToken(stream, ":");
            WriteFingerprintString(stream, value);
        }
    }

    private static void WriteFingerprintString(Stream stream, string? value)
    {
        if (value is null)
        {
            WriteFingerprintToken(stream, "n");
            return;
        }

        WriteFingerprintToken(stream, "s");
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteFingerprintNumber(stream, byteCount);
        WriteFingerprintToken(stream, ":");
        WriteFingerprintToken(stream, value);
    }

    private static void WriteFingerprintNumber(Stream stream, double value)
    {
        Span<byte> buffer = stackalloc byte[32];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written, new StandardFormat('G', 17)))
            throw new InvalidOperationException("Unable to format XLSX fingerprint number.");

        stream.Write(buffer[..written]);
    }

    private static void WriteFingerprintNumber(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Unable to format XLSX fingerprint number.");

        stream.Write(buffer[..written]);
    }

    private static void WriteFingerprintNumber(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (!Utf8Formatter.TryFormat(value, buffer, out var written))
            throw new InvalidOperationException("Unable to format XLSX fingerprint number.");

        stream.Write(buffer[..written]);
    }

    private static void WriteFingerprintToken(Stream stream, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= 256)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), buffer);
            stream.Write(buffer[..written]);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), rented.AsSpan(0, byteCount));
            stream.Write(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private sealed record XlsxOfficeRevisionAttributeFacts(
        bool HasWorkbookAttributes,
        IReadOnlySet<string> WorksheetPaths)
    {
        public bool HasAny => HasWorkbookAttributes || WorksheetPaths.Count > 0;
    }

    private sealed record XlsxSourcePackage(
        byte[] Buffer,
        int Offset,
        int Count,
        string? ModelFingerprint,
        IReadOnlySet<string>? WorksheetsWithPreservableSourceMetadata,
        bool? HasUnsupportedConditionalFormatting,
        bool AllowsCellPatchSave,
        string? CellPatchEligibilityBlockReason,
        XlsxCellPatchBaseline? CellPatchBaseline,
        string? CellPatchBaselineBlockReason,
        XlsxCellPatchBaselineFacts? CellPatchBaselineFacts = null,
        bool IsCellPatchBaselineLazy = false,
        bool IsCellPatchEligibilityLazy = false,
        bool SourceHasCustomViews = false,
        bool? SourceNeedsPackageGraphNormalization = null,
        XlsxOfficeRevisionAttributeFacts? SourceOfficeRevisionAttributes = null,
        string? SourceDrawingModelFingerprint = null,
        // The in-model Sheet.Id (a stable GUID assigned once per Sheet object, unlike the sheet
        // NAME) for each sheet, captured in the same order as the pristine <sheets> element this
        // snapshot's Buffer holds, at the moment this snapshot became the pristine baseline (load,
        // rebase, or a fresh full-save/patch-save re-baseline). Lets RestorePatchWorkbookDefinedNames
        // (R28-meta-3) tell a genuine sheet RENAME (the same Sheet object survives, just renamed)
        // apart from a same-ordinal delete+add-a-different-sheet (a brand-new Sheet.Id) when a
        // sheet-scoped defined name's old scope name can no longer be found by name alone.
        IReadOnlyList<SheetId>? SourceSheetIdsByLocalId = null)
    {
        private const int FingerprintCellLimit = 100_000;
        private const int FingerprintCompressedStyleOnlyCellLimit = 1_250_000;
        private const int CellPatchBaselineLimit = 2_000_000;
        private const int CellPatchChangeLimit = 4_096;
        private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
        private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
        private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
        private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
        private const string ChartThemeOverrideRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/themeOverride";
        private const string ChartThemeOverrideContentType = "application/vnd.openxmlformats-officedocument.themeOverride+xml";
        private const string DiagramDataRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramData";
        private const string DiagramLayoutRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramLayout";
        private const string DiagramQuickStyleRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramQuickStyle";
        private const string DiagramColorsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/diagramColors";
        private const string DiagramDataContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml";
        private const string DiagramLayoutContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramLayout+xml";
        private const string DiagramStyleContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramStyle+xml";
        private const string DiagramColorsContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml";
        private const string ImageRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
        private const string PivotTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
        private const string PivotCacheDefinitionRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
        private const string PivotCacheRecordsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
        private const string CommentsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
        private const string VmlDrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
        private const string TableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
        private const string SingleCellTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/tableSingleCells";
        private const string RdRichValueRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValue";
        private const string RdRichValueStructureRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueStructure";
        private const string RdArrayRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdArray";
        private const string RdSupportingPropertyBagRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdSupportingPropertyBag";
        private const string RdSupportingPropertyBagStructureRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdSupportingPropertyBagStructure";
        private const string RdRichValueTypesRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/rdRichValueTypes";
        private const string RichStylesRelationshipType = "http://schemas.microsoft.com/office/2017/06/relationships/richStyles";
        private const string RichValueRelRelationshipType = "http://schemas.microsoft.com/office/2022/10/relationships/richValueRel";

        public static XlsxSourcePackage Capture(Stream stream, Workbook workbook)
            => Capture(stream, workbook, allowBufferReuse: false);

        public static XlsxSourcePackage Capture(Stream stream, Workbook workbook, bool allowBufferReuse)
            => Capture(stream, workbook, allowBufferReuse, currentModelFingerprint: null);

        public static XlsxSourcePackage Capture(
            Stream stream,
            Workbook workbook,
            bool allowBufferReuse,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting = null)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint: null,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting);

        public static XlsxSourcePackage Capture(Stream stream, Workbook workbook, string? currentModelFingerprint)
            => Capture(stream, workbook, allowBufferReuse: false, currentModelFingerprint);

        public static XlsxSourcePackage Capture(
            Stream stream,
            Workbook workbook,
            string? currentModelFingerprint,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting = null)
            => Capture(
                stream,
                workbook,
                allowBufferReuse: false,
                currentModelFingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting);

        private static XlsxSourcePackage Capture(
            Stream stream,
            Workbook workbook,
            bool allowBufferReuse,
            string? currentModelFingerprint)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint,
                worksheetsWithPreservableSourceMetadata: null,
                hasUnsupportedConditionalFormatting: null);

        private static XlsxSourcePackage Capture(
            Stream stream,
            Workbook workbook,
            bool allowBufferReuse,
            string? currentModelFingerprint,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting)
        {
            if (stream is MemoryStream memoryStream)
            {
                return Capture(
                    memoryStream,
                    workbook,
                    allowBufferReuse,
                    currentModelFingerprint,
                    worksheetsWithPreservableSourceMetadata,
                    hasUnsupportedConditionalFormatting);
            }

            var fingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(
                bytes,
                0,
                bytes.Length,
                fingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                AllowsCellPatchSave: false,
                CellPatchEligibilityBlockReason: null,
                CellPatchBaseline: null,
                CellPatchBaselineBlockReason: null,
                IsCellPatchBaselineLazy: true,
                IsCellPatchEligibilityLazy: true,
                SourceHasCustomViews: workbook.CustomViews.Count > 0,
                SourceDrawingModelFingerprint: CreateDrawingModelFingerprint(workbook),
                SourceSheetIdsByLocalId: workbook.Sheets.Select(s => s.Id).ToArray());
        }

        public static XlsxSourcePackage Capture(MemoryStream stream, Workbook workbook)
            => Capture(stream, workbook, allowBufferReuse: false);

        public static XlsxSourcePackage Capture(MemoryStream stream, Workbook workbook, bool allowBufferReuse)
            => Capture(stream, workbook, allowBufferReuse, currentModelFingerprint: null);

        public static XlsxSourcePackage Capture(
            MemoryStream stream,
            Workbook workbook,
            bool allowBufferReuse,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting = null)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint: null,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting);

        public static XlsxSourcePackage Capture(
            MemoryStream stream,
            Workbook workbook,
            bool allowBufferReuse,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout,
            bool sourceHasWorkbookCustomViews = false,
            bool? sourceNeedsPackageGraphNormalization = null)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint: null,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                sheetXmlLayout,
                sourceHasWorkbookCustomViews,
                sourceNeedsPackageGraphNormalization);

        private static XlsxSourcePackage Capture(
            MemoryStream stream,
            Workbook workbook,
            bool allowBufferReuse,
            string? currentModelFingerprint)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint,
                worksheetsWithPreservableSourceMetadata: null,
                hasUnsupportedConditionalFormatting: null);

        private static XlsxSourcePackage Capture(
            MemoryStream stream,
            Workbook workbook,
            bool allowBufferReuse,
            string? currentModelFingerprint,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout = null,
            bool sourceHasWorkbookCustomViews = false,
            bool? sourceNeedsPackageGraphNormalization = null)
        {
            var fingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
            var cellPatchBaselineFacts = XlsxCellPatchBaselineFacts.Capture(workbook, sheetXmlLayout);
            var sourceHasCustomViews = SourcePackageHasCustomViews(
                workbook,
                sheetXmlLayout,
                sourceHasWorkbookCustomViews);
            var sourceSheetIds = workbook.Sheets.Select(s => s.Id).ToArray();
            if (stream.TryGetBuffer(out var buffer))
            {
                if (allowBufferReuse &&
                    buffer.Array is not null &&
                    stream.Length <= int.MaxValue &&
                    buffer.Offset >= 0 &&
                    buffer.Offset + (int)stream.Length <= buffer.Array.Length)
                {
                    return new XlsxSourcePackage(
                        buffer.Array,
                        buffer.Offset,
                        (int)stream.Length,
                        fingerprint,
                        worksheetsWithPreservableSourceMetadata,
                        hasUnsupportedConditionalFormatting,
                        AllowsCellPatchSave: false,
                        CellPatchEligibilityBlockReason: null,
                        CellPatchBaseline: null,
                        CellPatchBaselineBlockReason: null,
                        CellPatchBaselineFacts: cellPatchBaselineFacts,
                        IsCellPatchBaselineLazy: true,
                        IsCellPatchEligibilityLazy: true,
                        SourceHasCustomViews: sourceHasCustomViews,
                        SourceNeedsPackageGraphNormalization: sourceNeedsPackageGraphNormalization,
                        SourceDrawingModelFingerprint: CreateDrawingModelFingerprint(workbook),
                        SourceSheetIdsByLocalId: sourceSheetIds);
                }

                var copiedBytes = buffer.Array is not null &&
                    stream.Length <= int.MaxValue &&
                    buffer.Offset >= 0 &&
                    buffer.Offset + (int)stream.Length <= buffer.Array.Length
                    ? buffer.Array.AsSpan(buffer.Offset, (int)stream.Length).ToArray()
                    : ReadBytes(stream);
                return new XlsxSourcePackage(
                    copiedBytes,
                    0,
                    copiedBytes.Length,
                    fingerprint,
                    worksheetsWithPreservableSourceMetadata,
                    hasUnsupportedConditionalFormatting,
                    AllowsCellPatchSave: false,
                    CellPatchEligibilityBlockReason: null,
                    CellPatchBaseline: null,
                    CellPatchBaselineBlockReason: null,
                    CellPatchBaselineFacts: cellPatchBaselineFacts,
                    IsCellPatchBaselineLazy: true,
                    IsCellPatchEligibilityLazy: true,
                    SourceHasCustomViews: sourceHasCustomViews,
                    SourceNeedsPackageGraphNormalization: sourceNeedsPackageGraphNormalization,
                    SourceDrawingModelFingerprint: CreateDrawingModelFingerprint(workbook),
                    SourceSheetIdsByLocalId: sourceSheetIds);
            }

            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(
                bytes,
                0,
                bytes.Length,
                fingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                AllowsCellPatchSave: false,
                CellPatchEligibilityBlockReason: null,
                CellPatchBaseline: null,
                CellPatchBaselineBlockReason: null,
                CellPatchBaselineFacts: cellPatchBaselineFacts,
                IsCellPatchBaselineLazy: true,
                IsCellPatchEligibilityLazy: true,
                SourceHasCustomViews: sourceHasCustomViews,
                SourceNeedsPackageGraphNormalization: sourceNeedsPackageGraphNormalization,
                SourceDrawingModelFingerprint: CreateDrawingModelFingerprint(workbook),
                SourceSheetIdsByLocalId: sourceSheetIds);
        }

        private static bool SourcePackageHasCustomViews(
            Workbook workbook,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout,
            bool sourceHasWorkbookCustomViews)
        {
            if (sourceHasWorkbookCustomViews || workbook.CustomViews.Count > 0)
                return true;

            return sheetXmlLayout?.Values.Any(layout => layout.CustomViews.Count > 0) == true;
        }

        private static byte[] ReadBytes(Stream stream)
        {
            if (!stream.CanSeek)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }

            var previousPosition = stream.Position;
            var bytes = new byte[checked((int)stream.Length)];
            try
            {
                stream.Position = 0;
                stream.ReadExactly(bytes);
            }
            finally
            {
                stream.Position = previousPosition;
            }

            return bytes;
        }

        public MemoryStream OpenRead() => new(Buffer, Offset, Count, writable: false);

        public XlsxSourcePackage Rebase(Workbook workbook)
        {
            // Rebasing declares the CURRENT in-memory workbook to be the loaded baseline that maps to
            // the stored source bytes (used after the open service paints dynamic table/pivot styles
            // for display, which Excel keeps unbaked).  Recompute the source fingerprint from the now-
            // materialized workbook so an otherwise-unchanged save matches and takes the byte-copy fast
            // path (NOT a full rebuild that would write the materialized fills into the file).  Falls
            // back to null only when the workbook is too large to fingerprint, preserving prior
            // behaviour for that case.
            var rebasedFingerprint = GetModelFingerprint(workbook, currentModelFingerprint: null);

            if (CellPatchBaseline is null)
                return IsCellPatchBaselineLazy
                    ? this with
                    {
                        ModelFingerprint = rebasedFingerprint,
                        SourceDrawingModelFingerprint = CreateDrawingModelFingerprint(workbook)
                    }
                    : this;

            return this with
            {
                ModelFingerprint = rebasedFingerprint,
                SourceDrawingModelFingerprint = CreateDrawingModelFingerprint(workbook),
                CellPatchBaseline = CellPatchBaseline.Rebase(workbook, CreatePatchValidationModelFingerprint(workbook)),
                CellPatchBaselineBlockReason = null
            };
        }

        public bool TryEnsureCellPatchBaseline(
            Workbook workbook,
            out XlsxSourcePackage preparedPackage,
            out string? blockReason)
        {
            preparedPackage = this;
            blockReason = CellPatchBaselineBlockReason;
            if (!IsCellPatchBaselineLazy)
                return CellPatchBaseline is not null;

            var cellPatchBaseline = XlsxCellPatchBaseline.TryCreate(
                Buffer,
                Offset,
                Count,
                workbook,
                CellPatchBaselineLimit,
                out blockReason,
                baselineFacts: CellPatchBaselineFacts);
            preparedPackage = this with
            {
                CellPatchBaseline = cellPatchBaseline,
                CellPatchBaselineBlockReason = blockReason,
                CellPatchBaselineFacts = null,
                IsCellPatchBaselineLazy = false
            };
            return cellPatchBaseline is not null;
        }

        public bool TryEnsureCellPatchEligibility(
            Workbook workbook,
            out XlsxSourcePackage preparedPackage,
            out string? blockReason)
        {
            preparedPackage = this;
            blockReason = CellPatchEligibilityBlockReason;
            if (!IsCellPatchEligibilityLazy)
                return AllowsCellPatchSave;

            var preserveSourceDrawingPackageParts =
                SourceDrawingModelFingerprint is not null &&
                string.Equals(
                    SourceDrawingModelFingerprint,
                    CreateDrawingModelFingerprint(workbook),
                    StringComparison.Ordinal);
            var allowsCellPatchSave = AllowsCellPatchSaveForPackage(
                Buffer,
                Offset,
                Count,
                workbook,
                preserveSourceDrawingPackageParts,
                out blockReason,
                out var officeRevisionAttributes);
            preparedPackage = this with
            {
                AllowsCellPatchSave = allowsCellPatchSave,
                CellPatchEligibilityBlockReason = blockReason,
                SourceOfficeRevisionAttributes = allowsCellPatchSave ? officeRevisionAttributes : null,
                IsCellPatchEligibilityLazy = false
            };
            return allowsCellPatchSave;
        }

        public bool Matches(Workbook workbook) => Matches(workbook, out _);

        public bool Matches(Workbook workbook, out string? currentModelFingerprint)
        {
            currentModelFingerprint = null;
            if (ModelFingerprint is null)
                return false;

            currentModelFingerprint = ShouldCaptureModelFingerprint(workbook)
                ? CreateModelFingerprint(workbook)
                : null;
            return currentModelFingerprint is not null &&
                   string.Equals(ModelFingerprint, currentModelFingerprint, StringComparison.Ordinal);
        }

        public void CopyTo(Stream stream)
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
                if (stream.CanWrite)
                    stream.SetLength(0);
            }

            stream.Write(Buffer, Offset, Count);
            if (stream.CanSeek)
                stream.Position = Count;
        }

        public void RestoreWorkbookDefinedNames(Stream stream, Workbook workbook)
        {
            var sourceWorkbookDefinedNames = ReadWorkbookDefinedNames();
            if (sourceWorkbookDefinedNames is null)
                return;

            if (stream.CanSeek)
                stream.Position = 0;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
            RestorePatchWorkbookDefinedNames(archive, sourceWorkbookDefinedNames, workbook, XlsxNamedRangeMapper.GetLiveDefinedNameKeys(workbook), ReadSourceSheetNamesByLocalId(), SourceSheetIdsByLocalId ?? []);
        }

        public bool TrySavePatchedCellValues(
            Workbook workbook,
            Stream stream,
            ref string? currentModelFingerprint,
            out XlsxSaveDiagnostics diagnostics)
        {
            var sourceWorkbookDefinedNames = ReadWorkbookDefinedNames();

            static bool Fail(
                string reason,
                out XlsxSaveDiagnostics diagnostics,
                bool invalidatesCalcChain = false)
            {
                diagnostics = XlsxSaveDiagnostics.FullSave(reason, invalidatesCalcChain);
                return false;
            }

            var allowsCellPatchSave = TryEnsureCellPatchEligibility(
                workbook,
                out var eligibilityPreparedPackage,
                out var cellPatchEligibilityBlockReason);
            var preparedPackage = eligibilityPreparedPackage;
            if (!ReferenceEquals(eligibilityPreparedPackage, this))
            {
                SourcePackages.Remove(workbook);
                SourcePackages.Add(workbook, eligibilityPreparedPackage);
            }

            if (!allowsCellPatchSave)
            {
                // This gate trips before any cell diff runs, so we cannot know whether the
                // workbook's formulas/sheet layout actually changed since it was loaded. A stale
                // source calcChain.xml surviving the resulting full-rebuild fallback can make
                // Excel show stale values or mis-order recalculation, so treat every eligibility
                // rejection as calc-chain invalidating -- the full rebuild will safely regenerate
                // calcChain.xml (or Excel rebuilds it itself when the part is absent).
                return Fail(
                    cellPatchEligibilityBlockReason ?? "patch_blocked_package_or_workbook_requires_full_save",
                    out diagnostics,
                    invalidatesCalcChain: true);
            }

            if (preparedPackage.CellPatchBaseline is null &&
                preparedPackage.IsCellPatchBaselineLazy)
            {
                var allowsBaseline = preparedPackage.TryEnsureCellPatchBaseline(
                    workbook,
                    out var baselinePreparedPackage,
                    out _);
                preparedPackage = baselinePreparedPackage;
                SourcePackages.Remove(workbook);
                SourcePackages.Add(workbook, preparedPackage);
                if (!allowsBaseline)
                {
                    // Structurally identical to the eligibility gate above: this runs strictly
                    // before TryGetPatchableValueChanges (the actual cell diff), so it cannot know
                    // whether the user's edit touched a formula. Baseline creation can fail for
                    // reasons entirely orthogonal to what changed (cell-count limit, an unreadable
                    // worksheet-path map, chart/pivot source-range indexing, a missing sheet path,
                    // ambiguous source cell styles, or an unexpected exception), so -- like the
                    // eligibility gate -- treat every baseline-unavailable rejection as calc-chain
                    // invalidating. Otherwise the stale source calcChain.xml would survive the
                    // full-rebuild fallback (CopyUnknownPackageParts copies it back unconditionally)
                    // alongside freshly recalculated formula cells. See R39_CalcChainEligibilityGateFallbackTests
                    // for the sibling fix this mirrors.
                    return Fail(
                        preparedPackage.CellPatchBaselineBlockReason ?? "patch_blocked_baseline_unavailable",
                        out diagnostics,
                        invalidatesCalcChain: true);
                }
            }

            if (preparedPackage.CellPatchBaseline is not { } cellPatchBaseline)
                return Fail(
                    preparedPackage.IsCellPatchBaselineLazy
                        ? "patch_blocked_deferred_baseline_not_materialized"
                        : preparedPackage.CellPatchBaselineBlockReason ?? "patch_blocked_baseline_unavailable",
                    out diagnostics,
                    invalidatesCalcChain: true);

            if (!cellPatchBaseline.TryGetPatchableValueChanges(
                    workbook,
                    CellPatchChangeLimit,
                    currentModelFingerprint,
                    out var changes,
                    out var dimensionChanges,
                    out var mergeRegionChanges,
                    out var hyperlinkChanges,
                    out var commentChanges,
                    out var worksheetViewChanges,
                    out var currentPatchValidationModelFingerprint,
                    out var changeBlockReason))
            {
                var reason = changeBlockReason ?? "patch_blocked_changes_not_patchable";
                return Fail(
                    reason,
                    out diagnostics,
                    PatchBlockReasonInvalidatesCalcChain(reason));
            }

            if (changes.Count == 0 &&
                dimensionChanges.Count == 0 &&
                mergeRegionChanges.Count == 0 &&
                hyperlinkChanges.Count == 0 &&
                commentChanges.Count == 0 &&
                worksheetViewChanges.Count == 0)
            {
                CopyTo(stream);
                diagnostics = XlsxSaveDiagnostics.SourceCopy("model_unchanged_after_patch_baseline");
                return true;
            }

            currentModelFingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
            var patchedSourceModelFingerprint = currentModelFingerprint;
            var patchedPatchValidationFingerprint =
                currentPatchValidationModelFingerprint ?? CreatePatchValidationModelFingerprint(workbook);
            var invalidatesCalcChain = ChangesInvalidateCalcChain(changes);

            // Pre-flight: reject any worksheet that contains r-less <row> elements.  Streaming
            // writers may omit the r attribute on rows (position implied by document order).
            // Patch-save cannot reliably match or insert into such rows, and would produce a
            // duplicate-row document if it tried.  Check the ORIGINAL source bytes here, before
            // any normalizer has had a chance to modify the in-memory copy, so the guard runs on
            // exactly the bytes that were stored when the workbook was loaded.
            // Must cover dimension-only changes (row height/hidden, no cell edits) too --
            // ApplyDimensionChanges -> ApplyRowDimension -> FindOrCreateRow skips r-less rows just
            // like the cell path does, and would otherwise append a duplicate <row> for the same
            // position instead of failing safe.
            var worksheetPathsWithChanges = changes
                .Select(c => c.WorksheetPath)
                .Concat(dimensionChanges.Select(c => c.WorksheetPath))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            using (var sourceReadPackage = new MemoryStream(Buffer, Offset, Count, writable: false))
            using (var sourceReadArchive = new ZipArchive(sourceReadPackage, ZipArchiveMode.Read))
            {
                foreach (var wPath in worksheetPathsWithChanges)
                {
                    var wEntry = sourceReadArchive.GetEntry(wPath);
                    if (wEntry is null)
                        continue;
                    // Streaming row-index scan: avoids materializing the full worksheet XDocument just
                    // to confirm every <row> carries an r attribute.
                    if (XlsxWorksheetGridXmlNormalizer.AnyRowMissingRowIndex(wEntry))
                        return Fail("patch_rless_rows", out diagnostics, invalidatesCalcChain);
                }
            }

            using var patchedPackage = new MemoryStream(Count + 4096);
            patchedPackage.Write(Buffer, Offset, Count);
            using (var archive = new ZipArchive(patchedPackage, ZipArchiveMode.Update, leaveOpen: true))
            {
                NormalizePatchCustomViews(archive, workbook, SourceHasCustomViews);
                NormalizePatchWorkbookViews(archive);
                NormalizePatchWorkbookCalculationProperties(archive);
                NormalizePatchWorkbookExternalReferences(archive);
                NormalizePatchWorkbookDefinedNames(archive);
                RestorePatchWorkbookDefinedNames(archive, sourceWorkbookDefinedNames, workbook, XlsxNamedRangeMapper.GetLiveDefinedNameKeys(workbook), ReadSourceSheetNamesByLocalId(), SourceSheetIdsByLocalId ?? []);
                NormalizePatchWorkbookOleSize(archive);
                NormalizePatchWorkbookPivotCaches(archive);
                NormalizePatchPivotTableDefinitions(archive);
                NormalizePatchWorkbookWebPublishing(archive);
                NormalizePatchWorkbookWebPublishObjects(archive);
                NormalizePatchWorkbookExtensionList(archive);
                NormalizePatchWorkbookProperties(archive);
                NormalizePatchWorkbookFileVersion(archive);
                NormalizePatchWorkbookFileSharing(archive);
                NormalizePatchWorkbookFileRecoveryProperties(archive);
                NormalizePatchWorkbookFunctionGroups(archive);
                NormalizePatchWorkbookSmartTags(archive);
                NormalizePatchWorkbookProtection(archive);
                NormalizePatchSharedStrings(archive);
                NormalizePatchDocumentThumbnail(archive);
                NormalizePatchInlineStringFonts(archive);
                NormalizePatchThemeTypefaces(archive);
                NormalizePatchLegacyCommentFonts(archive);
                NormalizePatchStylesheetDifferentialStyles(archive);
                NormalizePatchStylesheetTableStyles(archive);
                NormalizePatchStylesheetExtensionLists(archive);
                NormalizePatchWorksheetGridXml(archive);
                NormalizePatchWorksheetMergeCells(archive);
                NormalizePatchWorksheetDimension(archive);
                NormalizePatchWorksheetCalculationProperties(archive);
                NormalizePatchWorksheetSheetFormat(archive);
                NormalizePatchWorksheetSheetProperties(archive);
                NormalizePatchWorksheetSheetViews(archive);
                NormalizePatchWorksheetProtection(archive);
                NormalizePatchWorksheetProtectedRanges(archive);
                NormalizePatchWorksheetScenarios(archive);
                NormalizePatchWorksheetSmartTags(archive);
                NormalizePatchWorksheetPhoneticProperties(archive);
                NormalizePatchWorksheetCellWatches(archive);
                NormalizePatchWorksheetCustomProperties(archive);
                NormalizePatchWorksheetIgnoredErrors(archive);
                NormalizePatchWorksheetHyperlinks(archive);
                NormalizePatchWorksheetConditionalFormats(archive);
                NormalizePatchWorksheetDataValidations(archive);
                NormalizePatchWorksheetExtensionLists(archive);
                NormalizePatchWorksheetWebPublishItems(archive);
                NormalizePatchWorksheetOleControls(archive);
                NormalizePatchWorksheetRelationshipMarkers(archive);
                NormalizePatchWorksheetPageLayout(archive);
                NormalizePatchWorksheetPageBreaks(archive);
                NormalizePatchWorksheetSingleXmlCells(archive);
                NormalizePatchSingleCellTableParts(archive);
                NormalizePatchWorksheetAutoFilters(archive);
                NormalizePatchStructuredTableAutoFilters(archive);
                NormalizePatchStructuredTableSortStates(archive);
                NormalizePatchStructuredTableMetadata(archive);
                NormalizePatchExternalLinks(archive);
                NormalizePatchWorksheetSortStates(archive);
                NormalizePatchWorksheetDataConsolidation(archive);
                if (SourceOfficeRevisionAttributes is { HasAny: true } officeRevisionAttributes)
                    NormalizePatchOfficeRevisionAttributes(archive, officeRevisionAttributes);

                var cellChangesByWorksheet = changes
                    .GroupBy(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
                var dimensionChangesByWorksheet = dimensionChanges
                    .ToDictionary(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase);
                var mergeRegionChangesByWorksheet = mergeRegionChanges
                    .ToDictionary(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase);
                var hyperlinkChangesByWorksheet = hyperlinkChanges
                    .ToDictionary(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase);
                var worksheetViewChangesByWorksheet = worksheetViewChanges
                    .ToDictionary(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase);
                var commentChangesByPart = commentChanges
                    .GroupBy(change => change.CommentPartPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
                var worksheetPaths = cellChangesByWorksheet.Keys
                    .Concat(dimensionChangesByWorksheet.Keys)
                    .Concat(mergeRegionChangesByWorksheet.Keys)
                    .Concat(hyperlinkChangesByWorksheet.Keys)
                    .Concat(worksheetViewChangesByWorksheet.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                // Every LiteralValue-kind edit that overwrites a cell previously stored as t="s"
                // (a shared-string reference) removes exactly one reference from xl/sharedStrings.xml's
                // <sst count="..."> total -- RewriteLiteralCellValue always converts the changed cell to
                // t="inlineStr"/t="str"/etc, so it never keeps or re-adds a shared-string reference.
                // Track how many such conversions happen across every worksheet in this patch so the
                // shared-strings part's stale reference count can be corrected below (see
                // R52-io-sst-shared-inline-3-1).
                var sharedStringReferencesRemoved = 0;

                foreach (var worksheetPath in worksheetPaths)
                {
                    var worksheetEntry = archive.GetEntry(worksheetPath);
                    if (worksheetEntry is null)
                        return Fail("patch_apply_missing_worksheet", out diagnostics, invalidatesCalcChain);

                    if (cellChangesByWorksheet.TryGetValue(worksheetPath, out var streamingCellChanges) &&
                        !dimensionChangesByWorksheet.ContainsKey(worksheetPath) &&
                        !mergeRegionChangesByWorksheet.ContainsKey(worksheetPath) &&
                        !hyperlinkChangesByWorksheet.ContainsKey(worksheetPath) &&
                        !worksheetViewChangesByWorksheet.ContainsKey(worksheetPath) &&
                        XlsxCellPatchBaseline.TryApplySimpleExistingCellChangesStreaming(
                            archive,
                            worksheetPath,
                            streamingCellChanges,
                            out var streamedSharedStringReferencesRemoved))
                    {
                        sharedStringReferencesRemoved += streamedSharedStringReferencesRemoved;
                        continue;
                    }

                    var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                    if (cellChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetCellChanges))
                    {
                        if (!XlsxCellPatchBaseline.ApplyChanges(
                                worksheetXml,
                                worksheetCellChanges,
                                out var nonStreamedSharedStringReferencesRemoved))
                        {
                            return Fail("patch_apply_cell_values", out diagnostics, invalidatesCalcChain);
                        }

                        sharedStringReferencesRemoved += nonStreamedSharedStringReferencesRemoved;
                    }

                    if (dimensionChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetDimensionPatch) &&
                        !XlsxCellPatchBaseline.ApplyDimensionChanges(worksheetXml, worksheetDimensionPatch))
                    {
                        return Fail("patch_apply_dimensions", out diagnostics, invalidatesCalcChain);
                    }

                    if (mergeRegionChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetMergeRegionPatch) &&
                        !XlsxCellPatchBaseline.ApplyMergeRegionChanges(worksheetXml, worksheetMergeRegionPatch))
                    {
                        return Fail("patch_apply_merge_regions", out diagnostics, invalidatesCalcChain);
                    }

                    if (hyperlinkChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetHyperlinkPatch) &&
                        !XlsxCellPatchBaseline.ApplyHyperlinkChanges(worksheetXml, worksheetHyperlinkPatch))
                    {
                        return Fail("patch_apply_hyperlinks", out diagnostics, invalidatesCalcChain);
                    }

                    if (worksheetViewChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetViewPatch) &&
                        !XlsxCellPatchBaseline.ApplyWorksheetViewChanges(worksheetXml, worksheetViewPatch))
                    {
                        return Fail("patch_apply_worksheet_view", out diagnostics, invalidatesCalcChain);
                    }

                    XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
                }

                foreach (var (commentPartPath, commentPartChanges) in commentChangesByPart)
                {
                    var commentEntry = archive.GetEntry(commentPartPath);
                    if (commentEntry is null)
                        return Fail("patch_apply_missing_comment_part", out diagnostics, invalidatesCalcChain);

                    var commentsXml = XlsxPackageXmlEditor.LoadXml(commentEntry);
                    if (!XlsxCellPatchBaseline.ApplyCommentChanges(commentsXml, commentPartChanges))
                        return Fail("patch_apply_comments", out diagnostics, invalidatesCalcChain);

                    XlsxPackageXmlEditor.ReplaceXml(archive, commentPartPath, commentsXml);
                }

                if (sharedStringReferencesRemoved > 0)
                    DecrementSharedStringsReferenceCount(archive, sharedStringReferencesRemoved);

                // The cell-patch loop above rewrote worksheet XML (and may have touched header
                // elements such as dimension / merge / hyperlinks / sheetViews) without going through
                // the header-normalization driver, so drop its memoized pruned headers before the
                // post-patch normalizers run against the current bytes.
                XlsxWorksheetHeaderNormalization.InvalidateAll(archive);

                NormalizePatchWorksheetSheetProperties(archive);
                NormalizePatchWorksheetSingleXmlCells(archive);
                NormalizePatchWorksheetProtection(archive);
                NormalizePatchWorksheetProtectedRanges(archive);
                NormalizePatchWorksheetScenarios(archive);
                NormalizePatchWorksheetSmartTags(archive);
                NormalizePatchWorksheetWebPublishItems(archive);
                NormalizePatchWorksheetOleControls(archive);
                NormalizePatchWorksheetRelationshipMarkers(archive);
                NormalizePatchSingleCellTableParts(archive);
                XlsxCustomRibbonPackageGraphNormalizer.NormalizePackage(archive);
                XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(archive);

                if (invalidatesCalcChain)
                {
                    XlsxExcelCompatibilityNormalizer.RemoveCalcChain(archive);
                }

                if (SourceNeedsPackageGraphNormalization != false)
                    XlsxDocumentPropertiesPreserver.NormalizePackageGraph(archive);

                // Excel bumps dcterms:modified and cp:revision on every save (see
                // XlsxDocumentPropertiesPreserver.Preserve, which does the same for the
                // full-rebuild save path). The fast cell-patch path above never touches
                // docProps/core.xml itself, so without this the patched file's stamp would
                // stay frozen at whatever it was when the source package was first captured.
                UpdatePatchedDocumentPropertiesOnSave(archive, DateTimeOffset.UtcNow);
            }

            patchedPackage.Position = 0;
            if (stream.CanSeek)
            {
                stream.Position = 0;
                if (stream.CanWrite)
                    stream.SetLength(0);
            }

            patchedPackage.CopyTo(stream);
            if (stream.CanSeek)
                stream.Position = patchedPackage.Length;

            SourcePackages.Remove(workbook);
            if (patchedPackage.TryGetBuffer(out var patchedBuffer) &&
                patchedBuffer.Array is not null &&
                patchedPackage.Length <= int.MaxValue)
            {
                SourcePackages.Add(workbook, new XlsxSourcePackage(
                    patchedBuffer.Array,
                    patchedBuffer.Offset,
                    (int)patchedPackage.Length,
                    patchedSourceModelFingerprint,
                    WorksheetsWithPreservableSourceMetadata,
                    HasUnsupportedConditionalFormatting,
                    allowsCellPatchSave,
                    cellPatchEligibilityBlockReason,
                    cellPatchBaseline.WithAppliedChanges(
                        changes,
                        dimensionChanges,
                        mergeRegionChanges,
                        hyperlinkChanges,
                        commentChanges,
                        worksheetViewChanges,
                        patchedPatchValidationFingerprint),
                    preparedPackage.CellPatchBaselineBlockReason,
                    SourceHasCustomViews: workbook.CustomViews.Count > 0,
                    SourceNeedsPackageGraphNormalization: false,
                    SourceOfficeRevisionAttributes: null,
                    SourceDrawingModelFingerprint: CreateDrawingModelFingerprint(workbook),
                    // Patch-save is only eligible when every sheet's identity (Sheet.Id) and name
                    // are unchanged from the baseline (see change_sheet_identity_or_style_only_cells
                    // above), so the pristine per-ordinal Sheet.Id list carries forward unchanged.
                    SourceSheetIdsByLocalId: SourceSheetIdsByLocalId));
            }
            else
            {
                patchedPackage.Position = 0;
                SourcePackages.Add(workbook, Capture(
                    patchedPackage,
                    workbook,
                    patchedSourceModelFingerprint,
                    WorksheetsWithPreservableSourceMetadata,
                    HasUnsupportedConditionalFormatting) with
                    {
                        SourceNeedsPackageGraphNormalization = false
                    });
            }

            diagnostics = XlsxSaveDiagnostics.SourcePatch(
                "patch_applied",
                changes.Count,
                dimensionChanges.Count,
                mergeRegionChanges.Count,
                hyperlinkChanges.Count,
                commentChanges.Count,
                worksheetViewChanges.Count);
            return true;
        }

        private static bool PatchBlockReasonInvalidatesCalcChain(string reason) =>
            reason is "change_formula_text" or "change_formula_to_literal" or "change_formula_array_mode"
                or "change_sheet_count" or "change_dimension_metadata" or "change_cell_count_mismatch";

        private static bool ChangesInvalidateCalcChain(IEnumerable<XlsxCellValuePatch> changes) =>
            changes.Any(change =>
                change.Kind == XlsxCellValuePatchKind.FormulaTextAndCachedValue ||
                (change.Kind == XlsxCellValuePatchKind.DeletedCell && change.OriginalFormulaText is not null));

        private static void NormalizePatchCustomViews(ZipArchive archive, Workbook workbook, bool sourceHasCustomViews)
        {
            if (!sourceHasCustomViews)
                return;

            var changed = workbook.CustomViews.Count > 0
                ? NormalizePatchCustomWorkbookViews(archive) | NormalizePatchWorksheetCustomSheetViewExtensionLists(archive)
                : RemovePatchWorkbookCustomViews(archive) | RemovePatchWorksheetCustomViews(archive);
            if (changed)
                XlsxExcelCompatibilityNormalizer.RemoveCalcChain(archive);
        }

        private static bool NormalizePatchCustomWorkbookViews(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return false;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return false;

            var changed = false;
            foreach (var customWorkbookViews in root.Elements(workbookNs + "customWorkbookViews").ToList())
            {
                changed |= XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewsElement(customWorkbookViews);
                if (!XlsxWorkbookCustomViewNormalizer.ShouldRemoveCustomWorkbookViewsElement(customWorkbookViews))
                    continue;

                customWorkbookViews.Remove();
                changed = true;
            }

            if (!changed)
                return false;

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            return true;
        }

        private static bool NormalizePatchWorksheetCustomSheetViewExtensionLists(ZipArchive archive)
        {
            var changed = false;
            foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is null ||
                    !XlsxWorksheetCustomSheetViewExtensionListNormalizer.NormalizeWorksheetRoot(root))
                {
                    continue;
                }

                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
                changed = true;
            }

            return changed;
        }

        private static bool RemovePatchWorkbookCustomViews(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return false;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return false;

            var customWorkbookViews = root.Elements(workbookNs + "customWorkbookViews").ToList();
            if (customWorkbookViews.Count == 0)
                return false;

            foreach (var customWorkbookView in customWorkbookViews)
                customWorkbookView.Remove();

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
            return true;
        }

        private static bool RemovePatchWorksheetCustomViews(ZipArchive archive)
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var changed = false;
            foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is null)
                    continue;

                var customSheetViews = root.Elements(worksheetNs + "customSheetViews").ToList();
                if (customSheetViews.Count == 0)
                    continue;

                foreach (var customSheetView in customSheetViews)
                    customSheetView.Remove();

                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
                changed = true;
            }

            return changed;
        }

        private static void NormalizePatchWorkbookViews(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var bookViews = workbookXml.Root?.Element(workbookNs + "bookViews");
            if (bookViews is not null &&
                XlsxWorkbookViewNormalizer.NormalizeBookViewsElement(bookViews))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookCalculationProperties(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var calcPr = workbookXml.Root?.Element(workbookNs + "calcPr");
            if (calcPr is not null &&
                XlsxWorkbookCalculationPropertyNormalizer.NormalizeElement(calcPr))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookExternalReferences(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            // R99-io-external-references-patch-normalizer-1: this normalizer runs on every cell-PATCH
            // save, regardless of whether the edit touches external links, on the copied ORIGINAL
            // source bytes -- so a blank/missing/duplicate r:id here reflects a slot the source file
            // already encoded (possibly a dangling reference Excel itself left behind), not something
            // this save introduced. Preserve that ordinal slot (see the parameter doc on
            // NormalizeWorkbookRoot) instead of dropping it. Seed the reserved-id pool with every id
            // already used in xl/_rels/workbook.xml.rels so a minted placeholder id can never collide
            // with -- and thereby accidentally resolve against -- an unrelated real relationship (e.g.
            // a worksheet/styles/theme rId), matching XlsxExternalLinkReferencePreserver's guard.
            var reservedRelationshipIds = XlsxRelationshipReader.LoadTargets(
                archive,
                "xl/_rels/workbook.xml.rels",
                "xl/workbook.xml",
                packageRelNs).Keys;

            if (XlsxWorkbookExternalReferencesNormalizer.NormalizeWorkbookRoot(
                    root,
                    workbookNs,
                    reservedRelationshipIds))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookDefinedNames(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            var changed = false;
            foreach (var definedNames in root.Elements(workbookNs + "definedNames").ToList())
            {
                changed |= XlsxWorkbookDefinedNameNormalizer.NormalizeDefinedNamesElement(definedNames);
                if (!XlsxWorkbookDefinedNameNormalizer.ShouldRemoveDefinedNamesElement(definedNames))
                    continue;

                definedNames.Remove();
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private XElement? ReadWorkbookDefinedNames()
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var sourcePackage = new MemoryStream(Buffer, Offset, Count, writable: false);
            using var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Read);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return null;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var definedNames = workbookXml.Root?.Element(workbookNs + "definedNames");
            if (definedNames is null)
                return null;

            var copy = new XElement(definedNames);
            XlsxWorkbookDefinedNameNormalizer.NormalizeDefinedNamesElement(copy);
            return XlsxWorkbookDefinedNameNormalizer.ShouldRemoveDefinedNamesElement(copy)
                ? null
                : copy;
        }

        /// <summary>
        /// Reads the pristine pre-edit workbook's &lt;sheets&gt; order (sheet name by its ORIGINAL
        /// zero-based position, i.e. the localSheetId a sheet-scoped definedName's localSheetId
        /// attribute refers to). Used by <see cref="RestorePatchWorkbookDefinedNames"/> to remap a
        /// resurrected name's scope onto the sheet's current index after a delete/reorder (P112).
        /// </summary>
        private List<string> ReadSourceSheetNamesByLocalId()
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            using var sourcePackage = new MemoryStream(Buffer, Offset, Count, writable: false);
            using var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Read);
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return [];

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var sheets = workbookXml.Root?.Element(workbookNs + "sheets");
            if (sheets is null)
                return [];

            return sheets
                .Elements(workbookNs + "sheet")
                .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
                .ToList();
        }

        private static void RestorePatchWorkbookDefinedNames(
            ZipArchive archive,
            XElement? sourceDefinedNames,
            Workbook workbook,
            HashSet<string> liveModelDefinedNameKeys,
            IReadOnlyList<string> sourceSheetNamesByLocalId,
            IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
        {
            if (sourceDefinedNames is null)
                return;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            // Current (post-edit) sheet order, for remapping a resurrected sheet-scoped name's
            // stale localSheetId (P112): index in this list = the CURRENT localSheetId for that name.
            var targetSheetNames = root.Element(workbookNs + "sheets")?
                .Elements(workbookNs + "sheet")
                .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
                .ToList() ?? [];

            var changed = false;
            var targetDefinedNames = root.Element(workbookNs + "definedNames");
            if (targetDefinedNames is null)
            {
                targetDefinedNames = new XElement(workbookNs + "definedNames");
                InsertRestoredDefinedNamesElement(root, workbookNs, targetDefinedNames);
                changed = true;
            }

            var existingKeys = targetDefinedNames
                .Elements(workbookNs + "definedName")
                .Select(DefinedNameKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var sourceName in sourceDefinedNames.Elements(workbookNs + "definedName"))
            {
                var key = DefinedNameKey(sourceName);
                var existing = targetDefinedNames
                    .Elements(workbookNs + "definedName")
                    .FirstOrDefault(element => string.Equals(DefinedNameKey(element), key, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    foreach (var attribute in sourceName.Attributes())
                    {
                        if (existing.Attribute(attribute.Name) is not null)
                            continue;

                        existing.SetAttributeValue(attribute.Name, attribute.Value);
                        changed = true;
                    }

                    continue;
                }

                // This defined name exists in the pristine pre-edit source snapshot but was dropped
                // by the patch/full-save name write-back. Only resurrect it here when it is still
                // live in the current workbook model (e.g. it was preserved verbatim because the
                // save path never touched defined names for this key) - never re-add a name the user
                // deleted from the Name Manager, and never re-add an Excel-reserved name (Print_Area
                // etc.), which is intentionally excluded from the model round-trip and handled
                // elsewhere.
                //
                // liveModelDefinedNameKeys is derived purely from workbook.NamedFormulas/
                // ScopedNamedFormulas (via XlsxNamedRangeMapper.CreateDefinedNameEntries), which
                // itself re-checks workbook.ValidateNamedRangeName and skips any name FreeX's
                // (stricter-than-Excel) validator rejects. Such a name was *never loaded into the
                // model in the first place* (see LoadWorkbookDefinedNameFormulasFromPackage), so its
                // absence from liveModelDefinedNameKeys does not mean the user deleted it - it means
                // FreeX simply cannot round-trip it through the model. Gating resurrection on
                // liveness alone would then permanently drop a name the user never touched. Detect
                // that case directly and resurrect unconditionally, matching this method's original
                // (pre-round-8) unconditional-restore behavior for names FreeX doesn't model; keep
                // the liveness gate only for names FreeX *can* model (where an absence genuinely
                // means the user removed it via the Name Manager).
                var sourceNameAttr = sourceName.Attribute("name")?.Value;
                var isModelRepresentable = !string.IsNullOrWhiteSpace(sourceNameAttr) &&
                    workbook.ValidateNamedRangeName(sourceNameAttr) is null &&
                    !XlsxNamedRangeMapper.IsUnmodelableDefinedNameRefersTo(sourceName.Value);
                if (isModelRepresentable &&
                    !liveModelDefinedNameKeys.Contains(key) &&
                    !XlsxNamedRangeMapper.IsExcelReservedDefinedName(sourceNameAttr))
                {
                    continue;
                }

                var resurrected = new XElement(sourceName);
                var localSheetIdAttr = resurrected.Attribute("localSheetId");
                if (localSheetIdAttr is not null)
                {
                    // Sheet-scoped: remap the OLD localSheetId (an index into the pristine pre-edit
                    // <sheets> order) onto that same sheet's CURRENT index, since sheet delete/reorder
                    // shifts indices but this name was never live in the model to get remapped
                    // automatically. If the scope sheet itself no longer exists (deleted), drop the
                    // name entirely rather than emit an out-of-range/misscoped localSheetId (P112).
                    if (!int.TryParse(localSheetIdAttr.Value, out var oldLocalSheetId) ||
                        oldLocalSheetId < 0 ||
                        oldLocalSheetId >= sourceSheetNamesByLocalId.Count)
                        continue;

                    var scopeSheetName = sourceSheetNamesByLocalId[oldLocalSheetId];
                    var newLocalSheetId = targetSheetNames.FindIndex(
                        name => string.Equals(name, scopeSheetName, StringComparison.OrdinalIgnoreCase));
                    if (newLocalSheetId < 0)
                    {
                        // The old scope-sheet name isn't present under any current sheet BY NAME.
                        // This is ambiguous between the sheet having been deleted (drop the name,
                        // per the comment above) and the sheet having simply been RENAMED with no
                        // other structural change (the sheet - and this name's scope - is still
                        // there, just under a new name). Count+ordinal alone can't tell those apart:
                        // deleting a sheet and adding an unrelated new one at the same ordinal also
                        // leaves the count and position matching. Disambiguate by identity instead -
                        // a rename keeps the SAME Sheet object (and its stable Sheet.Id) alive in the
                        // model; a delete+add always produces a brand-new Sheet.Id that was never
                        // present at this snapshot's pristine load/rebase. Only treat this as a
                        // rename when the ORIGINAL sheet's Sheet.Id genuinely still exists.
                        var renamedSheetIndex = -1;
                        if (oldLocalSheetId < sourceSheetIdsByLocalId.Count)
                        {
                            var originalSheetId = sourceSheetIdsByLocalId[oldLocalSheetId];
                            for (var sheetIndex = 0; sheetIndex < workbook.Sheets.Count; sheetIndex++)
                            {
                                if (workbook.Sheets[sheetIndex].Id == originalSheetId)
                                {
                                    renamedSheetIndex = sheetIndex;
                                    break;
                                }
                            }
                        }

                        if (renamedSheetIndex >= 0)
                        {
                            newLocalSheetId = renamedSheetIndex;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    localSheetIdAttr.Value = newLocalSheetId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    key = DefinedNameKey(resurrected);

                    // Re-check for a collision at the remapped index: ClosedXML may already have
                    // re-emitted an entry for this name at the sheet's new index (common for
                    // Excel-reserved names like Print_Area).
                    if (existingKeys.Contains(key))
                        continue;

                    // R62-io-defined-name-print-6-1: Print_Area/Print_Titles are Excel-reserved
                    // names that ARE modeled (Sheet.PrintAreas / PrintTitleRows|PrintTitleColumns),
                    // unlike the other reserved names (_FilterDatabase, Criteria, Database, ...)
                    // that FreeX never loads into the model and therefore always preserves
                    // verbatim. Because IsExcelReservedDefinedName(sourceNameAttr) is true for
                    // these two names, the isModelRepresentable gate above can never fire for them
                    // (its "!IsExcelReservedDefinedName" term is always false, making the whole AND
                    // false) - so a Print_Area/Print_Titles the user just cleared
                    // (Sheet.SetPrintAreas([]) / PrintTitleRows = null) was unconditionally
                    // resurrected from the pristine source snapshot regardless of the sheet's
                    // current state. Mirror XlsxWorkbookMetadataPreserver.MergeDefinedNames'
                    // TryGetPrintSettingKind liveness check here: only resurrect when the sheet
                    // this name is (now) scoped to still actually has a print area / print titles
                    // set.
                    if (TryGetPrintSettingKind(sourceNameAttr, out var printSettingKind) &&
                        newLocalSheetId >= 0 &&
                        newLocalSheetId < workbook.Sheets.Count)
                    {
                        var scopeSheet = workbook.Sheets[newLocalSheetId];
                        var isLive = printSettingKind == PrintSettingKind.PrintArea
                            ? scopeSheet.PrintAreas.Count > 0
                            : scopeSheet.PrintTitleRows is not null || scopeSheet.PrintTitleColumns is not null;
                        if (!isLive)
                            continue;
                    }
                }

                targetDefinedNames.Add(resurrected);
                existingKeys.Add(key);
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);

            static string DefinedNameKey(XElement element)
            {
                var name = element.Attribute("name")?.Value ?? string.Empty;
                var localSheetId = element.Attribute("localSheetId")?.Value ?? string.Empty;
                return $"{name}\u001f{localSheetId}";
            }
        }

        // Mirrors XlsxWorkbookMetadataPreserver's private PrintSettingKind/TryGetPrintSettingKind
        // (used by MergeDefinedNames for the same print-area/print-titles liveness check on the
        // full-rebuild-without-source-package path); duplicated here rather than shared because
        // that preserver's copy is private to its own class and this file's resurrection path
        // (RestorePatchWorkbookDefinedNames) runs at a different point in the save pipeline.
        private enum PrintSettingKind
        {
            PrintArea,
            PrintTitles,
        }

        // Matches the reserved defined-name identifying a sheet's print area or print titles
        // (repeat rows/columns), whether stored with the standard OOXML "_xlnm." built-in-name
        // prefix (e.g. "_xlnm.Print_Area") or, for oddly-authored/legacy files, bare
        // ("Print_Area") - mirroring the two forms XlsxNamedRangeMapper.IsExcelReservedDefinedName
        // itself recognizes.
        private static bool TryGetPrintSettingKind(string? name, out PrintSettingKind kind)
        {
            kind = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var trimmed = name.Trim();
            var unprefixed = trimmed.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase)
                ? trimmed["_xlnm.".Length..]
                : trimmed;

            if (string.Equals(unprefixed, "Print_Area", StringComparison.OrdinalIgnoreCase))
            {
                kind = PrintSettingKind.PrintArea;
                return true;
            }

            if (string.Equals(unprefixed, "Print_Titles", StringComparison.OrdinalIgnoreCase))
            {
                kind = PrintSettingKind.PrintTitles;
                return true;
            }

            return false;
        }

        // Mirrors XlsxWorkbookSchemaNormalizer.WorkbookChildOrder's CT_Workbook child sequence (the
        // same ordering XlsxNamedRangeMapper.InsertDefinedNamesElement enforces for its own,
        // separate defined-name write-back) so a newly-created <definedNames> element on THIS
        // resurrection path - which runs strictly AFTER the schema normalizer's one-and-only reorder
        // pass has already completed - lands after sheets/functionGroups/externalReferences and
        // before calcPr/oleSize/etc, instead of unconditionally right after <sheets/>. Placing it
        // before <externalReferences/> violates the CT_Workbook sequence and triggers Excel's
        // "we found a problem with some content" repair prompt on open (R27-io-workbook-parts-deep-1).
        private static readonly string[] RestoredDefinedNamesPrecedingSiblings =
        {
            "sheets",
            "functionGroups",
            "externalReferences",
        };

        private static readonly string[] RestoredDefinedNamesFollowingSiblings =
        {
            "calcPr",
            "oleSize",
            "customWorkbookViews",
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects",
            "extLst",
        };

        private static void InsertRestoredDefinedNamesElement(XElement root, XNamespace workbookNs, XElement definedNames)
        {
            // Insert immediately after the last of sheets/functionGroups/externalReferences that is
            // present, in document order, so definedNames lands after all three per the schema.
            XElement? lastPrecedingSibling = null;
            foreach (var localName in RestoredDefinedNamesPrecedingSiblings)
            {
                var element = root.Element(workbookNs + localName);
                if (element is not null)
                    lastPrecedingSibling = element;
            }

            if (lastPrecedingSibling is not null)
            {
                lastPrecedingSibling.AddAfterSelf(definedNames);
                return;
            }

            // No sheets/functionGroups/externalReferences element found (unexpected but be
            // defensive): insert before the first element that must follow definedNames, if any.
            foreach (var localName in RestoredDefinedNamesFollowingSiblings)
            {
                var element = root.Element(workbookNs + localName);
                if (element is not null)
                {
                    element.AddBeforeSelf(definedNames);
                    return;
                }
            }

            root.Add(definedNames);
        }

        private static void NormalizePatchWorkbookOleSize(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            if (XlsxWorkbookOleSizeNormalizer.NormalizeWorkbookRoot(root, workbookNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookPivotCaches(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            if (XlsxWorkbookPivotCachesNormalizer.NormalizeWorkbookRoot(root, workbookNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchPivotTableDefinitions(ZipArchive archive) =>
            XlsxExcelCompatibilityNormalizer.NormalizeLegacyPivotTableDefinitionAttributes(archive);

        private static void NormalizePatchWorkbookWebPublishing(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            if (XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(root, workbookNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookWebPublishObjects(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            if (XlsxWorkbookWebPublishObjectsNormalizer.NormalizeWorkbookRoot(root, workbookNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookExtensionList(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            if (root is null)
                return;

            if (XlsxWorkbookExtensionListNormalizer.NormalizeWorkbookRoot(root, workbookNs))
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookProperties(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookPr = workbookXml.Root?.Element(workbookNs + "workbookPr");
            if (workbookPr is not null &&
                XlsxWorkbookPropertiesNormalizer.NormalizeElement(workbookPr))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookFileSharing(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var fileSharing = workbookXml.Root?.Element(workbookNs + "fileSharing");
            if (fileSharing is not null &&
                XlsxWorkbookFileSharingNormalizer.NormalizeElement(fileSharing))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookFileVersion(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var fileVersion = workbookXml.Root?.Element(workbookNs + "fileVersion");
            if (fileVersion is not null &&
                XlsxWorkbookFileVersionNormalizer.NormalizeElement(fileVersion))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookFileRecoveryProperties(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var changed = false;
            foreach (var fileRecoveryPr in workbookXml.Root?.Elements(workbookNs + "fileRecoveryPr") ?? [])
                changed |= XlsxWorkbookFileRecoveryPropertyNormalizer.NormalizeElement(fileRecoveryPr);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookFunctionGroups(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var functionGroups = workbookXml.Root?.Element(workbookNs + "functionGroups");
            if (functionGroups is not null &&
                XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(functionGroups))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchWorkbookSmartTags(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var changed = false;
            if (workbookXml.Root?.Element(workbookNs + "smartTagPr") is { } smartTagPr)
                changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagPr);

            if (workbookXml.Root?.Element(workbookNs + "smartTagTypes") is { } smartTagTypes)
            {
                changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);
                if (XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes))
                {
                    smartTagTypes.Remove();
                    changed = true;
                }
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }

        private static void NormalizePatchWorkbookProtection(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookProtection = workbookXml.Root?.Element(workbookNs + "workbookProtection");
            if (workbookProtection is not null &&
                XlsxWorkbookProtectionNormalizer.NormalizeElement(workbookProtection))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
            }
        }

        private static void NormalizePatchSharedStrings(ZipArchive archive)
        {
            XlsxRichTextFontNormalizer.NormalizeSharedStrings(archive);
            XlsxSharedStringPackageGraphNormalizer.NormalizePackage(archive);
        }

        // Patch-save always rewrites an edited shared-string cell (t="s") as an inline/literal value
        // (RewriteLiteralCellValue) instead of decrementing its reference in xl/sharedStrings.xml, so
        // the <sst count="..."> total (the workbook-wide count of cell references to shared strings)
        // goes stale by exactly one per such edit. This does not attempt full orphan-<si> pruning or
        // uniqueCount recomputation (both require a whole-workbook scan of every remaining t="s" cell
        // to know whether a given shared-string index still has any referrer, which patch-save's
        // per-worksheet streaming design intentionally avoids for performance) -- it only corrects the
        // one piece of information already known for free at the edit site: how many references were
        // just removed. See R52-io-sst-shared-inline-3-1.
        private static void DecrementSharedStringsReferenceCount(ZipArchive archive, int removedReferenceCount)
        {
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry is null)
                return;

            var sharedStringsXml = XlsxPackageXmlEditor.LoadXml(sharedStringsEntry);
            var countAttribute = sharedStringsXml.Root?.Attribute("count");
            if (countAttribute is null ||
                !int.TryParse(countAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentCount))
            {
                return;
            }

            var updatedCount = Math.Max(0, currentCount - removedReferenceCount);
            if (updatedCount == currentCount)
                return;

            countAttribute.Value = updatedCount.ToString(CultureInfo.InvariantCulture);
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
        }

        private static void NormalizePatchDocumentThumbnail(ZipArchive archive)
            => XlsxDocumentThumbnailPackageGraphNormalizer.NormalizePackage(archive);

        private static void NormalizePatchInlineStringFonts(ZipArchive archive)
        {
            XlsxRichTextFontNormalizer.NormalizeWorksheetInlineStrings(archive);
        }

        private static void NormalizePatchThemeTypefaces(ZipArchive archive)
        {
            XlsxThemeTypefaceNormalizer.NormalizePackage(archive);
        }

        private static void NormalizePatchLegacyCommentFonts(ZipArchive archive)
        {
            XlsxLegacyCommentFontNormalizer.NormalizePackage(archive);
        }

        private static void NormalizePatchStylesheetDifferentialStyles(ZipArchive archive)
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
            var differentialStyles = stylesXml.Root?.Element(workbookNs + "dxfs");
            if (differentialStyles is null ||
                !XlsxStylesheetSchemaNormalizer.NormalizeDifferentialStyles(differentialStyles, workbookNs))
            {
                return;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        }

        private static void NormalizePatchStylesheetTableStyles(ZipArchive archive)
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
            var tableStyles = stylesXml.Root?.Element(workbookNs + "tableStyles");
            if (tableStyles is null ||
                !XlsxStylesheetSchemaNormalizer.NormalizeTableStyles(tableStyles, workbookNs))
            {
                return;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        }

        private static void NormalizePatchStylesheetExtensionLists(ZipArchive archive)
        {
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return;

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
            var root = stylesXml.Root;
            if (root is null ||
                !XlsxStylesheetSchemaNormalizer.NormalizeStylesheetExtensionLists(root, workbookNs))
            {
                return;
            }

            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
        }

        private static void NormalizePatchWorksheetPhoneticProperties(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetPhoneticPropertyNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetCellWatches(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetCellWatchesNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetCustomProperties(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetCustomPropertiesNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetIgnoredErrors(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetIgnoredErrorsNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetHyperlinks(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetHyperlinkNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetConditionalFormats(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetConditionalFormatNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetDataValidations(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetDataValidationNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetExtensionLists(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetExtensionListNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetWebPublishItems(ZipArchive archive)
        {
            // Web-publish items are absent from virtually every workbook.  Only pay for the full pass
            // (which loads each worksheet's XML) when a standalone part exists or some worksheet header
            // actually carries a <webPublishItems> element.
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            if (archive.GetEntry("xl/webPublishItems.xml") is not null ||
                XlsxWorksheetHeaderNormalization.AnyWorksheetHeaderMatches(
                    archive,
                    root => root.Elements(workbookNs + "webPublishItems").Any()))
            {
                XlsxWorksheetWebPublishItemsNormalizer.NormalizePackage(archive);
            }
        }

        private static void NormalizePatchWorksheetOleControls(ZipArchive archive)
        {
            // OLE objects / form controls are rare.  Skip the full per-worksheet pass (including the
            // relationship rebinds) unless some worksheet header carries <oleObjects> or <controls>.
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            if (XlsxWorksheetHeaderNormalization.AnyWorksheetHeaderMatches(
                    archive,
                    root => root.Elements(workbookNs + "oleObjects").Any() ||
                            root.Elements(workbookNs + "controls").Any()))
            {
                XlsxWorksheetOleControlNormalizer.NormalizeWorksheets(archive);
            }
        }

        private static void NormalizePatchWorksheetRelationshipMarkers(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetSingleXmlCells(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            // The per-worksheet rewrite only matters when a worksheet header carries <singleXmlCells>.
            // Skip the full worksheet loads entirely otherwise; the package-level mapper below still runs.
            if (XlsxWorksheetHeaderNormalization.AnyWorksheetHeaderMatches(
                    archive,
                    root => root.Elements(workbookNs + "singleXmlCells").Any()))
            {
                foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
                {
                    var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                    var root = worksheetXml.Root;
                    if (root is null)
                        continue;

                    var singleXmlCells = root.Elements(workbookNs + "singleXmlCells").ToList();
                    if (singleXmlCells.Count == 0 ||
                        !HasPartBackedSingleXmlCells(archive, worksheetEntry.FullName))
                    {
                        continue;
                    }

                    foreach (var element in singleXmlCells)
                        element.Remove();
                    XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
                }
            }

            XlsxWorksheetSingleXmlCellMapper.NormalizePackage(archive);
        }

        private static void NormalizePatchWorksheetPageLayout(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetGridXml(ZipArchive archive) =>
            XlsxWorksheetGridXmlNormalizer.NormalizeWorksheets(archive);

        private static void NormalizePatchWorksheetMergeCells(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetMergeCellsNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetDimension(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetDimensionNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetCalculationProperties(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetCalculationPropertyNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetSheetFormat(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetSheetFormatNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetSheetProperties(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetSheetPropertiesNormalizer.NormalizeWorksheetRoot);

        // Mirrors XlsxDocumentPropertiesPreserver's (private) UpdateModifiedAndRevisionOnSave,
        // which only runs on the full-ClosedXML-rebuild save path. The fast cell-patch path
        // (this file) needs the identical dcterms:modified / cp:revision bump on every save, so
        // the logic is duplicated here against docProps/core.xml directly using the same public
        // shared-Opc constants/helpers rather than reaching into that internal method.
        private static void UpdatePatchedDocumentPropertiesOnSave(ZipArchive archive, DateTimeOffset saveTimestamp)
        {
            var coreEntry = archive.GetEntry(OpcPackageProperties.CorePropertiesZipEntry);
            if (coreEntry is null)
                return;

            var coreXml = XlsxPackageXmlEditor.LoadXml(coreEntry);
            var coreRoot = coreXml.Root;
            if (coreRoot is null)
                return;

            var modifiedName = OpcDocumentProperties.DublinCoreTermsNamespace + "modified";
            var modifiedValue = OpcPackageProperties.ToW3CDtf(saveTimestamp);
            var modifiedElement = coreRoot.Element(modifiedName);
            if (modifiedElement is null)
            {
                // The xsi:type value below is a literal QName string ("<prefix>:W3CDTF"), so the
                // prefix it names must actually be declared in scope -- it cannot rely on
                // XElement's serializer happening to auto-generate a matching prefix for a
                // namespace that has never appeared in this document before (e.g. a fixture whose
                // docProps/core.xml has no pre-existing dcterms:* elements at all).
                var dcTermsPrefix = EnsureNamespaceDeclared(
                    coreRoot,
                    OpcDocumentProperties.DublinCoreTermsNamespace,
                    "dcterms");
                coreRoot.Add(new XElement(
                    modifiedName,
                    new XAttribute(OpcDocumentProperties.XmlSchemaInstanceNamespace + "type", $"{dcTermsPrefix}:W3CDTF"),
                    modifiedValue));
            }
            else
            {
                modifiedElement.SetValue(modifiedValue);
            }

            var revisionName = OpcDocumentProperties.CorePropertiesNamespace + "revision";
            var revisionElement = coreRoot.Element(revisionName);
            var currentRevision = int.TryParse(
                revisionElement?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedRevision)
                ? parsedRevision
                : 0;
            var nextRevision = (currentRevision + 1).ToString(CultureInfo.InvariantCulture);
            if (revisionElement is null)
                coreRoot.Add(new XElement(revisionName, nextRevision));
            else
                revisionElement.SetValue(nextRevision);

            XlsxPackageXmlEditor.ReplaceXml(archive, OpcPackageProperties.CorePropertiesZipEntry, coreXml);
        }

        // Returns the prefix already bound (on this element or an ancestor) to the given
        // namespace, or declares it under preferredPrefix on this element and returns that.
        private static string EnsureNamespaceDeclared(XElement element, XNamespace ns, string preferredPrefix)
        {
            var existingPrefix = element.GetPrefixOfNamespace(ns);
            if (existingPrefix is not null)
                return existingPrefix;

            element.SetAttributeValue(XNamespace.Xmlns + preferredPrefix, ns.NamespaceName);
            return preferredPrefix;
        }

        private static void NormalizePatchWorksheetProtection(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetProtectionNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetProtectedRanges(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetProtectedRangeNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetScenarios(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetScenarioNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetSmartTags(ZipArchive archive) =>
            XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetSmartTagNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetPageBreaks(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetPageBreakNormalizer.NormalizeWorksheetRoot);

        private static bool HasPartBackedSingleXmlCells(ZipArchive archive, string worksheetPath)
        {
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return false;

            try
            {
                var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
                return relationshipsXml.Root?
                    .Elements(packageRelNs + "Relationship")
                    .Any(relationship =>
                    {
                        if (!string.Equals(
                                relationship.Attribute("Type")?.Value,
                                SingleCellTableRelationshipType,
                                StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(
                                relationship.Attribute("TargetMode")?.Value,
                                "External",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }

                        var target = relationship.Attribute("Target")?.Value;
                        return !string.IsNullOrWhiteSpace(target) &&
                               archive.GetEntry(XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target)) is not null;
                    }) == true;
            }
            catch
            {
                return false;
            }
        }

        private static void NormalizePatchSingleCellTableParts(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var singleCellTableEntry in archive.Entries.Where(IsSingleCellTableXmlEntry).ToList())
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(singleCellTableEntry);
                var root = tableXml.Root;
                if (root is null || root.Name != workbookNs + "singleXmlCells")
                    continue;

                var attributes = root.Attributes()
                    .Where(attribute => !attribute.IsNamespaceDeclaration)
                    .ToList();
                if (attributes.Count == 0)
                    continue;

                foreach (var attribute in attributes)
                    attribute.Remove();
                XlsxPackageXmlEditor.ReplaceXml(archive, singleCellTableEntry.FullName, tableXml);
            }
        }

        private static void NormalizePatchWorksheetSheetViews(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(
                archive,
                XlsxWorksheetSheetViewNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetSortStates(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetSortStateNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchWorksheetAutoFilters(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(archive, XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchStructuredTableAutoFilters(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var tableEntry in archive.Entries.Where(IsStructuredTableXmlEntry).ToList())
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
                var root = tableXml.Root;
                if (root is null)
                    continue;

                var autoFilter = root.Element(workbookNs + "autoFilter");
                if (autoFilter is not null &&
                    XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter))
                {
                    XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
                }
            }
        }

        private static void NormalizePatchStructuredTableSortStates(ZipArchive archive)
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var tableEntry in archive.Entries.Where(IsStructuredTableXmlEntry).ToList())
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
                var root = tableXml.Root;
                if (root is null)
                    continue;

                var sortState = root.Element(workbookNs + "sortState");
                if (sortState is not null &&
                    XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState))
                {
                    XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
                }
            }
        }

        private static void NormalizePatchStructuredTableMetadata(ZipArchive archive)
            => XlsxStructuredTableSchemaNormalizer.NormalizePackage(archive);

        private static void NormalizePatchExternalLinks(ZipArchive archive)
            => XlsxExternalLinkSchemaNormalizer.NormalizePackage(archive);

        private static void NormalizePatchWorksheetDataConsolidation(ZipArchive archive)
            => XlsxWorksheetHeaderNormalization.NormalizeWorksheets(
                archive,
                XlsxWorksheetDataConsolidationNormalizer.NormalizeWorksheetRoot);

        private static void NormalizePatchOfficeRevisionAttributes(
            ZipArchive archive,
            XlsxOfficeRevisionAttributeFacts facts)
        {
            if (facts.HasWorkbookAttributes)
                RemovePatchOfficeRevisionAttributes(archive, "xl/workbook.xml");

            foreach (var worksheetPath in facts.WorksheetPaths)
                RemovePatchOfficeRevisionAttributes(archive, worksheetPath);
        }

        private static void RemovePatchOfficeRevisionAttributes(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path);
            if (entry is null)
                return;

            if (TryReplacePatchOfficeRevisionAttributesStreaming(archive, entry))
                return;

            entry = archive.GetEntry(path);
            if (entry is null)
                return;

            var document = XlsxPackageXmlEditor.LoadXml(entry);
            if (document.Root is not null && RemoveOfficeRevisionAttributes(document.Root))
                XlsxPackageXmlEditor.ReplaceXml(archive, path, document);
        }

        private static bool TryReplacePatchOfficeRevisionAttributesStreaming(
            ZipArchive archive,
            ZipArchiveEntry entry)
        {
            using var rewritten = new MemoryStream();
            bool changed;
            bool hasOfficeRevisionElements;
            try
            {
                using (var source = entry.Open())
                {
                    changed = WritePatchXmlWithoutOfficeRevisionAttributes(
                        source,
                        rewritten,
                        out hasOfficeRevisionElements);
                }
            }
            catch
            {
                return false;
            }

            if (hasOfficeRevisionElements)
                return false;

            if (!changed)
                return true;

            var path = entry.FullName;
            entry.Delete();
            var replacement = archive.CreateEntry(path, CompressionLevel.Optimal);
            rewritten.Position = 0;
            using var replacementStream = replacement.Open();
            rewritten.CopyTo(replacementStream);
            return true;
        }

        private static bool WritePatchXmlWithoutOfficeRevisionAttributes(
            Stream source,
            Stream target,
            out bool hasOfficeRevisionElements)
        {
            var changed = false;
            hasOfficeRevisionElements = false;
            var inScopeRevisionPrefixes = new Stack<HashSet<string>>();
            using var reader = XmlReader.Create(source, SecureXmlReaderSettings.Create());
            using var writer = XmlWriter.Create(target, CreatePatchPackageXmlWriterSettings());

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        if (IsOfficeRevisionNamespace(reader.NamespaceURI))
                            hasOfficeRevisionElements = true;

                        changed |= WritePatchElementWithoutOfficeRevisionAttributes(
                            reader,
                            writer,
                            inScopeRevisionPrefixes);
                        break;

                    case XmlNodeType.EndElement:
                        writer.WriteFullEndElement();
                        if (inScopeRevisionPrefixes.Count > 0)
                            inScopeRevisionPrefixes.Pop();
                        break;

                    case XmlNodeType.Text:
                        writer.WriteString(reader.Value);
                        break;

                    case XmlNodeType.CDATA:
                        writer.WriteCData(reader.Value);
                        break;

                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        writer.WriteWhitespace(reader.Value);
                        break;

                    case XmlNodeType.Comment:
                        writer.WriteComment(reader.Value);
                        break;

                    case XmlNodeType.ProcessingInstruction:
                        if (!string.Equals(reader.Name, "xml", StringComparison.OrdinalIgnoreCase))
                            writer.WriteProcessingInstruction(reader.Name, reader.Value);
                        break;

                    case XmlNodeType.DocumentType:
                        writer.WriteDocType(
                            reader.Name,
                            reader.GetAttribute("PUBLIC"),
                            reader.GetAttribute("SYSTEM"),
                            reader.Value);
                        break;

                    case XmlNodeType.EntityReference:
                        writer.WriteEntityRef(reader.Name);
                        break;
                }
            }

            writer.Flush();
            return changed;
        }

        private static bool WritePatchElementWithoutOfficeRevisionAttributes(
            XmlReader reader,
            XmlWriter writer,
            Stack<HashSet<string>> inScopeRevisionPrefixes)
        {
            var changed = false;
            var attributes = ReadCurrentAttributes(reader);
            var revisionPrefixes = inScopeRevisionPrefixes.Count > 0
                ? new HashSet<string>(inScopeRevisionPrefixes.Peek(), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                if (!attribute.IsNamespaceDeclaration ||
                    string.Equals(attribute.LocalName, "xmlns", StringComparison.Ordinal))
                    continue;

                if (IsOfficeRevisionNamespace(attribute.Value))
                    revisionPrefixes.Add(attribute.LocalName);
                else
                    revisionPrefixes.Remove(attribute.LocalName);
            }

            writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
            foreach (var attribute in attributes)
            {
                if (attribute.IsNamespaceDeclaration && IsOfficeRevisionNamespace(attribute.Value))
                {
                    changed = true;
                    continue;
                }

                if (!attribute.IsNamespaceDeclaration && IsOfficeRevisionNamespace(attribute.NamespaceUri))
                {
                    changed = true;
                    continue;
                }

                if (IsMarkupCompatibilityIgnorableAttribute(attribute))
                {
                    var filteredValue = RemoveOfficeRevisionIgnorablePrefixes(attribute.Value, revisionPrefixes);
                    if (string.IsNullOrEmpty(filteredValue))
                    {
                        changed = true;
                        continue;
                    }

                    if (!string.Equals(filteredValue, attribute.Value, StringComparison.Ordinal))
                        changed = true;

                    writer.WriteAttributeString(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, filteredValue);
                    continue;
                }

                writer.WriteAttributeString(attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, attribute.Value);
            }

            if (reader.IsEmptyElement)
                writer.WriteEndElement();
            else
                inScopeRevisionPrefixes.Push(revisionPrefixes);

            return changed;
        }

        private static IReadOnlyList<PatchXmlAttribute> ReadCurrentAttributes(XmlReader reader)
        {
            if (!reader.HasAttributes)
                return [];

            var attributes = new List<PatchXmlAttribute>(reader.AttributeCount);
            for (var index = 0; index < reader.AttributeCount; index++)
            {
                reader.MoveToAttribute(index);
                attributes.Add(new PatchXmlAttribute(
                    reader.Prefix,
                    reader.LocalName,
                    reader.NamespaceURI,
                    reader.Value,
                    reader.Prefix == "xmlns" ||
                    (reader.Prefix.Length == 0 &&
                     string.Equals(reader.LocalName, "xmlns", StringComparison.Ordinal))));
            }

            reader.MoveToElement();
            return attributes;
        }

        private static bool IsMarkupCompatibilityIgnorableAttribute(PatchXmlAttribute attribute) =>
            string.Equals(attribute.LocalName, "Ignorable", StringComparison.Ordinal) &&
            string.Equals(
                attribute.NamespaceUri,
                "http://schemas.openxmlformats.org/markup-compatibility/2006",
                StringComparison.Ordinal);

        private static string RemoveOfficeRevisionIgnorablePrefixes(
            string value,
            IReadOnlySet<string> revisionPrefixes)
        {
            if (revisionPrefixes.Count == 0)
                return value;

            var retainedPrefixes = value
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(prefix => !revisionPrefixes.Contains(prefix))
                .ToArray();
            return string.Join(" ", retainedPrefixes);
        }

        private static XmlWriterSettings CreatePatchPackageXmlWriterSettings() => new()
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            CloseOutput = false
        };

        private static bool RemoveOfficeRevisionAttributes(XElement root)
        {
            var changed = false;

            foreach (var attribute in root
                         .DescendantsAndSelf()
                         .SelectMany(element => element.Attributes())
                         .Where(IsOfficeRevisionAttribute)
                         .ToList())
            {
                attribute.Remove();
                changed = true;
            }

            if (RemoveOfficeRevisionIgnorablePrefixes(root))
                changed = true;

            foreach (var namespaceDeclaration in root
                         .DescendantsAndSelf()
                         .SelectMany(element => element.Attributes())
                         .Where(attribute =>
                             attribute.IsNamespaceDeclaration &&
                             IsOfficeRevisionNamespace(attribute.Value) &&
                             !NamespaceIsUsed(root, attribute.Value))
                         .ToList())
            {
                namespaceDeclaration.Remove();
                changed = true;
            }

            return changed;
        }

        private readonly record struct PatchXmlAttribute(
            string Prefix,
            string LocalName,
            string NamespaceUri,
            string Value,
            bool IsNamespaceDeclaration);

        private static bool IsOfficeRevisionAttribute(XAttribute attribute) =>
            !attribute.IsNamespaceDeclaration &&
            IsOfficeRevisionNamespace(attribute.Name.NamespaceName);

        private static bool IsOfficeRevisionNamespace(string namespaceName) =>
            namespaceName.StartsWith("http://schemas.microsoft.com/office/spreadsheetml/", StringComparison.Ordinal) &&
            namespaceName.Contains("/revision", StringComparison.Ordinal);

        private static bool NamespaceIsUsed(XElement root, string namespaceName) =>
            root.DescendantsAndSelf().Any(element =>
                element.Name.NamespaceName == namespaceName ||
                element.Attributes().Any(attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    attribute.Name.NamespaceName == namespaceName));

        private static bool RemoveOfficeRevisionIgnorablePrefixes(XElement root)
        {
            var changed = false;
            RemoveOfficeRevisionIgnorablePrefixes(
                root,
                new Dictionary<string, string>(StringComparer.Ordinal),
                ref changed);
            return changed;

            static void RemoveOfficeRevisionIgnorablePrefixes(
                XElement element,
                Dictionary<string, string> inheritedRevisionNamespacesByPrefix,
                ref bool changed)
            {
                XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
                var revisionNamespacesByPrefix = new Dictionary<string, string>(
                    inheritedRevisionNamespacesByPrefix,
                    StringComparer.Ordinal);
                foreach (var namespaceDeclaration in element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                {
                    var prefix = namespaceDeclaration.Name.LocalName;
                    if (string.Equals(prefix, "xmlns", StringComparison.Ordinal))
                        continue;

                    if (IsOfficeRevisionNamespace(namespaceDeclaration.Value))
                        revisionNamespacesByPrefix[prefix] = namespaceDeclaration.Value;
                    else
                        revisionNamespacesByPrefix.Remove(prefix);
                }

                var ignorable = element.Attribute(markupCompatNs + "Ignorable");
                if (ignorable is not null)
                {
                    var retainedPrefixes = ignorable.Value
                        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Where(prefix =>
                            !revisionNamespacesByPrefix.TryGetValue(prefix, out var namespaceName) ||
                            HasOfficeRevisionElementInNamespace(element, namespaceName))
                        .ToArray();
                    var retainedValue = string.Join(" ", retainedPrefixes);
                    if (!string.Equals(ignorable.Value, retainedValue, StringComparison.Ordinal))
                    {
                        if (retainedPrefixes.Length == 0)
                            ignorable.Remove();
                        else
                            ignorable.Value = retainedValue;

                        changed = true;
                    }
                }

                foreach (var child in element.Elements())
                    RemoveOfficeRevisionIgnorablePrefixes(child, revisionNamespacesByPrefix, ref changed);
            }
        }

        private static bool HasOfficeRevisionElementInNamespace(XElement root, string namespaceName) =>
            root.DescendantsAndSelf()
                .Any(element => string.Equals(element.Name.NamespaceName, namespaceName, StringComparison.Ordinal));

        private static bool AllowsCellPatchSaveForPackage(
            byte[] package,
            int offset,
            int count,
            Workbook workbook)
            => AllowsCellPatchSaveForPackage(
                package,
                offset,
                count,
                workbook,
                preserveSourceDrawingPackageParts: false,
                out _,
                out _);

        private static bool AllowsCellPatchSaveForPackage(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            out string? blockReason)
            => AllowsCellPatchSaveForPackage(
                package,
                offset,
                count,
                workbook,
                preserveSourceDrawingPackageParts: false,
                out blockReason,
                out _);

        private static bool AllowsCellPatchSaveForPackage(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            bool preserveSourceDrawingPackageParts,
            out string? blockReason,
            out XlsxOfficeRevisionAttributeFacts? officeRevisionAttributes)
        {
            blockReason = null;
            officeRevisionAttributes = null;
            if (WorkbookRequiresFullSavePostProcessing(workbook, out blockReason))
                return false;

            try
            {
                using var packageStream = new MemoryStream(package, offset, count, writable: false);
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
                return PackageAllowsCellPatchSave(
                    archive,
                    workbook,
                    preserveSourceDrawingPackageParts,
                    out blockReason,
                    out officeRevisionAttributes);
            }
            catch
            {
                blockReason = "package_guard_exception";
                return false;
            }
        }

        private static bool WorkbookRequiresFullSavePostProcessing(Workbook workbook) =>
            WorkbookRequiresFullSavePostProcessing(workbook, out _);

        private static bool WorkbookRequiresFullSavePostProcessing(Workbook workbook, out string? blockReason)
        {
            blockReason = null;
            if (WorkbookHasPatchUnsafePivotFeatures(workbook))
            {
                blockReason = "workbook_postprocessing_pivots";
                return true;
            }

            if (WorkbookHasPatchUnsafeCustomViews(workbook))
            {
                blockReason = "workbook_postprocessing_custom_views";
                return true;
            }

            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Charts.Any(chart => ChartRequiresFullSavePostProcessing(workbook, sheet, chart)))
                {
                    blockReason = "workbook_postprocessing_charts";
                    return true;
                }

                if (SheetHasPatchUnsafeDrawingObjects(sheet))
                {
                    blockReason = "workbook_postprocessing_drawings";
                    return true;
                }

            }

            return false;
        }

        private static bool WorkbookHasPatchUnsafeCustomViews(Workbook workbook)
        {
            foreach (var customView in workbook.CustomViews)
            {
                if (customView is null)
                    return true;

                var normalizedId = XlsxCustomViewMapper.NormalizeId(customView.Id);
                if (!string.IsNullOrWhiteSpace(customView.Id) &&
                    !string.Equals(customView.Id, normalizedId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool WorkbookHasPatchUnsafePivotFeatures(Workbook workbook)
        {
            if (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0)
                return true;

            foreach (var cache in workbook.PivotCaches)
            {
                if (cache.SourceType != PivotCacheSourceType.WorksheetRange ||
                    string.IsNullOrWhiteSpace(cache.SourceSheetName) ||
                    string.IsNullOrWhiteSpace(cache.SourceReference) ||
                    !string.IsNullOrWhiteSpace(cache.SourceTableName) ||
                    cache.ConnectionId is not null ||
                    cache.IsOlap)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ChartRequiresFullSavePostProcessing(Workbook workbook, Sheet sheet, ChartModel chart) =>
            chart.ExternalData is not null ||
            chart.UserShapes is not null ||
            (chart.IsPivotChart && !IsPatchSafePivotChartModel(workbook, sheet, chart));

        private static bool IsPatchSafePivotChartModel(Workbook workbook, Sheet chartSheet, ChartModel chart)
        {
            if (!chart.IsPivotChart)
                return true;

            var pivotTableName = chart.PivotTableName;
            if (string.IsNullOrWhiteSpace(pivotTableName) ||
                chart.PivotCacheId is not { } pivotCacheId)
            {
                return false;
            }

            var pivot = FindPivotTableByChartSource(workbook, chartSheet, chart, pivotTableName, pivotCacheId);
            return pivot is not null &&
                   WorkbookContainsPivotCache(workbook, pivotCacheId);
        }

        private static bool SheetHasPatchUnsafeDrawingObjects(Sheet sheet)
        {
            if (sheet.TextBoxes.Any(textBox => !IsPatchSafeSourceTextBox(textBox)) ||
                sheet.DrawingShapes.Any(shape => !IsPatchSafeSourceDrawingShape(shape)))
            {
                return true;
            }

            foreach (var picture in sheet.Pictures)
            {
                if (!picture.IsSourceLoaded ||
                    picture.Kind != PictureKind.Image ||
                    picture.IsLinkedToSourceRange ||
                    picture.LinkedSourceRange is not null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PackageAllowsCellPatchSave(ZipArchive archive, Workbook workbook) =>
            PackageAllowsCellPatchSave(
                archive,
                workbook,
                preserveSourceDrawingPackageParts: false,
                out _,
                out _);

        private static bool PackageAllowsCellPatchSave(
            ZipArchive archive,
            Workbook workbook,
            out string? blockReason)
            => PackageAllowsCellPatchSave(
                archive,
                workbook,
                preserveSourceDrawingPackageParts: false,
                out blockReason,
                out _);

        private static bool PackageAllowsCellPatchSave(
            ZipArchive archive,
            Workbook workbook,
            bool preserveSourceDrawingPackageParts,
            out string? blockReason,
            out XlsxOfficeRevisionAttributeFacts? officeRevisionAttributes)
        {
            blockReason = null;
            officeRevisionAttributes = null;
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
            {
                blockReason = "package_guard_missing_workbook";
                return false;
            }

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            if (workbookXml.Root is null)
            {
                blockReason = "package_guard_workbook_xml";
                return false;
            }

            // A Protect/Unprotect Workbook command changes IsStructureProtected/
            // StructureProtectionPassword/ProtectionMetadata on the live model, but the patch-save
            // path below only carries the *original* xl/workbook.xml workbookProtection element
            // forward (cosmetically normalized) -- it never re-derives that element from the
            // model. Detect a protection-state delta against the source bytes here and force the
            // full ClosedXML save (which does call XlsxWorkbookMetadataWriter.ApplyProtection off
            // the current model) instead of silently keeping a stale verifier. See
            // FreeXCleanupMED15Tests.ProtectWorkbookCommand_AfterUnprotectingModernHashWorkbook_DropsStaleVerifierForOldPassword.
            var sourceProtection = XlsxWorkbookMetadataReader.LoadWorkbookMetadata(archive);
            if (sourceProtection.Protection.IsStructureProtected != workbook.IsStructureProtected ||
                !string.Equals(sourceProtection.Protection.PasswordHash, workbook.StructureProtectionPassword, StringComparison.Ordinal) ||
                !string.Equals(
                    sourceProtection.ProtectionMetadata?.Get("workbookProtection"),
                    workbook.ProtectionMetadata?.Get("workbookProtection"),
                    StringComparison.Ordinal))
            {
                blockReason = "workbook_postprocessing_protection_changed";
                return false;
            }

            var hasWorkbookOfficeRevisionAttributes = HasOfficeRevisionAttributes(workbookXml.Root);

            var worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);
            if (worksheetPathMap is null)
            {
                blockReason = "package_guard_worksheet_path_map";
                return false;
            }

            var sheetsByWorksheetPath = new Dictionary<string, Sheet>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in workbook.Sheets)
            {
                if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                {
                    blockReason = "package_guard_sheet_path_missing";
                    return false;
                }

                sheetsByWorksheetPath[worksheetPath] = sheet;
            }

            var allowedVmlDrawingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedDrawingPackagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedChartPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedPivotPackagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var officeRevisionWorksheetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!RichDataPackageGraphAllowsCellPatchSave(archive, packageRelNs, out blockReason))
                return false;

            foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry))
            {
                var worksheetPath = XlsxPackagePath.NormalizeEntryPath(worksheetEntry);
                if (!sheetsByWorksheetPath.TryGetValue(worksheetPath, out var sheet))
                {
                    blockReason = "package_guard_unmatched_worksheet_part";
                    return false;
                }

                // A Protect/Unprotect Sheet command changes IsProtected/ProtectionPassword/
                // ProtectionMetadata on the live model, but the patch-save path below
                // (NormalizePatchWorksheetProtection) only cosmetically normalizes the *original*
                // sheetProtection element -- it never re-derives it from the model (that only
                // happens on the full/source-independent save path via
                // XlsxWorksheetProtectionMetadataWriter). Detect a protection-state delta against
                // the source bytes here and force the full ClosedXML save instead of silently
                // keeping a stale verifier or dropping a freshly-typed password. Mirrors the
                // workbook-level guard above (workbook_postprocessing_protection_changed). See
                // FreeXR11B7Tests.ProtectSheetCommand_AfterUnprotectingModernHashSheet_DropsStaleVerifierForOldPassword.
                if (!TryReadSheetProtectionPackageGuardInfo(worksheetEntry, workbookNs, out var sourceSheetProtection))
                {
                    blockReason = "package_guard_worksheet_xml";
                    return false;
                }

                if (WorksheetProtectionStateChanged(sourceSheetProtection, sheet))
                {
                    blockReason = "worksheet_postprocessing_protection_changed";
                    return false;
                }

                // R59 io-protection-5-1/5-2: a Protect-Sheet permission-flag edit or an Allow-Edit-
                // Range add/remove/password-change is likewise never re-derived from the model by
                // the patch-save normalizers (NormalizePatchWorksheetProtection only cosmetically
                // normalizes the *original* sheetProtection attributes;
                // NormalizePatchWorksheetProtectedRanges only cosmetically normalizes the *original*
                // protectedRanges element) -- force the full ClosedXML save (which does call
                // XlsxWorksheetProtectionMetadataWriter/XlsxAllowEditRangeMapper off the current
                // model) only when one of those genuinely differs from the source. A plain cell edit
                // on a protected sheet whose permissions/ranges are unchanged must still take the
                // cheap cell-patch path below.
                if (WorksheetProtectionPermissionsOrAllowEditRangesChanged(sourceSheetProtection, sheet))
                {
                    blockReason = "worksheet_postprocessing_protection_permissions_changed";
                    return false;
                }

                if (!TryReadWorksheetPackageGuardInfo(
                        worksheetEntry,
                        workbookNs,
                        relNs,
                        out var worksheetGuardInfo))
                {
                    blockReason = "package_guard_worksheet_xml";
                    return false;
                }

                if (worksheetGuardInfo.DrawingRelationshipIds.Count > 1 && !preserveSourceDrawingPackageParts)
                {
                    blockReason = "package_guard_drawing";
                    return false;
                }

                foreach (var drawingRelationshipId in worksheetGuardInfo.DrawingRelationshipIds)
                {
                    var allowsDrawing = preserveSourceDrawingPackageParts
                        ? TryAddPreservedDrawingPackagePaths(
                            archive,
                            worksheetPath,
                            drawingRelationshipId,
                            allowedDrawingPackagePaths,
                            allowedChartPaths,
                            packageRelNs)
                        : TryAddPatchSafeDrawingPackagePaths(
                            archive,
                            worksheetPath,
                            drawingRelationshipId,
                            sheet,
                            allowedDrawingPackagePaths,
                            allowedChartPaths,
                            packageRelNs);
                    if (allowsDrawing)
                        continue;

                    blockReason = "package_guard_drawing";
                    return false;
                }

                if (worksheetGuardInfo.DrawingRelationshipIds.Count == 0 &&
                    (sheet.Charts.Count > 0 ||
                     sheet.Pictures.Count > 0 ||
                     sheet.TextBoxes.Count > 0 ||
                     sheet.DrawingShapes.Count > 0))
                {
                    blockReason = "package_guard_drawing";
                    return false;
                }

                if (worksheetGuardInfo.HeaderFooterVmlRelationshipIds.Count > 1 ||
                    (worksheetGuardInfo.HeaderFooterVmlRelationshipIds.Count == 1 &&
                     !TryAddPatchSafeHeaderFooterVmlDrawingPaths(
                         archive,
                         worksheetPath,
                         worksheetGuardInfo.HeaderFooterVmlRelationshipIds[0],
                         sheet,
                         allowedVmlDrawingPaths)))
                {
                    blockReason = "package_guard_header_footer_vml";
                    return false;
                }

                if (worksheetGuardInfo.HasQueryTableParts)
                {
                    blockReason = "package_guard_query_table";
                    return false;
                }

                if (HasUnsupportedWorksheetTableParts(archive, worksheetPath, worksheetGuardInfo, workbookNs, sheet))
                {
                    blockReason = "package_guard_table_parts";
                    return false;
                }

                if (sheet.PivotTables.Count > 0 &&
                    !HasCanonicalWorksheetPartPath(workbook, sheet, worksheetPath))
                {
                    blockReason = "package_guard_pivot_worksheet_path";
                    return false;
                }

                if (!TryAddPatchSafePivotPackagePaths(
                        archive,
                        worksheetPath,
                        sheet,
                        workbook,
                        allowedPivotPackagePaths,
                        packageRelNs))
                {
                    blockReason = "package_guard_pivot_parts";
                    return false;
                }

                if (worksheetGuardInfo.HasOfficeRevisionAttributes)
                    officeRevisionWorksheetPaths.Add(worksheetPath);

                if (worksheetGuardInfo.LegacyDrawingRelationshipIds.Count > 1 ||
                    (worksheetGuardInfo.LegacyDrawingRelationshipIds.Count == 1 &&
                    !TryAddPatchPreservedLegacyDrawingVmlPaths(
                        archive,
                        worksheetPath,
                        worksheetGuardInfo.LegacyDrawingRelationshipIds[0],
                        allowedVmlDrawingPaths)))
                {
                    blockReason = "package_guard_legacy_drawing_vml";
                    return false;
                }
            }

            foreach (var entry in archive.Entries)
            {
                var path = XlsxPackagePath.NormalizeEntryPath(entry);
                if (XlsxDigitalSignaturePackagePolicy.IsDigitalSignaturePackagePath(path))
                {
                    blockReason = "package_guard_digital_signatures";
                    return false;
                }

                if (TryGetPatchUnsafePackagePartReason(
                        path,
                        allowedVmlDrawingPaths,
                        allowedDrawingPackagePaths,
                        allowedChartPaths,
                        allowedPivotPackagePaths,
                        out blockReason))
                {
                    return false;
                }

                if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
                    XlsxDigitalSignaturePackagePolicy.HasDigitalSignatureRelationship(entry))
                {
                    blockReason = "package_guard_digital_signatures";
                    return false;
                }

                if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
                    !allowedPivotPackagePaths.Contains(path) &&
                    !IsValidRelationshipPart(entry))
                {
                    blockReason = "package_guard_relationships";
                    return false;
                }
            }

            officeRevisionAttributes = new XlsxOfficeRevisionAttributeFacts(
                hasWorkbookOfficeRevisionAttributes,
                officeRevisionWorksheetPaths);
            return true;
        }

        private static bool HasCanonicalWorksheetPartPath(Workbook workbook, Sheet sheet, string worksheetPath)
        {
            for (var index = 0; index < workbook.Sheets.Count; index++)
            {
                if (workbook.Sheets[index].Id != sheet.Id)
                    continue;

                return string.Equals(
                    worksheetPath,
                    $"xl/worksheets/sheet{(index + 1).ToString(CultureInfo.InvariantCulture)}.xml",
                    StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool IsPatchUnsafePackagePart(
            string path,
            IReadOnlySet<string> allowedVmlDrawingPaths,
            IReadOnlySet<string> allowedDrawingPackagePaths,
            IReadOnlySet<string> allowedChartPaths,
            IReadOnlySet<string> allowedPivotPackagePaths) =>
            (path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
             !allowedVmlDrawingPaths.Contains(path) &&
             !allowedDrawingPackagePaths.Contains(path)) ||
            (path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
             !allowedChartPaths.Contains(path)) ||
            ((path.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase)) &&
             !allowedPivotPackagePaths.Contains(path)) ||
            path.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase);

        private static bool TryGetPatchUnsafePackagePartReason(
            string path,
            IReadOnlySet<string> allowedVmlDrawingPaths,
            IReadOnlySet<string> allowedDrawingPackagePaths,
            IReadOnlySet<string> allowedChartPaths,
            IReadOnlySet<string> allowedPivotPackagePaths,
            out string? blockReason)
        {
            blockReason = null;
            if (path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
                !allowedVmlDrawingPaths.Contains(path) &&
                !allowedDrawingPackagePaths.Contains(path))
            {
                blockReason = "package_guard_drawing_parts";
                return true;
            }

            if (path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
                !allowedChartPaths.Contains(path))
            {
                blockReason = "package_guard_chart_parts";
                return true;
            }

            if ((path.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) ||
                 path.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase)) &&
                !allowedPivotPackagePaths.Contains(path))
            {
                blockReason = "package_guard_pivot_parts";
                return true;
            }

            if (path.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "package_guard_query_table_parts";
                return true;
            }

            if (path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase))
            {
                blockReason = "package_guard_revision_parts";
                return true;
            }

            return false;
        }

        private static bool TryAddPatchSafeDrawingPackagePaths(
            ZipArchive archive,
            string worksheetPath,
            XElement drawing,
            Sheet sheet,
            HashSet<string> allowedDrawingPackagePaths,
            HashSet<string> allowedChartPaths,
            XNamespace relNs,
            XNamespace packageRelNs)
        {
            var relationshipId = drawing.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            return TryAddPatchSafeDrawingPackagePaths(
                archive,
                worksheetPath,
                relationshipId,
                sheet,
                allowedDrawingPackagePaths,
                allowedChartPaths,
                packageRelNs);
        }

        private static bool TryAddPatchSafeDrawingPackagePaths(
            ZipArchive archive,
            string worksheetPath,
            string relationshipId,
            Sheet sheet,
            HashSet<string> allowedDrawingPackagePaths,
            HashSet<string> allowedChartPaths,
            XNamespace packageRelNs)
        {
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return false;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return false;

            var drawingRelationship = FindInternalRelationshipByIdAndType(
                relationshipsRoot.Elements(packageRelNs + "Relationship"),
                relationshipId,
                DrawingRelationshipType);
            var drawingTarget = drawingRelationship?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(drawingTarget))
                return false;

            var drawingPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
            if (!drawingPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                drawingPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                !drawingPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var drawingEntry = archive.GetEntry(drawingPath);
            if (drawingEntry is null)
                return false;

            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            var drawingRoot = drawingXml.Root;
            if (drawingRoot is null)
                return false;

            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
            const string diagramGraphicDataUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
            XNamespace markupCompatNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
            if (drawingRoot.Name != spreadsheetDrawingNs + "wsDr" ||
                drawingXml.Descendants(spreadsheetDrawingNs + "grpSp").Any())
            {
                return false;
            }

            var chartElements = drawingXml
                .Descendants()
                .Where(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart")
                .ToList();
            var diagramGraphicDataElements = drawingXml
                .Descendants(drawingNs + "graphicData")
                .Where(element => string.Equals(element.Attribute("uri")?.Value, diagramGraphicDataUri, StringComparison.Ordinal))
                .ToList();
            var pictureElements = drawingXml.Descendants(spreadsheetDrawingNs + "pic").ToList();
            var contentPartElements = drawingXml.Descendants(spreadsheetDrawingNs + "contentPart").ToList();
            var (sourceTextBoxes, sourceShapes) = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml);

            // Loaded here (rather than alongside the relationship-graph walk further below) so the picture
            // anchors/geometry can be compared against the in-memory sheet before any drawing part is
            // deemed patch-safe: a resized/moved picture must force a rewrite just like a resized shape.
            var drawingRelsPath = XlsxPackagePath.GetRelationshipPartPath(drawingPath);
            var drawingRelsEntry = archive.GetEntry(drawingRelsPath);
            var drawingRelsXmlForPictures = drawingRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(drawingRelsEntry)
                : null;
            var sourcePictures = XlsxWorksheetDrawingPartReader.ReadPictureParts(archive, drawingPath, drawingXml, drawingRelsXmlForPictures);

            if (chartElements.Count != sheet.Charts.Count ||
                pictureElements.Count < sheet.Pictures.Count ||
                sourcePictures.Count != sheet.Pictures.Count ||
                sourceTextBoxes.Count != sheet.TextBoxes.Count ||
                sourceShapes.Count != sheet.DrawingShapes.Count ||
                sheet.Pictures.Any(picture => !IsPatchSafeSourcePicture(picture)) ||
                !SourcePicturesMatchSheet(sourcePictures, sheet) ||
                !SourceTextBoxesMatchSheet(sourceTextBoxes, sheet) ||
                !SourceDrawingShapesMatchSheet(sourceShapes, sheet))
            {
                return false;
            }

            var anchorElements = drawingRoot
                .Elements()
                .Where(element => element.Name == spreadsheetDrawingNs + "oneCellAnchor" ||
                                  element.Name == spreadsheetDrawingNs + "twoCellAnchor" ||
                                  element.Name == spreadsheetDrawingNs + "absoluteAnchor")
                .ToList();
            if (drawingRoot.Elements().Any(element =>
                    element.Name.Namespace == spreadsheetDrawingNs &&
                    element.Name.LocalName.EndsWith("Anchor", StringComparison.Ordinal) &&
                    !anchorElements.Contains(element)))
            {
                return false;
            }

            foreach (var anchor in anchorElements)
            {
                var chartCount = anchor
                    .Descendants()
                    .Count(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart");
                var diagramCount = anchor
                    .Descendants(drawingNs + "graphicData")
                    .Count(element => string.Equals(element.Attribute("uri")?.Value, diagramGraphicDataUri, StringComparison.Ordinal));
                var pictureCount = anchor.Descendants(spreadsheetDrawingNs + "pic").Count();
                var shapeCount = anchor
                    .Descendants(spreadsheetDrawingNs + "sp")
                    .Count(element => !element.Ancestors(markupCompatNs + "Fallback").Any());
                var connectorCount = anchor
                    .Descendants(spreadsheetDrawingNs + "cxnSp")
                    .Count(element => !element.Ancestors(markupCompatNs + "Fallback").Any());
                // Ink annotations (hand-drawn strokes anchored via <xdr:contentPart r:id="..."/>,
                // referencing an InkML part) are not modeled anywhere in the reader/writer. The
                // full-rewrite path (XlsxWorksheetDrawingObjectWriter) has no concept of them at all
                // and would silently drop the annotation. Count them here so an anchor containing
                // only a contentPart is still recognized as patch-safe (preserved verbatim) instead
                // of forcing a lossy full rewrite.
                var contentPartCount = anchor.Descendants(spreadsheetDrawingNs + "contentPart").Count();
                if (chartCount + diagramCount + pictureCount + shapeCount + connectorCount + contentPartCount == 0 ||
                    anchor.Descendants(spreadsheetDrawingNs + "graphicFrame").Count() != chartCount + diagramCount)
                {
                    return false;
                }
            }

            if ((chartElements.Count > 0 || pictureElements.Count > 0 || contentPartElements.Count > 0) && drawingRelsEntry is null)
                return false;

            var referencedRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
            var relationshipElements = Array.Empty<XElement>();
            if (drawingRelsEntry is not null)
            {
                var drawingRelsXml = drawingRelsXmlForPictures!;
                if (drawingRelsXml.Root is null)
                    return false;

                relationshipElements = drawingRelsXml.Root
                    .Elements(packageRelNs + "Relationship")
                    .ToArray();
            }

            foreach (var chartElement in chartElements)
            {
                var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
                var chartRelationshipType = chartElement.Name == chartExNs + "chart"
                    ? ChartExRelationshipType
                    : ChartRelationshipType;
                if (string.IsNullOrWhiteSpace(chartRelId) ||
                    !referencedRelationshipIds.Add(chartRelId) ||
                    !TryGetRelationship(
                        relationshipElements,
                        chartRelId,
                        chartRelationshipType,
                        out var chartTarget))
                {
                    return false;
                }

                var chartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartTarget);
                if (!chartPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
                    chartPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !chartPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(chartPath) is null)
                {
                    return false;
                }

                if (string.Equals(chartRelationshipType, ChartExRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryAddPatchSafeChartExPackagePaths(archive, chartPath, allowedChartPaths, packageRelNs))
                        return false;
                }
                else if (!TryAddPatchSafeChartPackagePaths(archive, chartPath, allowedChartPaths, packageRelNs))
                {
                    return false;
                }

                allowedChartPaths.Add(chartPath);
            }

            foreach (var graphicDataElement in diagramGraphicDataElements)
            {
                var relationshipIds = graphicDataElement
                    .DescendantsAndSelf()
                    .Attributes()
                    .Where(attribute => attribute.Name.Namespace == relNs)
                    .Select(attribute => attribute.Value)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
                if (relationshipIds.Length == 0 ||
                    !TryAddPatchSafeDiagramPackagePaths(
                        archive,
                        drawingPath,
                        relationshipElements,
                        relationshipIds,
                        referencedRelationshipIds))
                {
                    return false;
                }
            }

            foreach (var pictureElement in pictureElements)
            {
                var imageRelId = ReadFirstEmbeddedImageRelationshipId(pictureElement, drawingNs, relNs);
                if (string.IsNullOrWhiteSpace(imageRelId) ||
                    pictureElement.Descendants(drawingNs + "blip").Any(blip => blip.Attribute(relNs + "link") is not null) ||
                    !referencedRelationshipIds.Add(imageRelId) ||
                    !TryGetRelationship(
                        relationshipElements,
                        imageRelId,
                        ImageRelationshipType,
                        out var imageTarget))
                {
                    return false;
                }

                var imagePath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, imageTarget);
                if (!imagePath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(imagePath) is null)
                {
                    return false;
                }
            }

            // Ink annotations: <xdr:contentPart r:id="..."/> references an InkML (or similar) part
            // by relationship id. Never modeled in the drawing object model, so the only safe
            // handling is to preserve the anchor and its referenced part verbatim on the patch-save
            // path. Validate the relationship resolves to a real package part (any content type -
            // Office does not constrain contentPart targets to a single relationship type) and mark
            // the relationship id as referenced so it isn't rejected as orphaned below.
            foreach (var contentPartElement in contentPartElements)
            {
                var contentPartRelId = contentPartElement.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(contentPartRelId) ||
                    !referencedRelationshipIds.Add(contentPartRelId))
                {
                    return false;
                }

                var contentPartRelationship = relationshipElements.FirstOrDefault(element =>
                    RelationshipHasId(element, contentPartRelId) && RelationshipHasInternalTarget(element));
                var contentPartTarget = contentPartRelationship?.Attribute("Target")?.Value;
                if (contentPartRelationship is null ||
                    string.IsNullOrWhiteSpace(contentPartTarget))
                {
                    return false;
                }

                var contentPartPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, contentPartTarget);
                if (archive.GetEntry(contentPartPath) is null)
                    return false;
            }

            if (relationshipElements.Any(element =>
                    element.Attribute("Id")?.Value is not { } id ||
                    !referencedRelationshipIds.Contains(id) ||
                    element.Attribute("TargetMode") is not null))
            {
                return false;
            }

            allowedDrawingPackagePaths.Add(drawingPath);
            allowedDrawingPackagePaths.Add(drawingRelsPath);
            return true;
        }

        private static bool TryAddPreservedDrawingPackagePaths(
            ZipArchive archive,
            string worksheetPath,
            string relationshipId,
            HashSet<string> allowedDrawingPackagePaths,
            HashSet<string> allowedChartPaths,
            XNamespace packageRelNs)
        {
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return false;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return false;

            var drawingRelationship = FindInternalRelationshipByIdAndType(
                relationshipsRoot.Elements(packageRelNs + "Relationship"),
                relationshipId,
                DrawingRelationshipType);
            var drawingTarget = drawingRelationship?.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(drawingTarget))
                return false;

            var drawingPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
            if (!drawingPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                drawingPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                !drawingPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                archive.GetEntry(drawingPath) is null)
            {
                return false;
            }

            var seenPackagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return TryAddPreservedRelationshipPackageGraph(
                archive,
                drawingPath,
                allowedDrawingPackagePaths,
                allowedChartPaths,
                packageRelNs,
                seenPackagePaths);
        }

        private static bool TryAddPreservedRelationshipPackageGraph(
            ZipArchive archive,
            string sourcePartPath,
            HashSet<string> allowedDrawingPackagePaths,
            HashSet<string> allowedChartPaths,
            XNamespace packageRelNs,
            HashSet<string> seenPackagePaths)
        {
            if (!seenPackagePaths.Add(sourcePartPath))
                return true;

            if (archive.GetEntry(sourcePartPath) is null)
                return false;

            AddPreservedRelationshipPackagePath(sourcePartPath, allowedDrawingPackagePaths, allowedChartPaths);

            var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(sourcePartPath);
            var relationshipsEntry = archive.GetEntry(relationshipsPath);
            if (relationshipsEntry is null)
                return true;

            AddPreservedRelationshipPackagePath(relationshipsPath, allowedDrawingPackagePaths, allowedChartPaths);

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return false;

            foreach (var relationship in relationshipsRoot.Elements(packageRelNs + "Relationship"))
            {
                if (relationship.Attribute("TargetMode") is not null)
                    return false;

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    return false;

                var targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
                if (targetPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(targetPath) is null)
                {
                    return false;
                }

                AddPreservedRelationshipPackagePath(targetPath, allowedDrawingPackagePaths, allowedChartPaths);
                if (!TryAddPreservedRelationshipPackageGraph(
                        archive,
                        targetPath,
                        allowedDrawingPackagePaths,
                        allowedChartPaths,
                        packageRelNs,
                        seenPackagePaths))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddPreservedRelationshipPackagePath(
            string path,
            HashSet<string> allowedDrawingPackagePaths,
            HashSet<string> allowedChartPaths)
        {
            if (path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase))
                allowedDrawingPackagePaths.Add(path);
            else if (path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase))
                allowedChartPaths.Add(path);
        }

        private static bool TryAddPatchSafeDiagramPackagePaths(
            ZipArchive archive,
            string drawingPath,
            IReadOnlyList<XElement> relationshipElements,
            IReadOnlyList<string> relationshipIds,
            HashSet<string> referencedRelationshipIds)
        {
            var contentTypes = TryReadPackageContentTypes(archive);
            if (contentTypes is null)
                return false;

            var relationshipTypesById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in relationshipIds)
            {
                var relationship = FindRelationshipById(relationshipElements, id);
                var type = relationship?.Attribute("Type")?.Value;
                if (string.Equals(type, DiagramDataRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, DiagramLayoutRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, DiagramQuickStyleRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, DiagramColorsRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    relationshipTypesById[id] = type!;
                }
                else
                {
                    return false;
                }
            }

            var seenTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relationshipId, relationshipType) in relationshipTypesById)
            {
                if (!seenTypes.Add(relationshipType) ||
                    !referencedRelationshipIds.Add(relationshipId) ||
                    !TryGetRelationship(relationshipElements, relationshipId, relationshipType, out var target))
                {
                    return false;
                }

                var diagramPath = XlsxPackagePath.ResolveRelationshipTarget(drawingPath, target);
                if (!diagramPath.StartsWith("xl/diagrams/", StringComparison.OrdinalIgnoreCase) ||
                    diagramPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !diagramPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(diagramPath)) is not null ||
                    archive.GetEntry(diagramPath) is not { } diagramEntry ||
                    !DiagramPartHasExpectedContentType(contentTypes, diagramPath, relationshipType))
                {
                    return false;
                }

                try
                {
                    var diagramXml = XlsxPackageXmlEditor.LoadXml(diagramEntry);
                    if (diagramXml.Root is null)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return seenTypes.Contains(DiagramDataRelationshipType);
        }

        private static Dictionary<string, string>? TryReadPackageContentTypes(ZipArchive archive)
        {
            var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
            if (contentTypesEntry is null)
                return null;

            try
            {
                XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
                var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
                return contentTypesXml.Root?
                    .Elements(contentTypeNs + "Override")
                    .Select(element => (
                        PartName: NormalizeContentTypePartName(element.Attribute("PartName")?.Value),
                        ContentType: element.Attribute("ContentType")?.Value))
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.PartName) && !string.IsNullOrWhiteSpace(pair.ContentType))
                    .ToDictionary(pair => pair.PartName!, pair => pair.ContentType!, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return null;
            }
        }

        private static string? NormalizeContentTypePartName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return XlsxPackagePath.NormalizePackagePath(value.Trim());
        }

        private static bool DiagramPartHasExpectedContentType(
            IReadOnlyDictionary<string, string> contentTypes,
            string diagramPath,
            string relationshipType)
        {
            if (!contentTypes.TryGetValue(diagramPath, out var contentType))
                return false;

            var expectedContentType = relationshipType switch
            {
                DiagramDataRelationshipType => DiagramDataContentType,
                DiagramLayoutRelationshipType => DiagramLayoutContentType,
                DiagramQuickStyleRelationshipType => DiagramStyleContentType,
                DiagramColorsRelationshipType => DiagramColorsContentType,
                _ => null
            };

            return !string.IsNullOrWhiteSpace(expectedContentType) &&
                   string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryAddPatchSafeChartPackagePaths(
            ZipArchive archive,
            string chartPath,
            HashSet<string> allowedChartPaths,
            XNamespace packageRelNs)
        {
            var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
            var chartRelsEntry = archive.GetEntry(chartRelsPath);
            if (chartRelsEntry is null)
                return true;

            var contentTypes = TryReadPackageContentTypes(archive);
            if (contentTypes is null)
                return false;

            var chartRelsXml = XlsxPackageXmlEditor.LoadXml(chartRelsEntry);
            var relationships = chartRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .ToArray();
            if (relationships is null)
                return false;

            var referencedRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
            var themeOverrideRelationshipCount = 0;
            foreach (var relationship in relationships)
            {
                var id = relationship.Attribute("Id")?.Value;
                var type = relationship.Attribute("Type")?.Value;
                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(id) ||
                    !referencedRelationshipIds.Add(id) ||
                    !string.Equals(type, ChartThemeOverrideRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(target) ||
                    relationship.Attribute("TargetMode") is not null)
                {
                    return false;
                }

                themeOverrideRelationshipCount++;
                var sidecarPath = XlsxPackagePath.ResolveRelationshipTarget(chartPath, target);
                var sidecarFileName = sidecarPath[(sidecarPath.LastIndexOf('/') + 1)..];
                if (themeOverrideRelationshipCount > 1 ||
                    !sidecarPath.StartsWith("xl/theme/", StringComparison.OrdinalIgnoreCase) ||
                    sidecarPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !sidecarFileName.StartsWith("themeOverride", StringComparison.OrdinalIgnoreCase) ||
                    !sidecarPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sidecarPath)) is not null ||
                    archive.GetEntry(sidecarPath) is not { } sidecarEntry ||
                    !contentTypes.TryGetValue(sidecarPath, out var contentType) ||
                    !string.Equals(contentType, ChartThemeOverrideContentType, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
                var sidecarXml = XlsxPackageXmlEditor.LoadXml(sidecarEntry);
                if (sidecarXml.Root?.Name != drawingNs + "themeOverride")
                    return false;
            }

            allowedChartPaths.Add(chartRelsPath);
            return themeOverrideRelationshipCount == relationships.Length;
        }

        private static bool TryAddPatchSafeChartExPackagePaths(
            ZipArchive archive,
            string chartPath,
            HashSet<string> allowedChartPaths,
            XNamespace packageRelNs)
        {
            var chartRelsPath = XlsxPackagePath.GetRelationshipPartPath(chartPath);
            var chartRelsEntry = archive.GetEntry(chartRelsPath);
            if (chartRelsEntry is null)
                return false;

            var chartRelsXml = XlsxPackageXmlEditor.LoadXml(chartRelsEntry);
            var relationships = chartRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .ToArray();
            if (relationships is null)
                return false;

            var referencedRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
            var styleRelationshipCount = 0;
            var colorStyleRelationshipCount = 0;
            foreach (var relationship in relationships)
            {
                var id = relationship.Attribute("Id")?.Value;
                var type = relationship.Attribute("Type")?.Value;
                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(id) ||
                    !referencedRelationshipIds.Add(id) ||
                    string.IsNullOrWhiteSpace(type) ||
                    string.IsNullOrWhiteSpace(target) ||
                    relationship.Attribute("TargetMode") is not null)
                {
                    return false;
                }

                var expectedRootName = XName.Get("chartStyle", "http://schemas.microsoft.com/office/drawing/2012/chartStyle");
                if (string.Equals(type, ChartExStyleRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    styleRelationshipCount++;
                }
                else if (string.Equals(type, ChartExColorStyleRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    colorStyleRelationshipCount++;
                    expectedRootName = XName.Get("colorStyle", "http://schemas.microsoft.com/office/drawing/2012/chartStyle");
                }
                else
                {
                    return false;
                }

                var sidecarPath = XlsxPackagePath.ResolveRelationshipTarget(chartPath, target);
                if (!sidecarPath.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
                    sidecarPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !sidecarPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sidecarPath)) is not null ||
                    archive.GetEntry(sidecarPath) is not { } sidecarEntry)
                {
                    return false;
                }

                var sidecarXml = XlsxPackageXmlEditor.LoadXml(sidecarEntry);
                if (sidecarXml.Root?.Name != expectedRootName)
                    return false;

                allowedChartPaths.Add(sidecarPath);
            }

            if (styleRelationshipCount != 1 || colorStyleRelationshipCount != 1)
                return false;

            allowedChartPaths.Add(chartRelsPath);
            return true;
        }

        private static bool TryAddPatchSafePivotPackagePaths(
            ZipArchive archive,
            string worksheetPath,
            Sheet sheet,
            Workbook workbook,
            HashSet<string> allowedPivotPackagePaths,
            XNamespace packageRelNs)
        {
            var pivotTablePaths = ReadWorksheetPivotTableTargets(archive, worksheetPath, packageRelNs);
            if (pivotTablePaths.Count == 0)
                return sheet.PivotTables.Count == 0;

            if (pivotTablePaths.Count != sheet.PivotTables.Count)
                return false;

            var pivotModelsByPath = sheet.PivotTables
                .Where(pivot => !string.IsNullOrWhiteSpace(pivot.PackagePart))
                .ToDictionary(
                    pivot => NormalizePivotPackagePart(pivot.PackagePart),
                    pivot => pivot,
                    StringComparer.OrdinalIgnoreCase);
            if (pivotModelsByPath.Count != sheet.PivotTables.Count)
                return false;

            var cacheModelsByPath = workbook.PivotCaches
                .Where(cache => !string.IsNullOrWhiteSpace(cache.PackagePart))
                .ToDictionary(
                    cache => NormalizePivotPackagePart(cache.PackagePart),
                    cache => cache,
                    StringComparer.OrdinalIgnoreCase);
            if (cacheModelsByPath.Count != workbook.PivotCaches.Count)
                return false;

            foreach (var pivotTablePath in pivotTablePaths)
            {
                if (!pivotTablePath.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) ||
                    pivotTablePath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !pivotTablePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    !pivotModelsByPath.ContainsKey(pivotTablePath) ||
                    archive.GetEntry(pivotTablePath) is null)
                {
                    return false;
                }

                var pivotRelsPath = XlsxPackagePath.GetRelationshipPartPath(pivotTablePath);
                var pivotRelsEntry = archive.GetEntry(pivotRelsPath);
                if (pivotRelsEntry is null)
                    return false;

                var pivotRelsXml = XlsxPackageXmlEditor.LoadXml(pivotRelsEntry);
                var relationships = pivotRelsXml.Root?
                    .Elements(packageRelNs + "Relationship")
                    .ToArray() ?? [];
                var cacheRelationships = relationships
                    .Where(relationship =>
                        string.Equals(
                            relationship.Attribute("Type")?.Value,
                            PivotCacheDefinitionRelationshipType,
                            StringComparison.OrdinalIgnoreCase) &&
                        relationship.Attribute("TargetMode") is null)
                    .ToArray();
                if (cacheRelationships.Length != 1 || relationships.Length != 1)
                    return false;

                var cacheTarget = cacheRelationships[0].Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(cacheTarget))
                    return false;

                var cachePath = XlsxPackagePath.ResolveRelationshipTarget(pivotTablePath, cacheTarget);
                if (!cachePath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                    cachePath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                    !cachePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    !cacheModelsByPath.TryGetValue(cachePath, out var cacheModel) ||
                    !TryValidatePatchSafePivotCacheDefinition(
                        archive,
                        cachePath,
                        cacheModel,
                        allowedPivotPackagePaths,
                        packageRelNs))
                {
                    return false;
                }

                allowedPivotPackagePaths.Add(pivotTablePath);
                allowedPivotPackagePaths.Add(pivotRelsPath);
                allowedPivotPackagePaths.Add(cachePath);
                var cacheRelsPath = XlsxPackagePath.GetRelationshipPartPath(cachePath);
                if (archive.GetEntry(cacheRelsPath) is not null)
                    allowedPivotPackagePaths.Add(cacheRelsPath);
            }

            return true;
        }

        private static IReadOnlyList<string> ReadWorksheetPivotTableTargets(
            ZipArchive archive,
            string worksheetPath,
            XNamespace packageRelNs)
        {
            var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relsEntry is null)
                return [];

            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            var targets = new List<string>();
            foreach (var relationship in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
            {
                if (!string.Equals(
                        relationship.Attribute("Type")?.Value,
                        PivotTableRelationshipType,
                        StringComparison.OrdinalIgnoreCase) ||
                    relationship.Attribute("TargetMode") is not null)
                {
                    continue;
                }

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    return [];

                targets.Add(XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target));
            }

            return targets;
        }

        private static bool TryValidatePatchSafePivotCacheDefinition(
            ZipArchive archive,
            string cachePath,
            PivotCacheModel cacheModel,
            HashSet<string> allowedPivotPackagePaths,
            XNamespace packageRelNs)
        {
            if (cacheModel.SourceType != PivotCacheSourceType.WorksheetRange ||
                string.IsNullOrWhiteSpace(cacheModel.SourceSheetName) ||
                string.IsNullOrWhiteSpace(cacheModel.SourceReference) ||
                !string.IsNullOrWhiteSpace(cacheModel.SourceTableName) ||
                cacheModel.ConnectionId is not null ||
                cacheModel.IsOlap)
            {
                return false;
            }

            var cacheEntry = archive.GetEntry(cachePath);
            if (cacheEntry is null)
                return false;

            var cacheXml = XlsxPackageXmlEditor.LoadXml(cacheEntry);
            var root = cacheXml.Root;
            if (root is null)
                return false;

            var workbookNs = root.Name.Namespace;
            if (root.Name != workbookNs + "pivotCacheDefinition")
                return false;

            var cacheSource = root.Element(workbookNs + "cacheSource");
            var worksheetSource = cacheSource?.Element(workbookNs + "worksheetSource");
            if (cacheSource is null ||
                worksheetSource is null ||
                !string.Equals(cacheSource.Attribute("type")?.Value, "worksheet", StringComparison.OrdinalIgnoreCase) ||
                cacheSource.Attribute("connectionId") is not null ||
                worksheetSource.Attribute("name") is not null ||
                !string.Equals(worksheetSource.Attribute("sheet")?.Value, cacheModel.SourceSheetName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(worksheetSource.Attribute("ref")?.Value, cacheModel.SourceReference, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var cacheRelsPath = XlsxPackagePath.GetRelationshipPartPath(cachePath);
            var cacheRelsEntry = archive.GetEntry(cacheRelsPath);
            if (cacheRelsEntry is null)
                return true;

            var cacheRelsXml = XlsxPackageXmlEditor.LoadXml(cacheRelsEntry);
            foreach (var relationship in cacheRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
            {
                var relationshipType = relationship.Attribute("Type")?.Value;
                if (string.Equals(relationshipType, PivotCacheRecordsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    relationship.Attribute("TargetMode") is null)
                {
                    var target = relationship.Attribute("Target")?.Value;
                    if (string.IsNullOrWhiteSpace(target))
                        return false;

                    var recordsPath = XlsxPackagePath.ResolveRelationshipTarget(cachePath, target);
                    if (!recordsPath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                        recordsPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                        !recordsPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                        archive.GetEntry(recordsPath) is null)
                    {
                        return false;
                    }

                    allowedPivotPackagePaths.Add(recordsPath);
                    continue;
                }

                if (string.Equals(relationshipType, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static string NormalizePivotPackagePart(string packagePart) =>
            XlsxPackagePath.NormalizePackagePath(packagePart);

        private static bool IsPatchSafeSourcePicture(PictureModel picture) =>
            picture.IsSourceLoaded &&
            picture.Kind == PictureKind.Image &&
            !picture.IsLinkedToSourceRange &&
            picture.LinkedSourceRange is null;

        private static bool IsPatchSafeSourceTextBox(TextBoxModel textBox) =>
            textBox.IsSourceLoaded;

        private static bool IsPatchSafeSourceDrawingShape(DrawingShapeModel shape) =>
            shape.IsSourceLoaded;

        private static bool SourcePicturesMatchSheet(
            IReadOnlyList<XlsxPicturePackagePart> sourcePictures,
            Sheet sheet)
        {
            if (sourcePictures.Count != sheet.Pictures.Count)
                return false;

            for (var index = 0; index < sourcePictures.Count; index++)
            {
                var source = sourcePictures[index];
                var current = sheet.Pictures[index];
                if (!IsPatchSafeSourcePicture(current) ||
                    !StringEquals(source.Name, current.Name) ||
                    !StringEquals(source.Title, current.Title) ||
                    !StringEquals(source.AltText, current.AltText) ||
                    !ApproximatelyEquals(source.RotationDegrees, current.RotationDegrees) ||
                    !DrawingAnchorMatchesGeometry(
                        source.Anchor,
                        sheet,
                        current.Anchor,
                        current.Width,
                        current.Height,
                        current.AnchorOffsetX,
                        current.AnchorOffsetY))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SourceTextBoxesMatchSheet(
            IReadOnlyList<XlsxTextBoxPackagePart> sourceTextBoxes,
            Sheet sheet)
        {
            if (sourceTextBoxes.Count != sheet.TextBoxes.Count)
                return false;

            for (var index = 0; index < sourceTextBoxes.Count; index++)
            {
                var source = sourceTextBoxes[index];
                var current = sheet.TextBoxes[index];
                if (!IsPatchSafeSourceTextBox(current) ||
                    !StringEquals(source.Name, current.Name) ||
                    !StringEquals(source.Text, current.Text) ||
                    !StringEquals(source.Title, current.Title) ||
                    !StringEquals(source.AltText, current.AltText) ||
                    !DrawingAnchorMatchesGeometry(
                        source.Anchor,
                        sheet,
                        current.Anchor,
                        current.Width,
                        current.Height,
                        current.AnchorOffsetX,
                        current.AnchorOffsetY) ||
                    !ApproximatelyEquals(source.RotationDegrees, current.RotationDegrees) ||
                    source.HasFill != current.HasFill ||
                    source.FillColor != current.FillColor ||
                    source.OutlineColor != current.OutlineColor ||
                    source.FillThemeColor != current.FillThemeColor ||
                    source.OutlineThemeColor != current.OutlineThemeColor)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SourceDrawingShapesMatchSheet(
            IReadOnlyList<XlsxShapePackagePart> sourceShapes,
            Sheet sheet)
        {
            if (sourceShapes.Count != sheet.DrawingShapes.Count)
                return false;

            for (var index = 0; index < sourceShapes.Count; index++)
            {
                var source = sourceShapes[index];
                var current = sheet.DrawingShapes[index];
                if (!IsPatchSafeSourceDrawingShape(current) ||
                    source.Kind != current.Kind ||
                    !StringEquals(source.Name, current.Name) ||
                    !StringEquals(source.Title, current.Title) ||
                    !StringEquals(source.AltText, current.AltText) ||
                    !DrawingAnchorMatchesGeometry(
                        source.Anchor,
                        sheet,
                        current.Anchor,
                        current.Width,
                        current.Height,
                        current.AnchorOffsetX,
                        current.AnchorOffsetY,
                        source.XfrmWidthPixels,
                        source.XfrmHeightPixels,
                        current.Kind) ||
                    !ApproximatelyEquals(source.RotationDegrees, current.RotationDegrees) ||
                    source.HasFill != current.HasFill ||
                    source.FillColor != current.FillColor ||
                    source.OutlineColor != current.OutlineColor ||
                    source.GradientFillEndColor != current.GradientFillEndColor ||
                    source.GradientFillDirection != current.GetEffectiveGradientFillDirection() ||
                    source.FillThemeColor != current.FillThemeColor ||
                    source.OutlineThemeColor != current.OutlineThemeColor ||
                    source.EffectPreset != current.GetEffectiveEffectPreset() ||
                    !ApproximatelyEquals(source.OutlineWidthPoints, current.OutlineWidthPoints) ||
                    source.OutlineHasNoFill != current.OutlineHasNoFill ||
                    source.OutlineDash != current.OutlineDash)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Patch-safe equality for a drawing object's anchor: the source anchor's from-cell must match the
        /// in-memory <paramref name="currentAnchor"/> cell AND the geometry it carries (rendered width/height,
        /// plus the from-cell sub-cell offset) must match the current model's <see cref="PictureModel.Width"/>/
        /// <see cref="TextBoxModel.Width"/>/<see cref="DrawingShapeModel.Width"/> etc. A pure resize or
        /// reposition (no XML anchor rewrite) must never be treated as "no drawing change" — otherwise the
        /// cell-patch fast-save path keeps the stale source drawing XML and silently discards the resize/move.
        /// </summary>
        private static bool DrawingAnchorMatchesGeometry(
            XlsxDrawingAnchor? sourceAnchor,
            Sheet sheet,
            CellAddress currentAnchor,
            double currentWidth,
            double currentHeight,
            double currentAnchorOffsetX,
            double currentAnchorOffsetY,
            double? xfrmWidthPixels = null,
            double? xfrmHeightPixels = null,
            DrawingShapeKind? shapeKind = null)
        {
            if (sourceAnchor is null)
                return currentAnchor.Row == 1 && currentAnchor.Col == 1;

            if (sourceAnchor.FromRowZeroBased + 1 != currentAnchor.Row ||
                sourceAnchor.FromColumnZeroBased + 1 != currentAnchor.Col)
            {
                return false;
            }

            if (!ApproximatelyEquals(sourceAnchor.FromColumnOffset, currentAnchorOffsetX) ||
                !ApproximatelyEquals(sourceAnchor.FromRowOffset, currentAnchorOffsetY))
            {
                return false;
            }

            double sourceWidth, sourceHeight;
            var isLineLike = shapeKind is { } kind && DrawingShapeKindSupport.IsLineLike(kind);
            if (xfrmWidthPixels.HasValue && xfrmHeightPixels.HasValue &&
                (xfrmWidthPixels is > 0 || isLineLike) &&
                (xfrmHeightPixels is > 0 || isLineLike))
            {
                sourceWidth = xfrmWidthPixels.Value;
                sourceHeight = xfrmHeightPixels.Value;
            }
            else
            {
                (sourceWidth, sourceHeight) = XlsxDrawingAnchorApplier.GetAnchorSize(sourceAnchor, sheet);
            }

            // Mirror XlsxDrawingAnchorApplier's "only apply a positive measurement" rule: an anchor that
            // resolves to a non-positive width/height (e.g. a degenerate twoCellAnchor) leaves the current
            // model's dimension untouched on load, so it must not be compared here either.
            if (sourceWidth > 0 && !ApproximatelyEquals(sourceWidth, currentWidth))
                return false;

            if (isLineLike)
            {
                if (sourceHeight >= 0 && !ApproximatelyEquals(Math.Max(0, sourceHeight), currentHeight))
                    return false;
            }
            else if (sourceHeight > 0 && !ApproximatelyEquals(sourceHeight, currentHeight))
            {
                return false;
            }

            return true;
        }

        private static bool StringEquals(string? source, string? current) =>
            string.Equals(source, current, StringComparison.Ordinal);

        private static bool ApproximatelyEquals(double source, double current) =>
            Math.Abs(source - current) < 0.0001;

        private static bool TryGetRelationship(
            IReadOnlyList<XElement> relationships,
            string relationshipId,
            string relationshipType,
            out string target)
        {
            var relationship = FindInternalRelationshipByIdAndType(relationships, relationshipId, relationshipType);
            target = relationship?.Attribute("Target")?.Value ?? "";
            return !string.IsNullOrWhiteSpace(target);
        }

        private static XElement? FindInternalRelationshipByIdAndType(
            IEnumerable<XElement> relationships,
            string relationshipId,
            string relationshipType) =>
            relationships.SingleOrDefault(relationship =>
                RelationshipHasId(relationship, relationshipId) &&
                RelationshipHasType(relationship, relationshipType) &&
                RelationshipHasInternalTarget(relationship));

        private static XElement? FindRelationshipByIdAndType(
            IEnumerable<XElement> relationships,
            string relationshipId,
            string relationshipType) =>
            relationships.SingleOrDefault(relationship =>
                RelationshipHasId(relationship, relationshipId) &&
                RelationshipHasType(relationship, relationshipType));

        private static bool RelationshipHasId(XElement relationship, string relationshipId) =>
            string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal);

        private static bool RelationshipHasType(XElement relationship, string relationshipType) =>
            string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase);

        private static bool RelationshipHasInternalTarget(XElement relationship) =>
            relationship.Attribute("TargetMode") is null;

        private static Sheet? FindSheetByName(Workbook workbook, string sheetName)
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                    return sheet;
            }

            return null;
        }

        private static PivotTableModel? FindPivotTableByChartSource(
            Workbook workbook,
            Sheet chartSheet,
            ChartModel chart,
            string pivotTableName,
            int pivotCacheId)
        {
            var sourceSheetName = GetFirstNonBlankPivotSourceSheetName(chart, chartSheet);
            var sourceSheet = FindSheetByName(workbook, sourceSheetName);
            var pivotTable = sourceSheet is null ? null : FindPivotTableByName(sourceSheet, pivotTableName);
            return PivotTableCacheMatches(pivotTable, pivotCacheId) ? pivotTable : null;
        }

        private static string GetFirstNonBlankPivotSourceSheetName(ChartModel chart, Sheet chartSheet) =>
            string.IsNullOrWhiteSpace(chart.PivotSourceSheetName)
                ? chartSheet.Name
                : chart.PivotSourceSheetName!;

        private static PivotTableModel? FindPivotTableByName(Sheet sheet, string pivotTableName)
        {
            foreach (var candidate in sheet.PivotTables)
            {
                if (PivotTableNameMatches(candidate, pivotTableName))
                    return candidate;
            }

            return null;
        }

        private static bool PivotTableNameMatches(PivotTableModel pivotTable, string pivotTableName) =>
            string.Equals(pivotTable.Name, pivotTableName, StringComparison.OrdinalIgnoreCase);

        private static bool PivotTableCacheMatches(PivotTableModel? pivotTable, int pivotCacheId) =>
            pivotTable?.CacheId == pivotCacheId;

        private static bool WorkbookContainsPivotCache(Workbook workbook, int pivotCacheId) =>
            workbook.PivotCaches.Any(cache => cache.CacheId == pivotCacheId);

        private static string? ReadFirstEmbeddedImageRelationshipId(XElement pictureElement, XNamespace drawingNs, XNamespace relNs)
        {
            foreach (var blip in pictureElement.Descendants(drawingNs + "blip"))
            {
                var value = blip.Attribute(relNs + "embed")?.Value;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        private static XElement? FindRelationshipById(IEnumerable<XElement> relationships, string relationshipId)
        {
            foreach (var element in relationships)
            {
                if (RelationshipHasId(element, relationshipId))
                    return element;
            }

            return null;
        }

        private static bool TryAddPatchPreservedLegacyDrawingVmlPaths(
            ZipArchive archive,
            string worksheetPath,
            XElement legacyDrawing,
            HashSet<string> allowedVmlDrawingPaths)
        {
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var relationshipId = legacyDrawing.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            return TryAddPatchPreservedLegacyDrawingVmlPaths(
                archive,
                worksheetPath,
                relationshipId,
                allowedVmlDrawingPaths);
        }

        private static bool TryAddPatchPreservedLegacyDrawingVmlPaths(
            ZipArchive archive,
            string worksheetPath,
            string relationshipId,
            HashSet<string> allowedVmlDrawingPaths)
        {
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return false;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return false;

            var relationships = relationshipsRoot.Elements(packageRelNs + "Relationship").ToList();
            if (!TryGetRelationship(relationships, relationshipId, VmlDrawingRelationshipType, out var target))
                return false;

            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            var fileName = vmlPath[(vmlPath.LastIndexOf('/') + 1)..];
            if (!vmlPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                vmlPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                !fileName.StartsWith("vmlDrawing", StringComparison.OrdinalIgnoreCase) ||
                !vmlPath.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var vmlEntry = archive.GetEntry(vmlPath);
            var vmlRelsPath = XlsxPackagePath.GetRelationshipPartPath(vmlPath);
            var vmlRelsEntry = archive.GetEntry(vmlRelsPath);
            if (vmlEntry is null ||
                !TryReadWorksheetCommentReferences(archive, worksheetPath, relationshipsRoot, packageRelNs, out var commentReferences) ||
                !IsPatchSafePreservedLegacyDrawingVml(vmlEntry, commentReferences) ||
                (vmlRelsEntry is not null && !IsValidRelationshipPart(vmlRelsEntry)))
            {
                return false;
            }

            allowedVmlDrawingPaths.Add(vmlPath);
            if (vmlRelsEntry is not null)
                allowedVmlDrawingPaths.Add(vmlRelsPath);

            return true;
        }

        private static bool TryAddPatchSafeHeaderFooterVmlDrawingPaths(
            ZipArchive archive,
            string worksheetPath,
            XDocument worksheetXml,
            XElement legacyDrawing,
            Sheet sheet,
            HashSet<string> allowedVmlDrawingPaths)
        {
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var relationshipId = legacyDrawing.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
                return false;

            return TryAddPatchSafeHeaderFooterVmlDrawingPaths(
                archive,
                worksheetPath,
                relationshipId,
                sheet,
                allowedVmlDrawingPaths,
                worksheetXml);
        }

        private static bool TryAddPatchSafeHeaderFooterVmlDrawingPaths(
            ZipArchive archive,
            string worksheetPath,
            string relationshipId,
            Sheet sheet,
            HashSet<string> allowedVmlDrawingPaths,
            XDocument? worksheetXml = null)
        {
            if (!XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet) ||
                string.IsNullOrWhiteSpace(relationshipId))
            {
                return false;
            }

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return false;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return false;

            var relationships = relationshipsRoot.Elements(packageRelNs + "Relationship").ToList();
            if (!TryGetRelationship(relationships, relationshipId, VmlDrawingRelationshipType, out var target))
                return false;

            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            var fileName = vmlPath[(vmlPath.LastIndexOf('/') + 1)..];
            if (!vmlPath.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                vmlPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
                !fileName.StartsWith("vmlDrawing", StringComparison.OrdinalIgnoreCase) ||
                !vmlPath.EndsWith(".vml", StringComparison.OrdinalIgnoreCase) ||
                archive.GetEntry(vmlPath) is null)
            {
                return false;
            }

            var vmlRelsPath = XlsxPackagePath.GetRelationshipPartPath(vmlPath);
            if (archive.GetEntry(vmlRelsPath) is not { } vmlRelsEntry ||
                !IsValidRelationshipPart(vmlRelsEntry))
            {
                return false;
            }

            if (!XlsxHeaderFooterPicturePackageGraphNormalizer.IsPatchSafe(archive, vmlPath))
                return false;

            if (worksheetXml is null)
            {
                var worksheetEntry = archive.GetEntry(worksheetPath);
                if (worksheetEntry is null)
                    return false;

                worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            }

            var sourcePictures = XlsxHeaderFooterPictureReaderWriter.Read(archive, worksheetPath, worksheetXml);
            if (!XlsxHeaderFooterPicturePackagePlanner.PictureSetsEqual(sourcePictures, sheet))
                return false;

            allowedVmlDrawingPaths.Add(vmlPath);
            allowedVmlDrawingPaths.Add(vmlRelsPath);
            return true;
        }

        private static bool TryReadWorksheetCommentReferences(
            ZipArchive archive,
            string worksheetPath,
            XElement relationshipsRoot,
            XNamespace packageRelNs,
            out HashSet<(uint Row, uint Col)> commentReferences)
        {
            commentReferences = [];
            var commentPartPaths = relationshipsRoot
                .Elements(packageRelNs + "Relationship")
                .Where(relationship =>
                    string.Equals(relationship.Attribute("Type")?.Value, CommentsRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
                .Select(relationship => XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, relationship.Attribute("Target")!.Value))
                .Where(path => archive.GetEntry(path) is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (commentPartPaths.Count == 0)
                return true;

            if (commentPartPaths.Count > 1)
                return false;

            var commentsEntry = archive.GetEntry(commentPartPaths[0]);
            if (commentsEntry is null)
                return false;

            var commentsXml = XlsxPackageXmlEditor.LoadXml(commentsEntry);
            var root = commentsXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            foreach (var comment in root.Element(worksheetNs + "commentList")?.Elements(worksheetNs + "comment") ?? [])
            {
                if (!TryParsePackageCellReference(comment.Attribute("ref")?.Value, out var row, out var col) ||
                    !IsValidWorksheetRow(row) ||
                    !IsValidWorksheetColumn(col) ||
                    !commentReferences.Add((row, col)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParsePackageCellReference(string? reference, out uint row, out uint col)
        {
            row = 0;
            col = 0;
            if (string.IsNullOrWhiteSpace(reference) ||
                reference.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }

            if (!CellAddress.TryParse(reference, default, out var address))
                return false;

            row = address.Row;
            col = address.Col;
            return true;
        }

        private static bool IsPatchSafePreservedLegacyDrawingVml(
            ZipArchiveEntry vmlEntry,
            IReadOnlySet<(uint Row, uint Col)> commentReferences)
        {
            XNamespace vmlNs = "urn:schemas-microsoft-com:vml";
            XNamespace excelNs = "urn:schemas-microsoft-com:office:excel";
            XDocument vmlXml;
            try
            {
                vmlXml = XlsxPackageXmlEditor.LoadXml(vmlEntry);
            }
            catch
            {
                return false;
            }

            var shapeReferences = new HashSet<(uint Row, uint Col)>();
            foreach (var shape in vmlXml.Descendants(vmlNs + "shape"))
            {
                if (!TryReadLegacyNoteShapeReference(shape, excelNs, out var reference))
                    return false;

                if (reference is null)
                    continue;

                if (!shapeReferences.Add(reference.Value))
                {
                    return false;
                }
            }

            return shapeReferences.SetEquals(commentReferences);
        }

        private static bool TryReadLegacyNoteShapeReference(
            XElement shape,
            XNamespace excelNs,
            out (uint Row, uint Col)? reference)
        {
            reference = null;
            var noteClientData = shape
                .Elements(excelNs + "ClientData")
                .Where(element => string.Equals(element.Attribute("ObjectType")?.Value, "Note", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (noteClientData.Count == 0)
                return true;

            if (noteClientData.Count != 1)
                return false;

            var clientData = noteClientData[0];
            if (!TryReadZeroBasedClientDataIndex(clientData.Element(excelNs + "Row"), out var zeroBasedRow) ||
                !TryReadZeroBasedClientDataIndex(clientData.Element(excelNs + "Column"), out var zeroBasedColumn))
            {
                return false;
            }

            var row = zeroBasedRow + 1;
            var col = zeroBasedColumn + 1;
            if (!IsValidWorksheetRow(row) || !IsValidWorksheetColumn(col))
                return false;

            reference = (row, col);
            return true;
        }

        private static bool TryReadZeroBasedClientDataIndex(XElement? element, out uint oneBasedIndex)
        {
            oneBasedIndex = 0;
            return uint.TryParse(
                element?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out oneBasedIndex);
        }

        private static bool IsValidRelationshipPart(ZipArchiveEntry entry)
        {
            try
            {
                XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                var relationshipsXml = XlsxPackageXmlEditor.LoadXml(entry);
                if (relationshipsXml.Root?.Name != packageRelNs + "Relationships")
                    return false;

                foreach (var relationship in relationshipsXml.Root.Elements(packageRelNs + "Relationship"))
                {
                    if (string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value) ||
                        string.IsNullOrWhiteSpace(relationship.Attribute("Type")?.Value) ||
                        string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool RichDataPackageGraphAllowsCellPatchSave(
            ZipArchive archive,
            XNamespace packageRelNs,
            out string? blockReason)
        {
            blockReason = null;
            var hasRichDataParts = archive.Entries.Any(entry =>
                XlsxPackagePath.NormalizeEntryPath(entry)
                    .StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase));
            if (!hasRichDataParts)
                return true;

            var contentTypes = ReadContentTypeOverrides(archive);
            if (!RichDataContentTypesAreValid(archive, contentTypes))
            {
                blockReason = "package_guard_rich_data_content_types";
                return false;
            }

            foreach (var entry in archive.Entries.Where(entry =>
                         entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                if (!RichDataRelationshipsAreValid(archive, entry, packageRelNs))
                {
                    blockReason = "package_guard_rich_data_relationships";
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyDictionary<string, string> ReadContentTypeOverrides(ZipArchive archive)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var entry = archive.GetEntry("[Content_Types].xml");
            if (entry is null)
                return result;

            try
            {
                XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
                var xml = XlsxPackageXmlEditor.LoadXml(entry);
                foreach (var element in xml.Root?.Elements(contentTypeNs + "Override") ?? [])
                {
                    var partName = element.Attribute("PartName")?.Value;
                    var contentType = element.Attribute("ContentType")?.Value;
                    if (string.IsNullOrWhiteSpace(partName) || string.IsNullOrWhiteSpace(contentType))
                        continue;

                    var normalized = XlsxPackagePath.NormalizePackagePath(partName.Trim());
                    if (!string.IsNullOrWhiteSpace(normalized))
                        result[normalized] = contentType.Trim();
                }
            }
            catch
            {
                result.Clear();
            }

            return result;
        }

        private static bool RichDataContentTypesAreValid(
            ZipArchive archive,
            IReadOnlyDictionary<string, string> contentTypes)
        {
            foreach (var entry in archive.Entries)
            {
                var path = XlsxPackagePath.NormalizeEntryPath(entry);
                if (!TryGetKnownRichDataContentType(path, out var expectedContentType))
                    continue;

                if (!contentTypes.TryGetValue(path, out var contentType) ||
                    !string.Equals(contentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RichDataRelationshipsAreValid(
            ZipArchive archive,
            ZipArchiveEntry relationshipEntry,
            XNamespace packageRelNs)
        {
            var relationshipPartPath = XlsxPackagePath.NormalizeEntryPath(relationshipEntry);
            var sourcePartPath = RelationshipPartToSourcePart(relationshipPartPath);
            var sourceIsRichData = sourcePartPath.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase);

            XDocument relationshipsXml;
            try
            {
                relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
            }
            catch
            {
                return !sourceIsRichData;
            }

            if (relationshipsXml.Root?.Name != packageRelNs + "Relationships")
                return !sourceIsRichData;

            foreach (var relationship in relationshipsXml.Root.Elements(packageRelNs + "Relationship"))
            {
                if (!IsStructurallyValidPackageRelationship(relationship))
                    return !sourceIsRichData;

                var relationshipType = relationship.Attribute("Type")?.Value.Trim() ?? "";
                var isRichDataRelationship = IsRichDataRelationshipType(relationshipType);
                if (!sourceIsRichData && !isRichDataRelationship)
                    continue;

                if (relationship.Attribute("TargetMode") is { } targetMode &&
                    string.Equals(targetMode.Value.Trim(), "External", StringComparison.OrdinalIgnoreCase))
                {
                    if (sourceIsRichData && !string.Equals(relationshipType, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", StringComparison.OrdinalIgnoreCase))
                        return false;

                    continue;
                }

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    return false;

                var targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target.Trim().Replace('\\', '/'));
                if (archive.GetEntry(targetPath) is null)
                    return false;

                if (isRichDataRelationship &&
                    (!targetPath.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
                     !RichDataRelationshipTargetMatchesType(relationshipType, targetPath)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsStructurallyValidPackageRelationship(XElement relationship)
        {
            if (relationship.Attributes().Any(attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    attribute.Name.NamespaceName.Length != 0))
            {
                return false;
            }

            if (relationship.Attributes().Any(attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    attribute.Name.LocalName is not "Id" and not "Type" and not "Target" and not "TargetMode"))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value) ||
                string.IsNullOrWhiteSpace(relationship.Attribute("Type")?.Value) ||
                string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
            {
                return false;
            }

            var targetMode = relationship.Attribute("TargetMode")?.Value;
            return string.IsNullOrWhiteSpace(targetMode) ||
                   string.Equals(targetMode.Trim(), "External", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(targetMode.Trim(), "Internal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRichDataRelationshipType(string relationshipType) =>
            string.Equals(relationshipType, RdRichValueRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RdRichValueStructureRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RdArrayRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RdSupportingPropertyBagRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RdSupportingPropertyBagStructureRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RdRichValueTypesRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RichStylesRelationshipType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(relationshipType, RichValueRelRelationshipType, StringComparison.OrdinalIgnoreCase);

        private static bool RichDataRelationshipTargetMatchesType(string relationshipType, string targetPath) =>
            (string.Equals(relationshipType, RdRichValueRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdrichvalue.xml")) ||
            (string.Equals(relationshipType, RdRichValueStructureRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdrichvaluestructure.xml")) ||
            (string.Equals(relationshipType, RdArrayRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdarray.xml")) ||
            (string.Equals(relationshipType, RdSupportingPropertyBagRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdsupportingpropertybag.xml")) ||
            (string.Equals(relationshipType, RdSupportingPropertyBagStructureRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdsupportingpropertybagstructure.xml")) ||
            (string.Equals(relationshipType, RdRichValueTypesRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "rdRichValueTypes.xml")) ||
            (string.Equals(relationshipType, RichStylesRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "richStyles.xml")) ||
            (string.Equals(relationshipType, RichValueRelRelationshipType, StringComparison.OrdinalIgnoreCase) &&
             PathMatchesKnownRichDataPart(targetPath, "richValueRel.xml"));

        private static bool TryGetKnownRichDataContentType(string path, out string contentType)
        {
            contentType = "";
            var fileName = Path.GetFileName(path);
            if (!path.StartsWith("xl/richData/", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            contentType = fileName switch
            {
                _ when string.Equals(fileName, "rdrichvalue.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdrichvalue+xml",
                _ when string.Equals(fileName, "rdrichvaluestructure.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdrichvaluestructure+xml",
                _ when string.Equals(fileName, "rdarray.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdarray+xml",
                _ when string.Equals(fileName, "rdsupportingpropertybag.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdsupportingpropertybag+xml",
                _ when string.Equals(fileName, "rdsupportingpropertybagstructure.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdsupportingpropertybagstructure+xml",
                _ when string.Equals(fileName, "rdRichValueTypes.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.rdrichvaluetypes+xml",
                _ when string.Equals(fileName, "richStyles.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.richstyles+xml",
                _ when string.Equals(fileName, "richValueRel.xml", StringComparison.OrdinalIgnoreCase) =>
                    "application/vnd.ms-excel.richvaluerel+xml",
                _ => ""
            };

            return contentType.Length != 0;
        }

        private static bool PathMatchesKnownRichDataPart(string path, string fileName) =>
            string.Equals(
                XlsxPackagePath.NormalizePackagePath(path),
                $"xl/richData/{fileName}",
                StringComparison.OrdinalIgnoreCase);

        private static string RelationshipPartToSourcePart(string relationshipPartPath)
        {
            var normalized = XlsxPackagePath.NormalizePackagePath(relationshipPartPath);
            if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
                return "";

            const string relsSegment = "/_rels/";
            var relsIndex = normalized.IndexOf(relsSegment, StringComparison.OrdinalIgnoreCase);
            if (relsIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                return normalized;

            var directory = normalized[..relsIndex];
            var fileName = normalized[(relsIndex + relsSegment.Length)..^".rels".Length];
            return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
        }

        private sealed record XlsxWorksheetPackageGuardInfo(
            bool HasCustomProperties,
            bool HasQueryTableParts,
            bool HasOfficeRevisionAttributes,
            IReadOnlyList<string> DrawingRelationshipIds,
            IReadOnlyList<string> HeaderFooterVmlRelationshipIds,
            IReadOnlyList<string> LegacyDrawingRelationshipIds,
            bool HasTableParts,
            int? TablePartDeclaredCount,
            IReadOnlyList<string> TablePartRelationshipIds,
            bool HasInvalidTablePartRelationship);

        private static bool TryReadWorksheetPackageGuardInfo(
            ZipArchiveEntry worksheetEntry,
            XNamespace worksheetNs,
            XNamespace relNs,
            out XlsxWorksheetPackageGuardInfo info)
        {
            info = new XlsxWorksheetPackageGuardInfo(
                false,
                false,
                false,
                [],
                [],
                [],
                false,
                null,
                [],
                false);

            try
            {
                using var stream = worksheetEntry.Open();
                using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
                if (reader.NodeType == XmlNodeType.None)
                    reader.Read();
                if (reader.NodeType != XmlNodeType.Element)
                    reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element)
                    return false;

                var rootDepth = reader.Depth;
                var hasCustomProperties = false;
                var hasQueryTableParts = false;
                var hasOfficeRevisionAttributes = HasOfficeRevisionAttribute(reader);
                var drawingRelationshipIds = new List<string>();
                var headerFooterVmlRelationshipIds = new List<string>();
                var legacyDrawingRelationshipIds = new List<string>();
                var hasTableParts = false;
                int? tablePartDeclaredCount = null;
                var tablePartRelationshipIds = new List<string>();
                var hasInvalidTablePartRelationship = false;
                var tablePartsDepth = -1;

                if (!reader.IsEmptyElement)
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            if (reader.Depth == tablePartsDepth)
                                tablePartsDepth = -1;

                            if (reader.Depth == rootDepth)
                                break;

                            continue;
                        }

                        if (reader.NodeType != XmlNodeType.Element)
                            continue;

                        hasOfficeRevisionAttributes |= HasOfficeRevisionAttribute(reader);
                        if (reader.Depth == rootDepth + 1 &&
                            string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
                        {
                            switch (reader.LocalName)
                            {
                                case "customProperties":
                                    hasCustomProperties = true;
                                    break;
                                case "queryTableParts":
                                    hasQueryTableParts = true;
                                    break;
                                case "drawing":
                                    AddRelationshipId(reader, relNs, drawingRelationshipIds);
                                    break;
                                case "legacyDrawingHF":
                                    AddRelationshipId(reader, relNs, headerFooterVmlRelationshipIds);
                                    break;
                                case "legacyDrawing":
                                    AddRelationshipId(reader, relNs, legacyDrawingRelationshipIds);
                                    break;
                                case "tableParts":
                                    hasTableParts = true;
                                    if (int.TryParse(
                                            reader.GetAttribute("count"),
                                            NumberStyles.Integer,
                                            CultureInfo.InvariantCulture,
                                            out var declaredCount))
                                    {
                                        tablePartDeclaredCount = declaredCount;
                                    }

                                    tablePartsDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                                    break;
                            }

                            continue;
                        }

                        if (tablePartsDepth >= 0 &&
                            reader.Depth == tablePartsDepth + 1 &&
                            string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal) &&
                            string.Equals(reader.LocalName, "tablePart", StringComparison.Ordinal))
                        {
                            if (!AddRelationshipId(reader, relNs, tablePartRelationshipIds))
                                hasInvalidTablePartRelationship = true;
                        }
                    }
                }

                info = new XlsxWorksheetPackageGuardInfo(
                    hasCustomProperties,
                    hasQueryTableParts,
                    hasOfficeRevisionAttributes,
                    drawingRelationshipIds,
                    headerFooterVmlRelationshipIds,
                    legacyDrawingRelationshipIds,
                    hasTableParts,
                    tablePartDeclaredCount,
                    tablePartRelationshipIds,
                    hasInvalidTablePartRelationship);
                return true;
            }
            catch
            {
                return false;
            }

            static bool AddRelationshipId(XmlReader reader, XNamespace relNs, List<string> relationshipIds)
            {
                var relationshipId = reader.GetAttribute("id", relNs.NamespaceName);
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    relationshipIds.Add(string.Empty);
                    return false;
                }

                relationshipIds.Add(relationshipId);
                return true;
            }

            static bool HasOfficeRevisionAttribute(XmlReader reader)
            {
                if (!reader.HasAttributes)
                    return false;

                for (var index = 0; index < reader.AttributeCount; index++)
                {
                    reader.MoveToAttribute(index);
                    if (reader.Prefix != "xmlns" &&
                        !(reader.Prefix.Length == 0 &&
                          string.Equals(reader.LocalName, "xmlns", StringComparison.Ordinal)) &&
                        IsOfficeRevisionNamespace(reader.NamespaceURI))
                    {
                        reader.MoveToElement();
                        return true;
                    }
                }

                reader.MoveToElement();
                return false;
            }
        }

        private readonly record struct XlsxWorksheetSourceProtectionInfo(
            bool IsProtected,
            string? PasswordHash,
            IReadOnlyCollection<SheetProtectionPermission> Permissions,
            IReadOnlyDictionary<string, string?> AllowEditRanges);

        /// <summary>
        /// Streaming read of just the root-level <c>sheetProtection</c>/<c>protectedRanges</c>
        /// elements' protection-relevant attributes, without loading the full worksheet XDocument
        /// (mirrors <see cref="XlsxWorksheetGridXmlNormalizer.AnyRowMissingRowIndex"/>'s style).
        /// Encodes the password the same way <c>XlsxFileAdapter.SheetXmlLayout</c>'s
        /// <c>ReadSheetProtectionPasswordHash</c> does at full-load time, so the result is directly
        /// comparable to <see cref="Sheet.ProtectionPassword"/>. Also captures the permission
        /// booleans (via <see cref="XlsxSheetProtectionPermissionMapper.Read"/>) and the Allow-Edit
        /// ranges/range-passwords (via <see cref="XlsxAllowEditRangeMapper.Read(XDocument, XNamespace, out Dictionary{GridRange, string})"/>)
        /// so patch-save eligibility can detect a genuine permission or allow-edit-range delta
        /// (see <see cref="WorksheetProtectionPermissionsOrAllowEditRangesChanged"/>) instead of
        /// forcing a full save on every protected/protected-ranges worksheet unconditionally.
        /// </summary>
        private static bool TryReadSheetProtectionPackageGuardInfo(
            ZipArchiveEntry worksheetEntry,
            XNamespace worksheetNs,
            out XlsxWorksheetSourceProtectionInfo info)
        {
            info = new XlsxWorksheetSourceProtectionInfo(false, null, DefaultSourceProtectionPermissions, EmptyAllowEditRanges);

            try
            {
                using var stream = worksheetEntry.Open();
                using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
                reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "worksheet" ||
                    !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
                {
                    return true;
                }

                if (reader.IsEmptyElement)
                    return true;

                var isProtected = false;
                string? passwordHash = null;
                var permissions = DefaultSourceProtectionPermissions;
                var allowEditRanges = EmptyAllowEditRanges;

                var rootDepth = reader.Depth;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.Depth == rootDepth)
                            break;
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element)
                        continue;

                    if (reader.Depth != rootDepth + 1 ||
                        !string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (reader.LocalName == "sheetProtection")
                    {
                        isProtected = XlsxWorksheetXmlValueParser.IsTruthy(reader.GetAttribute("sheet"));
                        var legacyPassword = reader.GetAttribute("password");
                        if (!string.IsNullOrEmpty(legacyPassword))
                        {
                            passwordHash = legacyPassword;
                        }
                        else
                        {
                            var hashValue = reader.GetAttribute("hashValue");
                            passwordHash = string.IsNullOrEmpty(hashValue)
                                ? null
                                : ProtectionPasswordHelper.EncodeIso29500Hash(
                                    reader.GetAttribute("algorithmName"),
                                    reader.GetAttribute("spinCount"),
                                    reader.GetAttribute("saltValue"),
                                    hashValue);
                        }

                        var protectionElement = new XElement(worksheetNs + "sheetProtection");
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns")
                                    continue;

                                protectionElement.SetAttributeValue(reader.LocalName, reader.Value);
                            } while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }

                        permissions = XlsxSheetProtectionPermissionMapper.Read(protectionElement);
                        continue;
                    }

                    if (reader.LocalName == "protectedRanges")
                    {
                        var protectedRangesElement = XElement.Load(reader.ReadSubtree());
                        var syntheticDocument = new XDocument(
                            new XElement(worksheetNs + "worksheet", protectedRangesElement));
                        var ranges = XlsxAllowEditRangeMapper.Read(
                            syntheticDocument,
                            worksheetNs,
                            out var passwordsByRange);
                        var byReference = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var range in ranges)
                        {
                            byReference[range.ToString()] = passwordsByRange.TryGetValue(range, out var password)
                                ? password
                                : null;
                        }

                        allowEditRanges = byReference;
                        continue;
                    }
                }

                info = new XlsxWorksheetSourceProtectionInfo(isProtected, passwordHash, permissions, allowEditRanges);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly IReadOnlyCollection<SheetProtectionPermission> DefaultSourceProtectionPermissions =
            XlsxSheetProtectionPermissionMapper.Read(null);

        private static readonly IReadOnlyDictionary<string, string?> EmptyAllowEditRanges =
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True when the live model's sheet-protection state has diverged from what the source
        /// bytes still hold (i.e. a Protect/Unprotect Sheet command ran since load). Compares only
        /// what patch-save can never re-derive from the model (IsProtected + the password/hash
        /// verifier) -- permission-boolean and Allow-Edit-Range changes are handled separately by
        /// <see cref="WorksheetProtectionPermissionsOrAllowEditRangesChanged"/>.
        /// </summary>
        private static bool WorksheetProtectionStateChanged(XlsxWorksheetSourceProtectionInfo source, Sheet sheet) =>
            source.IsProtected != sheet.IsProtected ||
            !string.Equals(source.PasswordHash, sheet.ProtectionPassword, StringComparison.Ordinal);

        /// <summary>
        /// True when the live model's Protect-Sheet permission flags (<see cref="Sheet.ProtectionPermissions"/>)
        /// or its Allow-Edit-Ranges (<see cref="Sheet.AllowEditRanges"/>/<see cref="Sheet.AllowEditRangePasswords"/>)
        /// genuinely differ from what the source bytes parse to. Patch-save
        /// (<c>NormalizePatchWorksheetProtection</c>/<c>NormalizePatchWorksheetProtectedRanges</c>) only
        /// cosmetically normalizes the *original* elements -- it never re-derives either from the
        /// model (that only happens on the full/source-independent save path via
        /// <see cref="XlsxWorksheetProtectionMetadataWriter"/>/<see cref="XlsxAllowEditRangeMapper"/>).
        /// A real (not always-true) comparison here matters: forcing a full save whenever a
        /// protected sheet merely carries an *unchanged* set of permissions/ranges would regress a
        /// plain cell edit on that sheet onto the slower, source-independent path for no reason (and
        /// previously produced schema-invalid output on some full-save protectedRanges shapes -- see
        /// R57/R59 io-protection-5-1/5-2 history). Only force full save when something patch-save
        /// cannot express actually changed.
        /// </summary>
        private static bool WorksheetProtectionPermissionsOrAllowEditRangesChanged(
            XlsxWorksheetSourceProtectionInfo source,
            Sheet sheet)
        {
            if (source.Permissions.Count != sheet.ProtectionPermissions.Count ||
                !source.Permissions.All(sheet.ProtectionPermissions.Contains))
            {
                return true;
            }

            if (source.AllowEditRanges.Count != sheet.AllowEditRanges.Count)
                return true;

            foreach (var range in sheet.AllowEditRanges)
            {
                if (!source.AllowEditRanges.TryGetValue(range.ToString(), out var sourcePassword))
                    return true;

                var modelPassword = sheet.AllowEditRangePasswords.TryGetValue(range, out var storedPassword)
                    ? storedPassword
                    : null;

                if (!string.Equals(
                        string.IsNullOrEmpty(sourcePassword) ? null : sourcePassword,
                        string.IsNullOrEmpty(modelPassword) ? null : modelPassword,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
        {
            var path = XlsxPackagePath.NormalizeEntryPath(entry);
            return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStructuredTableXmlEntry(ZipArchiveEntry entry)
        {
            var path = XlsxPackagePath.NormalizeEntryPath(entry);
            return path.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) &&
                   !path.StartsWith("xl/tables/tableSingleCells", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSingleCellTableXmlEntry(ZipArchiveEntry entry)
        {
            var path = XlsxPackagePath.NormalizeEntryPath(entry);
            return path.StartsWith("xl/tables/tableSingleCells", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasOfficeRevisionAttributes(XElement root) =>
            root.DescendantsAndSelf()
                .SelectMany(element => element.Attributes())
                .Any(attribute =>
                    !attribute.IsNamespaceDeclaration &&
                    IsOfficeRevisionNamespace(attribute.Name.NamespaceName));

        private static bool HasUnsupportedWorksheetTableParts(
            ZipArchive archive,
            string worksheetPath,
            XlsxWorksheetPackageGuardInfo worksheetGuardInfo,
            XNamespace workbookNs,
            Sheet sheet)
        {
            if (!worksheetGuardInfo.HasTableParts)
                return false;

            if (worksheetGuardInfo.TablePartRelationshipIds.Count == 0)
                return worksheetGuardInfo.TablePartDeclaredCount != 0;

            if (worksheetGuardInfo.HasInvalidTablePartRelationship ||
                worksheetGuardInfo.TablePartDeclaredCount != worksheetGuardInfo.TablePartRelationshipIds.Count ||
                sheet.StructuredTables.Count != worksheetGuardInfo.TablePartRelationshipIds.Count)
            {
                return true;
            }

            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return true;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return true;

            var tableModelsByPath = sheet.StructuredTables
                .Where(table => !string.IsNullOrWhiteSpace(table.PackagePart))
                .ToDictionary(
                    table => XlsxPackagePath.NormalizePackagePath(table.PackagePart),
                    table => table,
                    StringComparer.OrdinalIgnoreCase);
            if (tableModelsByPath.Count != sheet.StructuredTables.Count)
                return true;

            var seenTablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var relationshipId in worksheetGuardInfo.TablePartRelationshipIds)
            {
                var relationship = FindRelationshipByIdAndType(
                    relationshipsRoot.Elements(packageRelNs + "Relationship"),
                    relationshipId,
                    TableRelationshipType);
                var target = relationship?.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    return true;

                var tablePath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
                if (!tablePath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
                    !tablePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    !seenTablePaths.Add(tablePath) ||
                    !tableModelsByPath.TryGetValue(tablePath, out var tableModel) ||
                    archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(tablePath)) is not null)
                {
                    return true;
                }

                var tableEntry = archive.GetEntry(tablePath);
                if (tableEntry is null || HasUnsupportedTablePart(tableEntry, workbookNs, tableModel))
                    return true;
            }

            return false;
        }

        private static bool HasUnsupportedWorksheetTableParts(
            ZipArchive archive,
            string worksheetPath,
            XElement worksheetRoot,
            XNamespace workbookNs,
            Sheet sheet)
        {
            var tableParts = worksheetRoot.Element(workbookNs + "tableParts");
            if (tableParts is null)
                return false;

            var tablePartElements = tableParts.Elements(workbookNs + "tablePart").ToList();
            if (tablePartElements.Count == 0)
            {
                return !string.Equals(tableParts.Attribute("count")?.Value, "0", StringComparison.Ordinal);
            }

            if (!int.TryParse(
                    tableParts.Attribute("count")?.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var declaredCount) ||
                declaredCount != tablePartElements.Count ||
                sheet.StructuredTables.Count != tablePartElements.Count)
            {
                return true;
            }

            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (relationshipsEntry is null)
                return true;

            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var relationshipsRoot = relationshipsXml.Root;
            if (relationshipsRoot is null)
                return true;

            var tableModelsByPath = sheet.StructuredTables
                .Where(table => !string.IsNullOrWhiteSpace(table.PackagePart))
                .ToDictionary(
                    table => XlsxPackagePath.NormalizePackagePath(table.PackagePart),
                    table => table,
                    StringComparer.OrdinalIgnoreCase);
            if (tableModelsByPath.Count != sheet.StructuredTables.Count)
                return true;

            var seenTablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tablePart in tablePartElements)
            {
                var relationshipId = tablePart.Attribute(relNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                    return true;

                var relationship = FindRelationshipByIdAndType(
                    relationshipsRoot.Elements(packageRelNs + "Relationship"),
                    relationshipId,
                    TableRelationshipType);
                var target = relationship?.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    return true;

                var tablePath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
                if (!tablePath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) ||
                    !tablePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    !seenTablePaths.Add(tablePath) ||
                    !tableModelsByPath.TryGetValue(tablePath, out var tableModel) ||
                    archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(tablePath)) is not null)
                {
                    return true;
                }

                var tableEntry = archive.GetEntry(tablePath);
                if (tableEntry is null || HasUnsupportedTablePart(tableEntry, workbookNs, tableModel))
                    return true;
            }

            return false;
        }

        private static bool HasUnsupportedTablePart(
            ZipArchiveEntry tableEntry,
            XNamespace workbookNs,
            StructuredTableModel tableModel)
        {
            var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
            var root = tableXml.Root;
            return root is null ||
                   root.Name != workbookNs + "table" ||
                   root.Attribute("connectionId") is not null ||
                   !string.Equals(root.Attribute("ref")?.Value, tableModel.Range.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUnsupportedRichSharedStringFonts(ZipArchive archive, XNamespace workbookNs)
        {
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry is null)
                return false;

            try
            {
                var sharedStringsXml = XlsxPackageXmlEditor.LoadXml(sharedStringsEntry);
                return sharedStringsXml.Root?
                    .Descendants(workbookNs + "rFont")
                    .Select(font => font.Attribute("val")?.Value)
                    .Any(value => value is not null &&
                                  (value.Contains(',', StringComparison.Ordinal) ||
                                   value.Contains('"', StringComparison.Ordinal))) == true;
            }
            catch
            {
                return true;
            }
        }

        private static bool ShouldCaptureModelFingerprint(Workbook workbook)
        {
            var cellCount = 0;
            var styleOnlyCellCount = 0;
            foreach (var sheet in workbook.Sheets)
            {
                cellCount += sheet.CellCount;
                if (cellCount > FingerprintCellLimit)
                    return false;

                if (!sheet.HasStyleOnlyCells)
                    continue;

                styleOnlyCellCount += sheet.StyleOnlyCellCount;
                if (styleOnlyCellCount > FingerprintCompressedStyleOnlyCellLimit)
                    return false;

                if (sheet.StyleOnlyCellCount > FingerprintCellLimit &&
                    !sheet.TryGetCompressedStyleOnlyRuns(out _))
                    return false;
            }

            return true;
        }

        private static string? GetModelFingerprint(Workbook workbook, string? currentModelFingerprint) =>
            currentModelFingerprint ?? (ShouldCaptureModelFingerprint(workbook)
                ? CreateModelFingerprint(workbook)
                : null);

        private static string CreateModelFingerprint(Workbook workbook) =>
            CreateSourceModelFingerprint(workbook);
    }

    private sealed record XlsxCellPatchBaselineFacts(
        IReadOnlyDictionary<string, string> SheetPathsByName,
        IReadOnlyDictionary<string, XlsxCellPatchBaselineSheetFacts> SheetsByName,
        XlsxChartSourceRangeIndex? ChartSourceRanges)
    {
        public static XlsxCellPatchBaselineFacts? Capture(
            Workbook workbook,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout)
        {
            if (sheetXmlLayout is null || sheetXmlLayout.Count != workbook.SheetCount)
                return null;

            var sheetPathsByName = new Dictionary<string, string>(workbook.SheetCount, StringComparer.OrdinalIgnoreCase);
            var sheetsByName = new Dictionary<string, XlsxCellPatchBaselineSheetFacts>(
                workbook.SheetCount,
                StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in workbook.Sheets)
            {
                if (!sheetXmlLayout.TryGetValue(sheet.Name, out var layout) ||
                    string.IsNullOrWhiteSpace(layout.WorksheetPath))
                {
                    return null;
                }

                sheetPathsByName[sheet.Name] = layout.WorksheetPath;
                sheetsByName[sheet.Name] = new XlsxCellPatchBaselineSheetFacts(
                    sheet.Name,
                    layout.WorksheetPath,
                    layout.ExplicitPopulatedCellStyles,
                    layout.ExplicitStyleOnlyCells);
            }

            return new XlsxCellPatchBaselineFacts(
                sheetPathsByName,
                sheetsByName,
                XlsxChartSourceRangeIndex.TryCreate(workbook, sheetXmlLayout, out _));
        }

        public bool MatchesWorkbookSheets(Workbook workbook)
        {
            if (SheetPathsByName.Count != workbook.SheetCount ||
                SheetsByName.Count != workbook.SheetCount)
            {
                return false;
            }

            foreach (var sheet in workbook.Sheets)
            {
                if (!SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath) ||
                    !TryGetSheetFacts(sheet, worksheetPath, out _))
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryGetSheetFacts(
            Sheet sheet,
            string worksheetPath,
            out XlsxCellPatchBaselineSheetFacts sheetFacts)
        {
            if (SheetsByName.TryGetValue(sheet.Name, out sheetFacts!) &&
                string.Equals(sheetFacts.WorksheetPath, worksheetPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            sheetFacts = null!;
            return false;
        }
    }

    private sealed record XlsxCellPatchBaselineSheetFacts(
        string SheetName,
        string WorksheetPath,
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitPopulatedCellStyles,
        IReadOnlyList<(uint Row, uint Col, int StyleIndex)> ExplicitStyleOnlyCells);

    private sealed class XlsxCellPatchBaseline
    {
        private readonly IReadOnlyList<XlsxWorksheetCellPatchBaseline> _worksheets;
        private readonly IReadOnlyDictionary<StyleId, string?> _sourceStyleIndexesByStyleId;
        private readonly XlsxChartSourceRangeIndex _chartSourceRanges;
        private readonly XlsxPivotSourceRangeIndex _pivotSourceRanges;
        private readonly string _modelFingerprint;

        private XlsxCellPatchBaseline(
            IReadOnlyList<XlsxWorksheetCellPatchBaseline> worksheets,
            IReadOnlyDictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            XlsxChartSourceRangeIndex chartSourceRanges,
            XlsxPivotSourceRangeIndex pivotSourceRanges,
            string modelFingerprint)
        {
            _worksheets = worksheets;
            _sourceStyleIndexesByStyleId = sourceStyleIndexesByStyleId;
            _chartSourceRanges = chartSourceRanges;
            _pivotSourceRanges = pivotSourceRanges;
            _modelFingerprint = modelFingerprint;
        }

        public static XlsxCellPatchBaseline? TryCreate(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            int cellLimit,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout = null,
            XlsxCellPatchBaselineFacts? baselineFacts = null)
            => TryCreate(package, offset, count, workbook, cellLimit, out _, sheetXmlLayout, baselineFacts);

        public static XlsxCellPatchBaseline? TryCreate(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            int cellLimit,
            out string? blockReason,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout = null,
            XlsxCellPatchBaselineFacts? baselineFacts = null)
        {
            blockReason = null;
            try
            {
                var totalCells = 0;
                foreach (var sheet in workbook.Sheets)
                {
                    totalCells += sheet.CellCount;
                    if (totalCells > cellLimit)
                    {
                        blockReason = "baseline_cell_limit";
                        return null;
                    }
                }

                var retainedBaselineFacts = baselineFacts is not null && baselineFacts.MatchesWorkbookSheets(workbook)
                    ? baselineFacts
                    : null;
                using var packageStream = new MemoryStream(package, offset, count, writable: false);
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
                IReadOnlyDictionary<string, string> sheetPathsByName;
                if (retainedBaselineFacts is not null)
                {
                    sheetPathsByName = retainedBaselineFacts.SheetPathsByName;
                }
                else
                {
                    var worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);
                    if (worksheetPathMap is null)
                    {
                        blockReason = "baseline_worksheet_path_map";
                        return null;
                    }

                    sheetPathsByName = worksheetPathMap.SheetPathsByName;
                }

                XlsxChartSourceRangeIndex? chartSourceRanges;
                string? chartSourceRangeBlockReason = null;
                if (retainedBaselineFacts?.ChartSourceRanges is { } retainedChartSourceRanges &&
                    retainedChartSourceRanges.Matches(workbook))
                {
                    chartSourceRanges = retainedChartSourceRanges;
                }
                else
                {
                    chartSourceRanges = XlsxChartSourceRangeIndex.TryCreate(
                        archive,
                        workbook,
                        sheetPathsByName,
                        sheetXmlLayout,
                        out chartSourceRangeBlockReason);
                }

                if (chartSourceRanges is null)
                {
                    blockReason = chartSourceRangeBlockReason ?? "baseline_chart_source_ranges";
                    return null;
                }

                var pivotSourceRanges = XlsxPivotSourceRangeIndex.TryCreate(
                    workbook,
                    out var pivotSourceRangeBlockReason);
                if (pivotSourceRanges is null)
                {
                    blockReason = pivotSourceRangeBlockReason ?? "baseline_pivot_source_ranges";
                    return null;
                }

                var worksheets = new List<XlsxWorksheetCellPatchBaseline>(workbook.SheetCount);
                var sourceStyleIndexesByStyleId = new Dictionary<StyleId, string?>();
                var ambiguousSourceStyleIds = new HashSet<StyleId>();
                sourceStyleIndexesByStyleId[StyleId.Default] = null;
                foreach (var sheet in workbook.Sheets)
                {
                    if (!sheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                    {
                        blockReason = "baseline_sheet_path_missing";
                        return null;
                    }

                    var sourceCellStyles =
                        retainedBaselineFacts is not null &&
                        retainedBaselineFacts.TryGetSheetFacts(sheet, worksheetPath, out var sheetFacts)
                            ? ReadSourceCellStyleIndexes(
                                sheetFacts,
                                sheet,
                                sourceStyleIndexesByStyleId,
                                ambiguousSourceStyleIds)
                            : sheetXmlLayout is not null &&
                        sheetXmlLayout.TryGetValue(sheet.Name, out var layout) &&
                        string.Equals(layout.WorksheetPath, worksheetPath, StringComparison.OrdinalIgnoreCase)
                            ? ReadSourceCellStyleIndexes(
                                layout,
                                sheet,
                                sourceStyleIndexesByStyleId,
                                ambiguousSourceStyleIds)
                            : ReadSourceCellStyleIndexes(
                                archive,
                                worksheetPath,
                                sheet,
                                sourceStyleIndexesByStyleId,
                                ambiguousSourceStyleIds);
                    if (sourceCellStyles is null)
                    {
                        blockReason = "baseline_source_cell_styles";
                        return null;
                    }

                    var sourceHyperlinks = ReadSourceHyperlinks(archive, worksheetPath, sheet);
                    var sourceComments = ReadSourceComments(archive, worksheetPath, sheet);
                    var occupiedCells = sheet.GetOccupiedCellMap();
                    var cells = new XlsxPatchCellEntry[occupiedCells.Count];
                    var cellIndex = 0;
                    foreach (var ((row, col), cell) in occupiedCells)
                    {
                        var hasExplicitSourceStyleIndex = sourceCellStyles.PopulatedCells.TryGetValue(row, col, out var sourceStyleIndex);
                        if (cell.StyleId == StyleId.Default || hasExplicitSourceStyleIndex)
                        {
                            AddSourceStyleIndex(
                                sourceStyleIndexesByStyleId,
                                ambiguousSourceStyleIds,
                                cell.StyleId,
                                sourceStyleIndex);
                        }

                        cells[cellIndex++] = new XlsxPatchCellEntry(
                            row,
                            col,
                            new XlsxPatchCell(
                                cell.Value,
                                cell.FormulaText,
                                cell.ArrayMode,
                                cell.StyleId,
                                sourceStyleIndex,
                                cell.IgnoreFormulaError,
                                sheet.RichTextRuns.GetValueOrDefault(new CellAddress(sheet.Id, row, col))));
                    }

                    Array.Sort(cells, XlsxPatchCellEntry.Compare);
                    worksheets.Add(new XlsxWorksheetCellPatchBaseline(
                        sheet.Id,
                        sheet.Name,
                        worksheetPath,
                        sheet.CellCount,
                        sheet.StyleOnlyCellCount,
                        XlsxWorksheetDimensionBaseline.Capture(sheet),
                        sheet.MergedRegions.ToArray(),
                        XlsxWorksheetHyperlinkBaseline.Capture(sheet),
                        sourceHyperlinks,
                        XlsxWorksheetCommentBaseline.Capture(sheet),
                        sourceComments,
                        XlsxWorksheetViewBaseline.Capture(sheet),
                        XlsxWorksheetTablePatchBaseline.Capture(sheet),
                        sourceCellStyles.StyleOnlyCells,
                        cells));
                }

                var fingerprint = CreatePatchValidationModelFingerprint(workbook);

                return new XlsxCellPatchBaseline(
                    worksheets,
                    sourceStyleIndexesByStyleId,
                    chartSourceRanges,
                    pivotSourceRanges,
                    fingerprint);
            }
            catch
            {
                blockReason = "baseline_exception";
                return null;
            }
        }

        public XlsxCellPatchBaseline Rebase(Workbook workbook, string modelFingerprint)
        {
            if (workbook.SheetCount != _worksheets.Count)
                return this;

            var worksheets = new List<XlsxWorksheetCellPatchBaseline>(_worksheets.Count);
            for (var sheetIndex = 0; sheetIndex < _worksheets.Count; sheetIndex++)
            {
                var baseline = _worksheets[sheetIndex];
                var sheet = workbook.Sheets[sheetIndex];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal))
                {
                    return this;
                }

                var occupiedCells = sheet.GetOccupiedCellMap();
                var cells = new XlsxPatchCellEntry[occupiedCells.Count];
                var cellIndex = 0;
                foreach (var ((row, col), cell) in occupiedCells)
                {
                    string? sourceStyleIndex = null;
                    if (baseline.TryGetCell(row, col, out var original) &&
                        original.StyleId == cell.StyleId)
                    {
                        sourceStyleIndex = original.SourceStyleIndex;
                    }

                    cells[cellIndex++] = new XlsxPatchCellEntry(
                        row,
                        col,
                        new XlsxPatchCell(
                            cell.Value,
                            cell.FormulaText,
                            cell.ArrayMode,
                            cell.StyleId,
                            sourceStyleIndex,
                            cell.IgnoreFormulaError,
                            sheet.RichTextRuns.GetValueOrDefault(new CellAddress(sheet.Id, row, col))));
                }

                Array.Sort(cells, XlsxPatchCellEntry.Compare);
                worksheets.Add(baseline with
                {
                    CellCount = sheet.CellCount,
                    StyleOnlyCellCount = sheet.StyleOnlyCellCount,
                    Dimensions = XlsxWorksheetDimensionBaseline.Capture(sheet),
                    MergedRegions = sheet.MergedRegions.ToArray(),
                    Hyperlinks = XlsxWorksheetHyperlinkBaseline.Capture(sheet),
                    Comments = XlsxWorksheetCommentBaseline.Capture(sheet),
                    View = XlsxWorksheetViewBaseline.Capture(sheet),
                    Tables = XlsxWorksheetTablePatchBaseline.Capture(sheet),
                    Cells = cells
                });
            }

            return new XlsxCellPatchBaseline(
                worksheets,
                _sourceStyleIndexesByStyleId,
                _chartSourceRanges,
                _pivotSourceRanges,
                modelFingerprint);
        }

        public bool TryGetPatchableValueChanges(
            Workbook workbook,
            int changeLimit,
            string? currentModelFingerprint,
            out List<XlsxCellValuePatch> changes,
            out List<XlsxWorksheetDimensionPatch> dimensionChanges,
            out List<XlsxWorksheetMergeRegionPatch> mergeRegionChanges,
            out List<XlsxWorksheetHyperlinkPatch> hyperlinkChanges,
            out List<XlsxWorksheetCommentPatch> commentChanges,
            out List<XlsxWorksheetViewPatch> worksheetViewChanges,
            out string? currentPatchValidationModelFingerprint,
            out string? blockReason)
        {
            static bool Fail(string reason, out string? blockReason)
            {
                blockReason = reason;
                return false;
            }

            changes = [];
            dimensionChanges = [];
            mergeRegionChanges = [];
            hyperlinkChanges = [];
            commentChanges = [];
            worksheetViewChanges = [];
            currentPatchValidationModelFingerprint = null;
            blockReason = null;
            // R61-io-rich-text-runs-6-1: NativeJsonAdapter.SaveForPatchValidationFingerprint
            // unconditionally serializes Sheet.RichTextRuns (it is not gated by the
            // includeCells/includeCellStyles flags that deliberately exclude per-cell
            // Value/StyleId from this fingerprint, since those ARE covered by `changes` instead).
            // A rich-run edit is likewise fully covered by `changes` now, but the fingerprint
            // does not know that -- so every cell whose runs changed must be reverted to its
            // baseline snapshot for the duration of the modelMatches fingerprint comparison
            // below, then restored, exactly like Value/StyleId already are in
            // ModelMatchesWithOriginalValues. Otherwise ANY legitimate rich-run-only (or
            // rich-run-plus-style) edit would spuriously fail this safety net and force an
            // unnecessary full-save fallback regardless of what `changes` correctly captures.
            var richRunFingerprintReverts = new List<(Sheet Sheet, CellAddress Address, IReadOnlyList<CellTextRun>? OriginalRichRuns)>();
            if (workbook.SheetCount != _worksheets.Count)
                return Fail("change_sheet_count", out blockReason);

            if (!_chartSourceRanges.Matches(workbook))
                return Fail("change_chart_source_metadata", out blockReason);

            if (!_pivotSourceRanges.Matches(workbook))
                return Fail("change_pivot_source_metadata", out blockReason);

            for (var sheetIndex = 0; sheetIndex < _worksheets.Count; sheetIndex++)
            {
                var baseline = _worksheets[sheetIndex];
                var sheet = workbook.Sheets[sheetIndex];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal))
                {
                    return Fail("change_sheet_identity_or_style_only_cells", out blockReason);
                }

                if (!baseline.Tables.EqualsModel(XlsxWorksheetTablePatchBaseline.Capture(sheet)))
                    return Fail("change_table_metadata", out blockReason);

                if (!XlsxWorksheetDimensionPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.Dimensions,
                        XlsxWorksheetDimensionBaseline.Capture(sheet),
                        out var dimensionPatch))
                {
                    return Fail("change_dimension_metadata", out blockReason);
                }

                if (dimensionPatch is not null)
                {
                    if (baseline.Tables.HasTables)
                        return Fail("change_table_dimension_metadata", out blockReason);

                    if (dimensionPatch.ChangeCount > changeLimit)
                        return Fail("change_limit_dimensions", out blockReason);

                    dimensionChanges.Add(dimensionPatch);
                }

                if (!XlsxWorksheetMergeRegionPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.MergedRegions,
                        sheet.MergedRegions,
                        out var mergeRegionPatch))
                {
                    return Fail("change_merge_metadata", out blockReason);
                }

                if (mergeRegionPatch is not null)
                {
                    if (baseline.Tables.HasTables)
                        return Fail("change_table_merge_metadata", out blockReason);

                    if (mergeRegionPatch.ChangeCount > changeLimit)
                        return Fail("change_limit_merges", out blockReason);

                    mergeRegionChanges.Add(mergeRegionPatch);
                }

                if (!XlsxWorksheetHyperlinkPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.Hyperlinks,
                        baseline.SourceHyperlinks,
                        XlsxWorksheetHyperlinkBaseline.Capture(sheet),
                        out var hyperlinkPatch))
                {
                    return Fail("change_hyperlink_metadata", out blockReason);
                }

                if (hyperlinkPatch is not null)
                {
                    if (baseline.Tables.HasTables)
                        return Fail("change_table_hyperlink_metadata", out blockReason);

                    if (hyperlinkPatch.ChangeCount > changeLimit)
                        return Fail("change_limit_hyperlinks", out blockReason);

                    hyperlinkChanges.Add(hyperlinkPatch);
                }

                if (!XlsxWorksheetCommentPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.Comments,
                        baseline.SourceComments,
                        XlsxWorksheetCommentBaseline.Capture(sheet),
                        out var commentPatch))
                {
                    return Fail("change_comment_metadata", out blockReason);
                }

                if (commentPatch is not null)
                {
                    if (baseline.Tables.HasTables)
                        return Fail("change_table_comment_metadata", out blockReason);

                    if (commentPatch.ChangeCount > changeLimit)
                        return Fail("change_limit_comments", out blockReason);

                    commentChanges.Add(commentPatch);
                }

                if (!XlsxWorksheetViewPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.View,
                        XlsxWorksheetViewBaseline.Capture(sheet),
                        out var worksheetViewPatch))
                {
                    return Fail("change_worksheet_view_metadata", out blockReason);
                }

                if (worksheetViewPatch is not null)
                {
                    if (worksheetViewPatch.ChangeCount > changeLimit)
                        return Fail("change_limit_worksheet_views", out blockReason);

                    worksheetViewChanges.Add(worksheetViewPatch);
                }

                var addedCells = 0;
                var consumedSourceStyleOnlyCells = 0;
                var currentCells = sheet.GetOccupiedCellMap();
                foreach (var ((row, col), cell) in currentCells)
                {
                    if (!baseline.TryGetCell(row, col, out var original))
                    {
                        if (_chartSourceRanges.Contains(baseline.SheetId, row, col))
                            return Fail("change_chart_source_cell", out blockReason);

                        if (_pivotSourceRanges.Contains(baseline.SheetId, row, col))
                            return Fail("change_pivot_source_cell", out blockReason);

                        if (baseline.Tables.HasTables &&
                            !baseline.Tables.AllowsInsertedScalarValueCellPatch(row, col))
                        {
                            return Fail("change_table_inserted_cell", out blockReason);
                        }

                        StyleId originalStyleId;
                        string? originalSourceStyleIndex;
                        string? insertedSourceStyleIndex;
                        var consumesSourceStyleOnlyCell = baseline.TryGetSourceStyleOnlyCell(row, col, out var sourceStyleOnlyCell);
                        if (consumesSourceStyleOnlyCell)
                        {
                            if (cell.StyleId != sourceStyleOnlyCell.StyleId)
                                return Fail("change_inserted_style_only_cell", out blockReason);

                            originalStyleId = sourceStyleOnlyCell.StyleId;
                            originalSourceStyleIndex = sourceStyleOnlyCell.SourceStyleIndex;
                            insertedSourceStyleIndex = sourceStyleOnlyCell.SourceStyleIndex;
                            consumedSourceStyleOnlyCells++;
                        }
                        else if (cell.StyleId == StyleId.Default)
                        {
                            originalStyleId = StyleId.Default;
                            originalSourceStyleIndex = null;
                            insertedSourceStyleIndex = null;
                        }
                        else if (TryGetSourceStyleIndex(cell.StyleId, out var mappedSourceStyleIndex))
                        {
                            originalStyleId = StyleId.Default;
                            originalSourceStyleIndex = null;
                            insertedSourceStyleIndex = mappedSourceStyleIndex;
                        }
                        else
                        {
                            return Fail("change_inserted_cell", out blockReason);
                        }

                        if (cell.HasFormula ||
                            cell.IgnoreFormulaError ||
                            cell.Value is BlankValue ||
                            !IsPatchableScalarValue(cell.Value))
                        {
                            return Fail("change_inserted_cell", out blockReason);
                        }

                        changes.Add(new XlsxCellValuePatch(
                            XlsxCellValuePatchKind.InsertedLiteralValue,
                            baseline.SheetId,
                            baseline.WorksheetPath,
                            row,
                            col,
                            BlankValue.Instance,
                            cell.Value,
                            OriginalFormulaText: null,
                            NewFormulaText: null,
                            OriginalArrayMode: FormulaArrayMode.Dynamic,
                            NewArrayMode: FormulaArrayMode.Dynamic,
                            OriginalStyleId: originalStyleId,
                            NewStyleId: cell.StyleId,
                            OriginalSourceStyleIndex: originalSourceStyleIndex,
                            NewSourceStyleIndex: insertedSourceStyleIndex,
                            OriginalIgnoreFormulaError: false,
                            ConsumesSourceStyleOnlyCell: consumesSourceStyleOnlyCell,
                            RichRuns: cell.Value is TextValue
                                ? sheet.RichTextRuns.GetValueOrDefault(new CellAddress(baseline.SheetId, row, col))
                                : null));
                        if (cell.Value is TextValue &&
                            sheet.RichTextRuns.ContainsKey(new CellAddress(baseline.SheetId, row, col)))
                        {
                            // A brand-new cell has no baseline rich-run snapshot at all -- revert
                            // to "no override" (null) for the fingerprint comparison below.
                            richRunFingerprintReverts.Add((sheet, new CellAddress(baseline.SheetId, row, col), null));
                        }
                        if (changes.Count > changeLimit)
                            return Fail("change_limit_cells", out blockReason);

                        addedCells++;
                        continue;
                    }

                    if (cell.IgnoreFormulaError != original.IgnoreFormulaError)
                    {
                        return Fail("change_formula_error_metadata", out blockReason);
                    }

                    if ((cell.HasFormula || original.FormulaText is not null) &&
                        cell.ArrayMode != original.ArrayMode)
                    {
                        return Fail("change_formula_array_mode", out blockReason);
                    }

                    var styleChanged = cell.StyleId != original.StyleId;
                    var newSourceStyleIndex = original.SourceStyleIndex;
                    if (styleChanged && !TryGetSourceStyleIndex(cell.StyleId, out newSourceStyleIndex))
                        return Fail("change_new_style", out blockReason);

                    var formulaChanged = !string.Equals(cell.FormulaText, original.FormulaText, StringComparison.Ordinal);
                    var valueChanged = !Equals(cell.Value, original.Value);

                    // R61-io-rich-text-runs-6-1: a whole-cell command (e.g. ApplyStyleCommand) can
                    // clear/replace per-run formatting overrides in Sheet.RichTextRuns without the
                    // cell's own Value or resolved StyleId changing at all -- that edit must still
                    // count as a change, or it is silently invisible to patch-save forever.
                    var currentRichRuns = !cell.HasFormula && cell.Value is TextValue
                        ? sheet.RichTextRuns.GetValueOrDefault(new CellAddress(baseline.SheetId, row, col))
                        : null;
                    var runsChanged = !RichRunsEqual(currentRichRuns, original.RichRuns);

                    if (!formulaChanged && !valueChanged && !styleChanged && !runsChanged)
                        continue;

                    if (runsChanged)
                    {
                        // See the richRunFingerprintReverts revert/restore block below Fail-outs
                        // for why this is collected: CreatePatchValidationModelFingerprint
                        // unconditionally includes Sheet.RichTextRuns, so a rich-run edit must be
                        // temporarily reverted to its baseline value for that comparison, exactly
                        // like the cell Value/StyleId reverts ModelMatchesWithOriginalValues
                        // already performs for those fields.
                        richRunFingerprintReverts.Add((sheet, new CellAddress(baseline.SheetId, row, col), original.RichRuns));
                    }

                    if (_chartSourceRanges.Contains(baseline.SheetId, row, col))
                        return Fail("change_chart_source_cell", out blockReason);

                    if (_pivotSourceRanges.Contains(baseline.SheetId, row, col))
                        return Fail("change_pivot_source_cell", out blockReason);

                    if (baseline.Tables.HasTables &&
                        (!valueChanged ||
                         styleChanged ||
                         formulaChanged ||
                         original.FormulaText is not null ||
                         cell.HasFormula ||
                         !baseline.Tables.AllowsExistingScalarValueCellPatch(row, col)))
                    {
                        return Fail("change_table_cell", out blockReason);
                    }

                    if ((formulaChanged || valueChanged) && !IsPatchableScalarValue(cell.Value))
                    {
                        return Fail("change_non_scalar_value", out blockReason);
                    }

                    XlsxCellValuePatchKind patchKind;
                    var patchRichRunsChanged = false;
                    if (!formulaChanged && !valueChanged)
                    {
                        patchKind = XlsxCellValuePatchKind.CellStyle;
                        patchRichRunsChanged = runsChanged;
                    }
                    else if (formulaChanged)
                    {
                        if (string.IsNullOrWhiteSpace(original.FormulaText) ||
                            string.IsNullOrWhiteSpace(cell.FormulaText))
                        {
                            return Fail("change_formula_text", out blockReason);
                        }

                        patchKind = XlsxCellValuePatchKind.FormulaTextAndCachedValue;
                    }
                    else
                    {
                        patchKind = cell.HasFormula
                            ? XlsxCellValuePatchKind.FormulaCachedValue
                            : XlsxCellValuePatchKind.LiteralValue;
                        if (patchKind == XlsxCellValuePatchKind.LiteralValue && original.FormulaText is not null)
                            return Fail("change_formula_to_literal", out blockReason);
                    }

                    changes.Add(new XlsxCellValuePatch(
                        patchKind,
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        row,
                        col,
                        original.Value,
                        cell.Value,
                        original.FormulaText,
                        cell.FormulaText,
                        original.ArrayMode,
                        cell.ArrayMode,
                        original.StyleId,
                        cell.StyleId,
                        original.SourceStyleIndex,
                        newSourceStyleIndex,
                        original.IgnoreFormulaError,
                        RichRuns: (patchKind == XlsxCellValuePatchKind.LiteralValue && cell.Value is TextValue) ||
                                  (patchKind == XlsxCellValuePatchKind.CellStyle && patchRichRunsChanged)
                            ? currentRichRuns
                            : null,
                        RichRunsChanged: patchRichRunsChanged,
                        // R76-io-richtext-runs-4-1: only a run-formatting-only edit (CellStyle
                        // patch whose runs changed) needs the preserved phonetic guide re-emitted;
                        // a plain literal-value edit rewrites the text itself, for which any prior
                        // phonetic guide's <rPh> base-text offsets would no longer apply.
                        PhoneticGuide: patchKind == XlsxCellValuePatchKind.CellStyle && patchRichRunsChanged
                            ? sheet.CellPhoneticGuides.GetValueOrDefault(new CellAddress(baseline.SheetId, row, col))
                            : null));
                    if (changes.Count > changeLimit)
                        return Fail("change_limit_cells", out blockReason);
                }

                var deletedCells = 0;
                foreach (var entry in baseline.Cells)
                {
                    var row = entry.Row;
                    var col = entry.Col;
                    if (currentCells.ContainsKey((row, col)))
                        continue;

                    var original = entry.Cell;
                    if (baseline.Tables.HasTables)
                        return Fail("change_table_deleted_cell", out blockReason);

                    if (_chartSourceRanges.Contains(baseline.SheetId, row, col))
                        return Fail("change_chart_source_cell", out blockReason);

                    if (_pivotSourceRanges.Contains(baseline.SheetId, row, col))
                        return Fail("change_pivot_source_cell", out blockReason);

                    // A cell absent from sheet._cells (currentCells) is not necessarily gone: a
                    // dynamic-array/legacy-CSE spill member that vacated _cells into the sheet's
                    // spill-value store (e.g. a recalc shrank/reshaped the spilling formula's
                    // result after this baseline was captured) is still live data, just relocated.
                    // Treating it as DeletedCell would drop its <c> element from the saved package
                    // entirely (RewriteFormulaTextAndCachedCellValue/ApplyChanges has no path to
                    // patch it back in). Bail to the full-save fallback so the package is
                    // regenerated consistently instead of silently losing the cell.
                    if (sheet.TryGetArrayExtent(new CellAddress(baseline.SheetId, row, col), out _, out _, out _))
                        return Fail("change_deleted_cell_still_spilled", out blockReason);

                    changes.Add(new XlsxCellValuePatch(
                        XlsxCellValuePatchKind.DeletedCell,
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        row,
                        col,
                        original.Value,
                        BlankValue.Instance,
                        original.FormulaText,
                        NewFormulaText: null,
                        original.ArrayMode,
                        NewArrayMode: FormulaArrayMode.Dynamic,
                        original.StyleId,
                        NewStyleId: StyleId.Default,
                        original.SourceStyleIndex,
                        NewSourceStyleIndex: null,
                        original.IgnoreFormulaError));
                    if (changes.Count > changeLimit)
                        return Fail("change_limit_cells", out blockReason);

                    deletedCells++;
                }

                if (sheet.CellCount != baseline.CellCount + addedCells - deletedCells)
                    return Fail("change_cell_count_mismatch", out blockReason);

                if (sheet.StyleOnlyCellCount != baseline.StyleOnlyCellCount - consumedSourceStyleOnlyCells)
                    return Fail("change_sheet_identity_or_style_only_cells", out blockReason);
            }

            var savedRichRuns = new List<(Sheet Sheet, CellAddress Address, IReadOnlyList<CellTextRun>? Current)>(
                richRunFingerprintReverts.Count);
            foreach (var (revertSheet, address, originalRuns) in richRunFingerprintReverts)
            {
                savedRichRuns.Add((revertSheet, address, revertSheet.RichTextRuns.GetValueOrDefault(address)));
                if (originalRuns is { Count: > 0 })
                    revertSheet.RichTextRuns[address] = originalRuns;
                else
                    revertSheet.RichTextRuns.Remove(address);
            }

            bool modelMatches;
            try
            {
                if (ChangesOnlyExistingCells(
                        changes,
                        dimensionChanges,
                        mergeRegionChanges,
                        hyperlinkChanges,
                        commentChanges,
                        worksheetViewChanges))
                {
                    currentPatchValidationModelFingerprint = CreatePatchValidationModelFingerprint(workbook);
                    modelMatches = string.Equals(
                        _modelFingerprint,
                        currentPatchValidationModelFingerprint,
                        StringComparison.Ordinal);
                }
                else
                {
                    modelMatches = changes.Count == 0 &&
                           dimensionChanges.Count == 0 &&
                           mergeRegionChanges.Count == 0 &&
                           hyperlinkChanges.Count == 0 &&
                           commentChanges.Count == 0 &&
                           worksheetViewChanges.Count == 0 &&
                           currentModelFingerprint is not null
                        ? string.Equals(_modelFingerprint, currentModelFingerprint, StringComparison.Ordinal)
                        : ModelMatchesWithOriginalValues(
                            workbook,
                            changes,
                            dimensionChanges,
                            mergeRegionChanges,
                            hyperlinkChanges,
                            commentChanges,
                            worksheetViewChanges);
                }
            }
            finally
            {
                foreach (var (revertSheet, address, current) in savedRichRuns)
                {
                    if (current is { Count: > 0 })
                        revertSheet.RichTextRuns[address] = current;
                    else
                        revertSheet.RichTextRuns.Remove(address);
                }
            }

            return modelMatches || Fail("change_unsupported_model_delta", out blockReason);
        }

        private static bool ChangesOnlyExistingCells(
            IReadOnlyList<XlsxCellValuePatch> changes,
            IReadOnlyList<XlsxWorksheetDimensionPatch> dimensionChanges,
            IReadOnlyList<XlsxWorksheetMergeRegionPatch> mergeRegionChanges,
            IReadOnlyList<XlsxWorksheetHyperlinkPatch> hyperlinkChanges,
            IReadOnlyList<XlsxWorksheetCommentPatch> commentChanges,
            IReadOnlyList<XlsxWorksheetViewPatch> worksheetViewChanges) =>
            changes.Count > 0 &&
            dimensionChanges.Count == 0 &&
            mergeRegionChanges.Count == 0 &&
            hyperlinkChanges.Count == 0 &&
            commentChanges.Count == 0 &&
            worksheetViewChanges.Count == 0 &&
            changes.All(change =>
                change.Kind != XlsxCellValuePatchKind.InsertedLiteralValue &&
                change.Kind != XlsxCellValuePatchKind.DeletedCell);

        public static bool TryApplySimpleExistingCellChangesStreaming(
            ZipArchive archive,
            string worksheetPath,
            IReadOnlyList<XlsxCellValuePatch> changes,
            out int sharedStringReferencesRemoved)
        {
            sharedStringReferencesRemoved = 0;
            if (!CanStreamSimpleExistingCellChanges(changes))
                return false;

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                return false;

            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var changesByReference = new Dictionary<string, XlsxCellValuePatch>(
                changes.Count,
                StringComparer.OrdinalIgnoreCase);
            foreach (var change in changes)
            {
                var reference = ToReference(change.Row, change.Col);
                if (!changesByReference.TryAdd(reference, change))
                    return false;
            }

            using var patchedWorksheet = new MemoryStream();
            var found = 0;
            try
            {
                using (var source = worksheetEntry.Open())
                using (var reader = XmlReader.Create(source, SecureXmlReaderSettings.Create()))
                using (var writer = XmlWriter.Create(patchedWorksheet, CreatePatchXmlWriterSettings()))
                {
                    var hasNode = reader.Read();
                    while (hasNode)
                    {
                        if (reader.NodeType == XmlNodeType.Element &&
                            reader.LocalName == "c" &&
                            string.Equals(reader.NamespaceURI, worksheetNs.NamespaceName, StringComparison.Ordinal) &&
                            changesByReference.TryGetValue(reader.GetAttribute("r") ?? "", out var change))
                        {
                            var cell = XElement.ReadFrom(reader) as XElement;
                            if (cell is null)
                                return false;

                            if (!ApplySimpleExistingCellChange(cell, worksheetNs, change, out var removedSharedStringReference))
                                return false;
                            if (removedSharedStringReference)
                                sharedStringReferencesRemoved++;

                            cell.WriteTo(writer);
                            found++;
                            hasNode = reader.ReadState != ReadState.EndOfFile;
                            continue;
                        }

                        WriteCurrentXmlNode(reader, writer);
                        hasNode = reader.Read();
                    }
                }
            }
            catch
            {
                return false;
            }

            if (found != changes.Count)
                return false;

            worksheetEntry.Delete();
            var replacement = archive.CreateEntry(worksheetPath, CompressionLevel.Fastest);
            patchedWorksheet.Position = 0;
            using var replacementStream = replacement.Open();
            patchedWorksheet.CopyTo(replacementStream);
            return true;
        }

        private static XmlWriterSettings CreatePatchXmlWriterSettings() => new()
        {
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            CloseOutput = false
        };

        private static bool CanStreamSimpleExistingCellChanges(IReadOnlyList<XlsxCellValuePatch> changes)
        {
            if (changes.Count == 0)
                return false;

            foreach (var change in changes)
            {
                if (change.Kind is not (
                    XlsxCellValuePatchKind.LiteralValue or
                    XlsxCellValuePatchKind.FormulaCachedValue or
                    XlsxCellValuePatchKind.CellStyle))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ApplySimpleExistingCellChange(
            XElement cell,
            XNamespace worksheetNs,
            XlsxCellValuePatch change,
            out bool removedSharedStringReference)
        {
            removedSharedStringReference = false;
            if (change.Kind == XlsxCellValuePatchKind.LiteralValue)
            {
                removedSharedStringReference = RewriteLiteralCellValue(cell, worksheetNs, change.NewValue, change.RichRuns, change.PhoneticGuide);
            }
            else if (change.Kind == XlsxCellValuePatchKind.FormulaCachedValue)
            {
                if (!RewriteFormulaCachedCellValue(cell, worksheetNs, change.NewValue))
                    return false;
            }
            else if (change.Kind == XlsxCellValuePatchKind.CellStyle && change.RichRunsChanged)
            {
                // R61-io-rich-text-runs-6-1: the cell's plain Value/StyleId didn't change, but its
                // rich-text run overrides did (e.g. a whole-cell style command cleared stale
                // per-run formatting) -- rewrite the <is>/run content so that edit isn't silently
                // dropped from the saved package. change.NewValue is the cell's own (unchanged)
                // current value here, so this only touches run formatting, not the text itself.
                // R76-io-richtext-runs-4-1: change.PhoneticGuide re-emits the cell's preserved
                // <rPh>/<phoneticPr> so a run-formatting-only edit doesn't drop it.
                removedSharedStringReference = RewriteLiteralCellValue(cell, worksheetNs, change.NewValue, change.RichRuns, change.PhoneticGuide);
            }

            if (change.HasStyleChange)
                ApplyCellStyle(cell, change.NewSourceStyleIndex);

            return true;
        }

        private static void WriteCurrentXmlNode(XmlReader reader, XmlWriter writer)
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    writer.WriteStartElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                        {
                            writer.WriteStartAttribute(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                            writer.WriteString(reader.Value);
                            writer.WriteEndAttribute();
                        }

                        reader.MoveToElement();
                    }

                    if (reader.IsEmptyElement)
                        writer.WriteEndElement();
                    break;

                case XmlNodeType.EndElement:
                    writer.WriteFullEndElement();
                    break;

                case XmlNodeType.Text:
                    writer.WriteString(reader.Value);
                    break;

                case XmlNodeType.CDATA:
                    writer.WriteCData(reader.Value);
                    break;

                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    writer.WriteWhitespace(reader.Value);
                    break;

                case XmlNodeType.Comment:
                    writer.WriteComment(reader.Value);
                    break;

                case XmlNodeType.ProcessingInstruction:
                    writer.WriteProcessingInstruction(reader.Name, reader.Value);
                    break;

                case XmlNodeType.XmlDeclaration:
                    writer.WriteProcessingInstruction(reader.Name, reader.Value);
                    break;

                case XmlNodeType.DocumentType:
                    writer.WriteDocType(reader.Name, reader.GetAttribute("PUBLIC"), reader.GetAttribute("SYSTEM"), reader.Value);
                    break;

                case XmlNodeType.EntityReference:
                    writer.WriteEntityRef(reader.Name);
                    break;
            }
        }

        public static bool ApplyChanges(
            XDocument worksheetXml,
            IEnumerable<XlsxCellValuePatch> changes,
            out int sharedStringReferencesRemoved)
        {
            sharedStringReferencesRemoved = 0;
            var root = worksheetXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            var sheetData = root.Element(worksheetNs + "sheetData");
            if (sheetData is null)
                return false;

            // Streaming writers may omit the optional r attribute on <row> elements (position is
            // implied by document order).  Patch-save cannot reliably identify such rows by number,
            // and inserting a <row r="N"> alongside an r-less row would create two logical rows for
            // the same position.  Bail out to the full-save fallback for this sheet.
            if (SheetDataHasRLessRows(sheetData, worksheetNs))
                return false;

            foreach (var change in changes)
            {
                var cell = FindCell(sheetData, worksheetNs, change.Row, change.Col);
                if (change.Kind == XlsxCellValuePatchKind.InsertedLiteralValue)
                {
                    if (cell is null)
                    {
                        if (!InsertLiteralCell(
                            sheetData,
                            worksheetNs,
                            change.Row,
                            change.Col,
                            change.NewValue,
                            change.NewSourceStyleIndex,
                            change.RichRuns,
                            change.PhoneticGuide))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!change.ConsumesSourceStyleOnlyCell ||
                        !RewriteStyleOnlyCellAsLiteral(cell, worksheetNs, change.NewValue, change.NewSourceStyleIndex, change.RichRuns, change.PhoneticGuide))
                    {
                        return false;
                    }

                    continue;
                }

                if (cell is null)
                    return false;

                if (change.Kind == XlsxCellValuePatchKind.DeletedCell)
                {
                    // A cell whose <f> element carries attributes (t="shared"/"array"/"dataTable",
                    // ref, si, ...) can be the master of a shared/array formula group that other
                    // cells reference by si index. Removing the <c> element outright would delete
                    // the only place the formula text/ref is stored, orphaning sibling cells with
                    // <f t="shared" si="N"/> and no master -- corrupting the package. Bail to the
                    // full-save fallback (mirrors the guard in RewriteFormulaTextAndCachedCellValue)
                    // so the whole package is regenerated consistently instead of patched in place.
                    var deletedFormula = cell.Element(worksheetNs + "f");
                    if (deletedFormula is not null && deletedFormula.HasAttributes)
                        return false;

                    // Mirror the shared-string bookkeeping RewriteLiteralCellValue performs for the
                    // overwrite case (R52-io-sst-shared-inline-3-1): a deleted/cleared cell that was a
                    // t="s" shared-string reference must also count toward sharedStringReferencesRemoved
                    // so the caller decrements xl/sharedStrings.xml's <sst count="..."> total. Otherwise
                    // the count is permanently overstated after a Delete-key/clear-contents action.
                    if (string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal))
                        sharedStringReferencesRemoved++;

                    cell.Remove();
                }
                else if (change.Kind == XlsxCellValuePatchKind.FormulaTextAndCachedValue)
                {
                    if (!RewriteFormulaTextAndCachedCellValue(
                            cell,
                            worksheetNs,
                            change.NewFormulaText,
                            change.NewValue))
                    {
                        return false;
                    }
                }
                else if (change.Kind == XlsxCellValuePatchKind.FormulaCachedValue)
                {
                    if (!RewriteFormulaCachedCellValue(cell, worksheetNs, change.NewValue))
                        return false;
                }
                else if (change.Kind == XlsxCellValuePatchKind.CellStyle)
                {
                    // Style-only changes intentionally leave cell contents and formulas untouched,
                    // UNLESS the cell's rich-text run overrides changed independently of its plain
                    // Value/resolved StyleId (R61-io-rich-text-runs-6-1) -- e.g. a whole-cell style
                    // command clearing stale per-run formatting. change.NewValue is the cell's own
                    // (unchanged) current value here, so this only rewrites run formatting.
                    if (change.RichRunsChanged &&
                        RewriteLiteralCellValue(cell, worksheetNs, change.NewValue, change.RichRuns, change.PhoneticGuide))
                    {
                        sharedStringReferencesRemoved++;
                    }
                }
                else
                {
                    if (RewriteLiteralCellValue(cell, worksheetNs, change.NewValue, change.RichRuns, change.PhoneticGuide))
                        sharedStringReferencesRemoved++;
                }

                if (change.HasStyleChange)
                    ApplyCellStyle(cell, change.NewSourceStyleIndex);
            }

            UpdateDimension(sheetData, root, worksheetNs);

            return true;
        }

        public static bool ApplyDimensionChanges(
            XDocument worksheetXml,
            XlsxWorksheetDimensionPatch patch)
        {
            var root = worksheetXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            var sheetData = root.Element(worksheetNs + "sheetData");
            if (sheetData is null)
                return false;

            foreach (var row in patch.ChangedRows)
            {
                if (!ApplyRowDimension(sheetData, worksheetNs, patch.Current, row))
                    return false;
            }

            return ApplyColumnDimensions(root, worksheetNs, patch);
        }

        public static bool ApplyMergeRegionChanges(
            XDocument worksheetXml,
            XlsxWorksheetMergeRegionPatch patch)
        {
            var root = worksheetXml.Root;
            if (root is null || !XlsxWorksheetMergeRegionPatch.ArePatchable(patch.SheetId, patch.Current))
                return false;

            var worksheetNs = root.Name.Namespace;
            var mergeCells = root.Element(worksheetNs + "mergeCells");
            if (patch.Current.Count == 0)
            {
                mergeCells?.Remove();
                return true;
            }

            var existingByReference = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            if (mergeCells is not null)
            {
                foreach (var child in mergeCells.Elements())
                {
                    if (child.Name != worksheetNs + "mergeCell")
                        return false;

                    var reference = child.Attribute("ref")?.Value;
                    if (string.IsNullOrWhiteSpace(reference) ||
                        !existingByReference.TryAdd(reference, child))
                    {
                        return false;
                    }
                }
            }

            mergeCells ??= new XElement(worksheetNs + "mergeCells");
            mergeCells.RemoveNodes();
            foreach (var region in patch.Current)
            {
                var reference = FormatMergeReference(region);
                if (existingByReference.TryGetValue(reference, out var existing))
                {
                    var preserved = new XElement(existing);
                    preserved.SetAttributeValue("ref", reference);
                    mergeCells.Add(preserved);
                }
                else
                {
                    mergeCells.Add(new XElement(
                        worksheetNs + "mergeCell",
                        new XAttribute("ref", reference)));
                }
            }

            mergeCells.SetAttributeValue("count", patch.Current.Count.ToString(CultureInfo.InvariantCulture));
            if (mergeCells.Parent is null)
                InsertMergeCellsElement(root, worksheetNs, mergeCells);

            ExtendDimensionForMergeRegions(root, worksheetNs, patch.Current);

            return true;
        }

        // R60-io-sheet-dimension-usedrange-6-2: a merge-only patch (no cell-value changes) never
        // runs UpdateDimension, so a new merge over previously blank/default cells left the sheet's
        // <dimension> ref stale (understating the used range / Ctrl+End extent versus the freshly
        // written <mergeCells>). UpdateDimension itself only scans <c> elements, which a merge over
        // blank cells never creates, so it cannot be reused as-is; instead grow the existing
        // dimension bounds (never shrink) to cover every current merge region's reference.
        private static void ExtendDimensionForMergeRegions(
            XElement worksheetRoot,
            XNamespace worksheetNs,
            IReadOnlyList<GridRange> mergeRegions)
        {
            if (mergeRegions.Count == 0)
                return;

            var dimension = worksheetRoot.Element(worksheetNs + "dimension");
            var reference = dimension?.Attribute("ref")?.Value;
            if (dimension is null ||
                string.IsNullOrWhiteSpace(reference) ||
                !TryParseDimensionRange(reference, out var minRow, out var minCol, out var maxRow, out var maxCol))
            {
                return;
            }

            var extended = false;
            foreach (var region in mergeRegions)
            {
                if (region.Start.Row < minRow) { minRow = region.Start.Row; extended = true; }
                if (region.Start.Col < minCol) { minCol = region.Start.Col; extended = true; }
                if (region.End.Row > maxRow) { maxRow = region.End.Row; extended = true; }
                if (region.End.Col > maxCol) { maxCol = region.End.Col; extended = true; }
            }

            if (!extended)
                return;

            var start = ToReference(minRow, minCol);
            var end = ToReference(maxRow, maxCol);
            dimension.SetAttributeValue("ref", start == end ? start : $"{start}:{end}");
        }

        private static bool TryParseDimensionRange(
            string reference,
            out uint minRow,
            out uint minCol,
            out uint maxRow,
            out uint maxCol)
        {
            minRow = minCol = maxRow = maxCol = 0;
            var separatorIndex = reference.IndexOf(':');
            if (separatorIndex < 0)
            {
                if (!TryParseCellReference(reference, out minRow, out minCol))
                    return false;

                maxRow = minRow;
                maxCol = minCol;
                return true;
            }

            var startReference = reference[..separatorIndex];
            var endReference = reference[(separatorIndex + 1)..];
            return TryParseCellReference(startReference, out minRow, out minCol) &&
                   TryParseCellReference(endReference, out maxRow, out maxCol);
        }

        public static bool ApplyHyperlinkChanges(
            XDocument worksheetXml,
            XlsxWorksheetHyperlinkPatch patch)
        {
            var root = worksheetXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var hyperlinks = root.Element(worksheetNs + "hyperlinks");
            if (hyperlinks is null)
                return false;

            var hyperlinksByReference = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var hyperlink in hyperlinks.Elements(worksheetNs + "hyperlink"))
            {
                var reference = hyperlink.Attribute("ref")?.Value;
                if (string.IsNullOrWhiteSpace(reference) ||
                    hyperlink.Attribute(relNs + "id") is not null ||
                    !hyperlinksByReference.TryAdd(reference, hyperlink))
                {
                    return false;
                }
            }

            foreach (var change in patch.Changes)
            {
                if (!hyperlinksByReference.TryGetValue(change.Reference, out var hyperlink))
                    return false;

                hyperlink.SetAttributeValue("location", QuoteInternalHyperlinkAddress(change.NewLocation));
                if (string.IsNullOrWhiteSpace(change.NewTooltip))
                    hyperlink.SetAttributeValue("tooltip", null);
                else
                    hyperlink.SetAttributeValue("tooltip", XlsxXmlTextEscaper.EscapeForXml(change.NewTooltip));
            }

            return true;
        }

        public static bool ApplyWorksheetViewChanges(
            XDocument worksheetXml,
            XlsxWorksheetViewPatch patch)
        {
            var sheet = new Sheet(patch.SheetId, patch.WorksheetPath);
            patch.Current.ApplyTo(sheet);
            // XlsxWorksheetViewBaseline now tracks ViewTopRow/ViewLeftCol (the scroll position),
            // so patch.Current.ApplyTo above already seeded the synthetic Sheet with the live
            // model's current scroll position -- write that, not whatever was previously on disk.

            // XlsxWorksheetViewWriter.UpdateSheetView only knows how to emit a <pane> element for
            // the split-pane case (state="split"); it has no support for writing a frozen-pane
            // (state="frozen"/"frozenSplit") <pane> element, never touches an existing one, and
            // never removes a <pane> element to represent unfreezing/un-splitting. If
            // FrozenRows/FrozenCols or SplitRow/SplitColumn actually changed as part of this patch,
            // that sub-change cannot be represented by the writer at all -- rather than silently
            // reverting the user's freeze/split change (which then gets baked into the new baseline
            // as "already on disk" and is permanently lost), escalate to a full save so the
            // authoritative full-save writer (which does handle freeze/split correctly) applies it.
            var (existingFrozenRows, existingFrozenCols, _, _) =
                ReadExistingPaneState(worksheetXml);
            if (patch.Original.FrozenRows != patch.Current.FrozenRows ||
                patch.Original.FrozenCols != patch.Current.FrozenCols)
            {
                return false;
            }

            if (sheet.FrozenRows == 0 && sheet.FrozenCols == 0 &&
                (patch.Original.SplitRow != patch.Current.SplitRow ||
                 patch.Original.SplitColumn != patch.Current.SplitColumn) &&
                (existingFrozenRows > 0 || existingFrozenCols > 0 ||
                 patch.Current.SplitRow is null && patch.Current.SplitColumn is null))
            {
                // Either the on-disk pane is currently frozen (not split) and the writer can't
                // convert a frozen pane into a split one, or the split is being removed entirely
                // (writer never removes a <pane> element) -- neither is representable by the
                // in-place writer, so escalate to a full save instead of silently reverting.
                return false;
            }

            return XlsxWorksheetViewWriter.UpdateSheetView(worksheetXml, sheet);
        }

        private static (uint FrozenRows, uint FrozenCols, uint? SplitRow, uint? SplitColumn) ReadExistingPaneState(
            XDocument worksheetXml)
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheetView = FindPrimarySheetView(worksheetXml);
            var pane = sheetView?.Element(worksheetNs + "pane");
            if (pane is null)
                return (0, 0, null, null);

            var paneState = pane.Attribute("state")?.Value;
            var rowSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane.Attribute("ySplit")?.Value);
            var columnSplit = XlsxWorksheetXmlValueParser.ParsePaneSplit(pane.Attribute("xSplit")?.Value);
            var isFrozen = paneState is "frozen" or "frozenSplit";
            var frozenRows = isFrozen ? XlsxWorksheetXmlValueParser.ValidFrozenRowsOrZero(rowSplit ?? 0) : 0;
            var frozenCols = isFrozen ? XlsxWorksheetXmlValueParser.ValidFrozenColumnsOrZero(columnSplit ?? 0) : 0;
            var splitRow = isFrozen ? null : rowSplit;
            var splitColumn = isFrozen ? null : columnSplit;
            return (frozenRows, frozenCols, splitRow, splitColumn);
        }

        private static XElement? FindPrimarySheetView(XDocument worksheetXml)
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheetViews = worksheetXml.Root?.Element(worksheetNs + "sheetViews");
            if (sheetViews is null)
                return null;

            foreach (var candidateView in sheetViews.Elements(worksheetNs + "sheetView"))
            {
                if (string.Equals(candidateView.Attribute("workbookViewId")?.Value ?? "0", "0", StringComparison.Ordinal))
                    return candidateView;
            }

            return null;
        }

        public static bool ApplyCommentChanges(
            XDocument commentsXml,
            IEnumerable<XlsxWorksheetCommentPatch> patches)
        {
            var root = commentsXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            var commentList = root.Element(worksheetNs + "commentList");
            if (commentList is null)
                return false;

            var commentsByReference = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var comment in commentList.Elements(worksheetNs + "comment"))
            {
                var reference = comment.Attribute("ref")?.Value;
                if (string.IsNullOrWhiteSpace(reference) ||
                    !commentsByReference.TryAdd(reference, comment))
                {
                    return false;
                }
            }

            foreach (var patch in patches)
            {
                foreach (var change in patch.Changes)
                {
                    if (!commentsByReference.TryGetValue(change.Reference, out var comment) ||
                        !TryGetPatchableCommentTextElement(comment, worksheetNs, out var textElement))
                    {
                        return false;
                    }

                    textElement.Value = XlsxXmlTextEscaper.EscapeForXml(change.NewText);
                    if (change.NewText.Length > 0 &&
                        (char.IsWhiteSpace(change.NewText[0]) || char.IsWhiteSpace(change.NewText[^1])))
                    {
                        textElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                    }
                    else
                    {
                        textElement.SetAttributeValue(XNamespace.Xml + "space", null);
                    }
                }
            }

            return true;
        }

        private static bool TryGetPatchableCommentTextElement(
            XElement comment,
            XNamespace worksheetNs,
            out XElement textElement)
        {
            textElement = null!;
            var text = comment.Element(worksheetNs + "text");
            if (text is null)
                return false;

            var runs = text.Elements(worksheetNs + "r").ToList();
            if (runs.Count == 1)
            {
                var run = runs[0];
                if (run.Elements().Any(element => element.Name != worksheetNs + "t"))
                    return false;

                var t = run.Element(worksheetNs + "t");
                if (t is null || run.Elements(worksheetNs + "t").Skip(1).Any())
                    return false;

                textElement = t;
                return true;
            }

            if (runs.Count > 0 || text.Elements().Any(element => element.Name != worksheetNs + "t"))
                return false;

            var directText = text.Element(worksheetNs + "t");
            if (directText is null || text.Elements(worksheetNs + "t").Skip(1).Any())
                return false;

            textElement = directText;
            return true;
        }

        private static string FormatMergeReference(GridRange region)
        {
            var start = ToReference(region.Start.Row, region.Start.Col);
            var end = ToReference(region.End.Row, region.End.Col);
            return $"{start}:{end}";
        }

        private static void InsertMergeCellsElement(
            XElement root,
            XNamespace worksheetNs,
            XElement mergeCells)
        {
            string[] laterWorksheetElements =
            [
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "drawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ];
            var insertionPoint = FindWorksheetInsertionPoint(root, worksheetNs, laterWorksheetElements);
            if (insertionPoint is null)
                root.Add(mergeCells);
            else
                insertionPoint.AddBeforeSelf(mergeCells);
        }

        private static XElement? FindWorksheetInsertionPoint(
            XElement root,
            XNamespace worksheetNs,
            IReadOnlyCollection<string> laterWorksheetElements)
        {
            foreach (var element in root.Elements())
            {
                if (IsWorksheetInsertionMarker(element, worksheetNs, laterWorksheetElements))
                    return element;
            }

            return null;
        }

        private static bool IsWorksheetInsertionMarker(
            XElement element,
            XNamespace worksheetNs,
            IReadOnlyCollection<string> laterWorksheetElements) =>
            element.Name.Namespace == worksheetNs &&
            laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal);

        private static bool ApplyRowDimension(
            XElement sheetData,
            XNamespace worksheetNs,
            XlsxWorksheetDimensionBaseline current,
            uint row)
        {
            var hasHeight = TryGetFinitePositiveDimension(current.RowHeights, row, out var height);
            var hidden = current.HiddenRows.Contains(row);
            if (!hasHeight && !hidden)
            {
                var existingRow = FindRow(sheetData, worksheetNs, row);
                if (existingRow is null)
                    return true;

                existingRow.SetAttributeValue("ht", null);
                existingRow.SetAttributeValue("customHeight", null);
                existingRow.SetAttributeValue("hidden", null);
                if (!HasMeaningfulRowContent(existingRow, worksheetNs))
                    existingRow.Remove();
                return true;
            }

            var rowElement = FindOrCreateRow(sheetData, worksheetNs, row);
            if (rowElement is null)
                return false;

            if (hasHeight)
            {
                rowElement.SetAttributeValue("ht", FormatDimensionDouble(height * (72.0 / 96.0)));
                rowElement.SetAttributeValue("customHeight", "1");
            }
            else
            {
                rowElement.SetAttributeValue("ht", null);
                rowElement.SetAttributeValue("customHeight", null);
            }

            if (hidden)
                rowElement.SetAttributeValue("hidden", "1");
            else
                rowElement.SetAttributeValue("hidden", null);

            return true;
        }

        private static bool ApplyColumnDimensions(
            XElement root,
            XNamespace worksheetNs,
            XlsxWorksheetDimensionPatch patch)
        {
            if (patch.ChangedColumns.Count == 0)
                return true;

            var cols = root.Element(worksheetNs + "cols");
            if (cols is null)
            {
                cols = new XElement(worksheetNs + "cols");
                InsertColsElement(root, worksheetNs, cols);
            }

            foreach (var column in patch.ChangedColumns)
            {
                var columnElement = FindOrCreateColumn(cols, worksheetNs, column);
                if (columnElement is null)
                    return false;

                var hasWidth = TryGetFinitePositiveDimension(patch.Current.ColumnWidths, column, out var width);
                if (hasWidth)
                {
                    columnElement.SetAttributeValue("width", FormatDimensionDouble(width));
                    columnElement.SetAttributeValue("customWidth", "1");
                    if (columnElement.Attribute("style")?.Value == "0")
                        columnElement.SetAttributeValue("style", null);
                }
                else
                {
                    columnElement.SetAttributeValue("width", null);
                    columnElement.SetAttributeValue("customWidth", null);
                }

                if (patch.Current.HiddenCols.Contains(column))
                    columnElement.SetAttributeValue("hidden", "1");
                else
                    columnElement.SetAttributeValue("hidden", null);

                if (!HasMeaningfulColumnAttributes(columnElement))
                    columnElement.Remove();
            }

            if (!cols.Elements(worksheetNs + "col").Any())
                cols.Remove();

            return true;
        }

        private static XElement? FindRow(XElement sheetData, XNamespace worksheetNs, uint row)
        {
            foreach (var rowElement in sheetData.Elements(worksheetNs + "row"))
            {
                if (uint.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber) &&
                    rowNumber == row)
                {
                    return rowElement;
                }
            }

            return null;
        }

        private static XElement? FindOrCreateColumn(XElement cols, XNamespace worksheetNs, uint column)
        {
            var colName = worksheetNs + "col";
            foreach (var col in cols.Elements(colName).ToList())
            {
                if (!uint.TryParse(col.Attribute("min")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) ||
                    !uint.TryParse(col.Attribute("max")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) ||
                    min == 0 ||
                    max < min ||
                    column < min ||
                    column > max)
                {
                    continue;
                }

                var replacements = new List<XElement>(3);
                if (min < column)
                {
                    var before = new XElement(col);
                    before.SetAttributeValue("min", min.ToString(CultureInfo.InvariantCulture));
                    before.SetAttributeValue("max", (column - 1).ToString(CultureInfo.InvariantCulture));
                    replacements.Add(before);
                }

                var target = new XElement(col);
                target.SetAttributeValue("min", column.ToString(CultureInfo.InvariantCulture));
                target.SetAttributeValue("max", column.ToString(CultureInfo.InvariantCulture));
                replacements.Add(target);

                if (column < max)
                {
                    var after = new XElement(col);
                    after.SetAttributeValue("min", (column + 1).ToString(CultureInfo.InvariantCulture));
                    after.SetAttributeValue("max", max.ToString(CultureInfo.InvariantCulture));
                    replacements.Add(after);
                }

                col.ReplaceWith(replacements);
                return target;
            }

            var created = new XElement(
                colName,
                new XAttribute("min", column.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("max", column.ToString(CultureInfo.InvariantCulture)));
            foreach (var existing in cols.Elements(colName))
            {
                if (uint.TryParse(existing.Attribute("min")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var existingMin) &&
                    existingMin > column)
                {
                    existing.AddBeforeSelf(created);
                    return created;
                }
            }

            cols.Add(created);
            return created;
        }

        private static bool HasMeaningfulRowContent(XElement row, XNamespace worksheetNs)
        {
            if (row.Elements(worksheetNs + "c").Any())
                return true;

            foreach (var attribute in row.Attributes())
            {
                if (attribute.Name.LocalName is not ("r" or "ht" or "customHeight" or "hidden" or "spans"))
                    return true;
            }

            return false;
        }

        private static bool HasMeaningfulColumnAttributes(XElement col)
        {
            foreach (var attribute in col.Attributes())
            {
                var name = attribute.Name.LocalName;
                if (name == "width")
                    return true;

                if (name == "hidden")
                    return XlsxWorksheetXmlValueParser.IsTruthy(attribute.Value);

                if (name is "min" or "max" or "customWidth")
                    continue;

                if (name == "style" && attribute.Value == "0")
                    continue;

                return true;
            }

            return false;
        }

        private static void InsertColsElement(XElement root, XNamespace worksheetNs, XElement cols)
        {
            if (root.Element(worksheetNs + "sheetData") is { } sheetData)
            {
                sheetData.AddBeforeSelf(cols);
                return;
            }

            var anchor = root.Element(worksheetNs + "sheetFormatPr") ??
                root.Element(worksheetNs + "sheetViews") ??
                root.Element(worksheetNs + "dimension");
            if (anchor is not null)
                anchor.AddAfterSelf(cols);
            else
                root.AddFirst(cols);
        }

        private static bool TryGetFinitePositiveDimension(
            IReadOnlyDictionary<uint, double> values,
            uint key,
            out double value)
        {
            if (values.TryGetValue(key, out value) &&
                double.IsFinite(value) &&
                value > 0)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static string FormatDimensionDouble(double value) =>
            value.ToString("0.################", CultureInfo.InvariantCulture);

        public XlsxCellPatchBaseline WithAppliedChanges(
            IReadOnlyList<XlsxCellValuePatch> changes,
            IReadOnlyList<XlsxWorksheetDimensionPatch> dimensionChanges,
            IReadOnlyList<XlsxWorksheetMergeRegionPatch> mergeRegionChanges,
            IReadOnlyList<XlsxWorksheetHyperlinkPatch> hyperlinkChanges,
            IReadOnlyList<XlsxWorksheetCommentPatch> commentChanges,
            IReadOnlyList<XlsxWorksheetViewPatch> worksheetViewChanges,
            string modelFingerprint)
        {
            if (changes.Count == 0 &&
                dimensionChanges.Count == 0 &&
                mergeRegionChanges.Count == 0 &&
                hyperlinkChanges.Count == 0 &&
                commentChanges.Count == 0 &&
                worksheetViewChanges.Count == 0)
            {
                return new XlsxCellPatchBaseline(
                    _worksheets,
                    _sourceStyleIndexesByStyleId,
                    _chartSourceRanges,
                    _pivotSourceRanges,
                    modelFingerprint);
            }

            var changesBySheet = changes
                .GroupBy(change => change.SheetId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var dimensionChangesBySheet = dimensionChanges
                .ToDictionary(change => change.SheetId);
            var mergeRegionChangesBySheet = mergeRegionChanges
                .ToDictionary(change => change.SheetId);
            var hyperlinkChangesBySheet = hyperlinkChanges
                .ToDictionary(change => change.SheetId);
            var commentChangesBySheet = commentChanges
                .ToDictionary(change => change.SheetId);
            var worksheetViewChangesBySheet = worksheetViewChanges
                .ToDictionary(change => change.SheetId);
            var worksheets = new List<XlsxWorksheetCellPatchBaseline>(_worksheets.Count);
            foreach (var baseline in _worksheets)
            {
                changesBySheet.TryGetValue(baseline.SheetId, out var sheetChanges);
                dimensionChangesBySheet.TryGetValue(baseline.SheetId, out var dimensionPatch);
                mergeRegionChangesBySheet.TryGetValue(baseline.SheetId, out var mergeRegionPatch);
                hyperlinkChangesBySheet.TryGetValue(baseline.SheetId, out var hyperlinkPatch);
                commentChangesBySheet.TryGetValue(baseline.SheetId, out var commentPatch);
                worksheetViewChangesBySheet.TryGetValue(baseline.SheetId, out var worksheetViewPatch);
                if ((sheetChanges is null || sheetChanges.Count == 0) &&
                    dimensionPatch is null &&
                    mergeRegionPatch is null &&
                    hyperlinkPatch is null &&
                    commentPatch is null &&
                    worksheetViewPatch is null)
                {
                    worksheets.Add(baseline);
                    continue;
                }

                var cells = baseline.WithAppliedCellChanges(sheetChanges ?? []);
                var inserted = CountCellPatchChanges(sheetChanges, XlsxCellValuePatchKind.InsertedLiteralValue);
                var deleted = CountCellPatchChanges(sheetChanges, XlsxCellValuePatchKind.DeletedCell);
                var consumedStyleOnly = CountConsumedSourceStyleOnlyCells(sheetChanges);

                worksheets.Add(baseline with
                {
                    CellCount = baseline.CellCount + inserted - deleted,
                    StyleOnlyCellCount = baseline.StyleOnlyCellCount - consumedStyleOnly,
                    Dimensions = dimensionPatch?.Current ?? baseline.Dimensions,
                    MergedRegions = mergeRegionPatch?.Current ?? baseline.MergedRegions,
                    Hyperlinks = hyperlinkPatch?.Current ?? baseline.Hyperlinks,
                    SourceHyperlinks = hyperlinkPatch?.CurrentSource ?? baseline.SourceHyperlinks,
                    Comments = commentPatch?.Current ?? baseline.Comments,
                    SourceComments = commentPatch?.CurrentSource ?? baseline.SourceComments,
                    View = worksheetViewPatch?.Current ?? baseline.View,
                    SourceStyleOnlyCells = baseline.WithConsumedSourceStyleOnlyCells(sheetChanges ?? []),
                    Cells = cells
                });
            }

            return new XlsxCellPatchBaseline(
                worksheets,
                _sourceStyleIndexesByStyleId,
                _chartSourceRanges,
                _pivotSourceRanges,
                modelFingerprint);
        }

        private bool ModelMatchesWithOriginalValues(
            Workbook workbook,
            IReadOnlyList<XlsxCellValuePatch> changes,
            IReadOnlyList<XlsxWorksheetDimensionPatch> dimensionChanges,
            IReadOnlyList<XlsxWorksheetMergeRegionPatch> mergeRegionChanges,
            IReadOnlyList<XlsxWorksheetHyperlinkPatch> hyperlinkChanges,
            IReadOnlyList<XlsxWorksheetCommentPatch> commentChanges,
            IReadOnlyList<XlsxWorksheetViewPatch> worksheetViewChanges)
        {
            var restoredCells = new List<(
                Cell Cell,
                ScalarValue CurrentValue,
                string? CurrentFormulaText,
                FormulaArrayMode CurrentArrayMode,
                StyleId CurrentStyleId,
                bool CurrentIgnoreFormulaError)>(changes.Count);
            var insertedCells = new List<(Sheet Sheet, uint Row, uint Col, Cell CurrentCell)>();
            var deletedCells = new List<(Sheet Sheet, uint Row, uint Col)>();
            var restoredDimensions = new List<(Sheet Sheet, XlsxWorksheetDimensionBaseline Current)>(dimensionChanges.Count);
            var restoredMergedRegions = new List<(Sheet Sheet, GridRange[] Current)>(mergeRegionChanges.Count);
            var restoredHyperlinks = new List<(Sheet Sheet, XlsxWorksheetHyperlinkBaseline Current)>(hyperlinkChanges.Count);
            var restoredComments = new List<(Sheet Sheet, XlsxWorksheetCommentBaseline Current)>(commentChanges.Count);
            var restoredViews = new List<(Sheet Sheet, XlsxWorksheetViewBaseline Current)>(worksheetViewChanges.Count);
            try
            {
                foreach (var worksheetViewChange in worksheetViewChanges)
                {
                    var sheet = workbook.GetSheet(worksheetViewChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredViews.Add((sheet, XlsxWorksheetViewBaseline.Capture(sheet)));
                    worksheetViewChange.Original.ApplyTo(sheet);
                }

                foreach (var dimensionChange in dimensionChanges)
                {
                    var sheet = workbook.GetSheet(dimensionChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredDimensions.Add((sheet, XlsxWorksheetDimensionBaseline.Capture(sheet)));
                    ApplyDimensionBaseline(sheet, dimensionChange.Original);
                }

                foreach (var mergeRegionChange in mergeRegionChanges)
                {
                    var sheet = workbook.GetSheet(mergeRegionChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredMergedRegions.Add((sheet, sheet.MergedRegions.ToArray()));
                    sheet.ReplaceMergedRegions(mergeRegionChange.Original);
                }

                foreach (var hyperlinkChange in hyperlinkChanges)
                {
                    var sheet = workbook.GetSheet(hyperlinkChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredHyperlinks.Add((sheet, XlsxWorksheetHyperlinkBaseline.Capture(sheet)));
                    ApplyHyperlinkBaseline(sheet, hyperlinkChange.Original);
                }

                foreach (var commentChange in commentChanges)
                {
                    var sheet = workbook.GetSheet(commentChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredComments.Add((sheet, XlsxWorksheetCommentBaseline.Capture(sheet)));
                    ApplyCommentBaseline(sheet, commentChange.Original);
                }

                foreach (var change in changes)
                {
                    var sheet = workbook.GetSheet(change.SheetId);
                    if (sheet is null)
                        return false;

                    if (change.Kind == XlsxCellValuePatchKind.InsertedLiteralValue)
                    {
                        var insertedCell = sheet.GetCell(change.Row, change.Col);
                        if (insertedCell is null)
                            return false;

                        insertedCells.Add((sheet, change.Row, change.Col, insertedCell));
                        sheet.ClearCell(change.Row, change.Col);
                        if (change.ConsumesSourceStyleOnlyCell)
                            sheet.SetStyleOnly(change.Row, change.Col, change.OriginalStyleId);
                        continue;
                    }

                    if (change.Kind == XlsxCellValuePatchKind.DeletedCell)
                    {
                        if (sheet.GetCell(change.Row, change.Col) is not null)
                            return false;

                        var originalCell = new Cell
                        {
                            Value = change.OriginalValue,
                            FormulaText = change.OriginalFormulaText,
                            ArrayMode = change.OriginalArrayMode,
                            StyleId = change.OriginalStyleId,
                            IgnoreFormulaError = change.OriginalIgnoreFormulaError
                        };
                        sheet.SetCell(new CellAddress(sheet.Id, change.Row, change.Col), originalCell);
                        deletedCells.Add((sheet, change.Row, change.Col));
                        continue;
                    }

                    var changedCell = sheet.GetCell(change.Row, change.Col);
                    if (changedCell is null)
                        return false;

                    restoredCells.Add((
                        changedCell,
                        changedCell.Value,
                        changedCell.FormulaText,
                        changedCell.ArrayMode,
                        changedCell.StyleId,
                        changedCell.IgnoreFormulaError));
                    changedCell.Value = change.OriginalValue;
                    changedCell.FormulaText = change.OriginalFormulaText;
                    changedCell.ArrayMode = change.OriginalArrayMode;
                    changedCell.StyleId = change.OriginalStyleId;
                    changedCell.IgnoreFormulaError = change.OriginalIgnoreFormulaError;
                }

                return string.Equals(
                    CreatePatchValidationModelFingerprint(workbook),
                    _modelFingerprint,
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var (cell, currentValue, currentFormulaText, currentArrayMode, currentStyleId, currentIgnoreFormulaError) in restoredCells)
                {
                    cell.Value = currentValue;
                    cell.FormulaText = currentFormulaText;
                    cell.ArrayMode = currentArrayMode;
                    cell.StyleId = currentStyleId;
                    cell.IgnoreFormulaError = currentIgnoreFormulaError;
                }

                foreach (var (sheet, row, col, currentCell) in insertedCells)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), currentCell);

                foreach (var (sheet, row, col) in deletedCells)
                    sheet.ClearCell(row, col);

                foreach (var (sheet, current) in restoredDimensions)
                    ApplyDimensionBaseline(sheet, current);
                foreach (var (sheet, current) in restoredMergedRegions)
                    sheet.ReplaceMergedRegions(current);
                foreach (var (sheet, current) in restoredHyperlinks)
                    ApplyHyperlinkBaseline(sheet, current);
                foreach (var (sheet, current) in restoredComments)
                    ApplyCommentBaseline(sheet, current);
                foreach (var (sheet, current) in restoredViews)
                    current.ApplyTo(sheet);
            }
        }

        private static void ApplyDimensionBaseline(Sheet sheet, XlsxWorksheetDimensionBaseline baseline)
        {
            sheet.DefaultColumnWidth = baseline.DefaultColumnWidth;
            sheet.DefaultRowHeight = baseline.DefaultRowHeight;
            ReplaceDictionary(sheet.RowHeights, baseline.RowHeights);
            ReplaceDictionary(sheet.ColumnWidths, baseline.ColumnWidths);
            ReplaceSet(sheet.HiddenRows, baseline.HiddenRows);
            ReplaceSet(sheet.FilterHiddenRows, baseline.FilterHiddenRows);
            ReplaceSet(sheet.HiddenCols, baseline.HiddenCols);
            ReplaceDictionary(sheet.RowOutlineLevels, baseline.RowOutlineLevels);
            ReplaceDictionary(sheet.ColOutlineLevels, baseline.ColOutlineLevels);
            ReplaceSet(sheet.GroupHiddenRows, baseline.GroupHiddenRows);
            ReplaceSet(sheet.GroupHiddenCols, baseline.GroupHiddenCols);
            ReplaceSet(sheet.CollapsedAnchorRows, baseline.CollapsedAnchorRows);
            ReplaceSet(sheet.CollapsedAnchorCols, baseline.CollapsedAnchorCols);
            sheet.OutlineSummaryBelow = baseline.OutlineSummaryBelow;
            sheet.OutlineSummaryRight = baseline.OutlineSummaryRight;
            sheet.ShowOutlineSymbols = baseline.ShowOutlineSymbols;
            sheet.ApplyOutlineStyles = baseline.ApplyOutlineStyles;
        }

        private static void ApplyHyperlinkBaseline(Sheet sheet, XlsxWorksheetHyperlinkBaseline baseline)
        {
            sheet.Hyperlinks.Clear();
            sheet.HyperlinkMetadata.Clear();
            foreach (var (address, hyperlink) in baseline.Hyperlinks)
            {
                sheet.Hyperlinks[address] = hyperlink.Target;
                sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
                    hyperlink.LinkType,
                    hyperlink.ScreenTip,
                    hyperlink.Bookmark);
            }
        }

        private static void ApplyCommentBaseline(Sheet sheet, XlsxWorksheetCommentBaseline baseline)
        {
            sheet.Comments.Clear();
            foreach (var (address, text) in baseline.Comments)
                sheet.Comments[address] = text;
            sheet.CommentAuthors.Clear();
            foreach (var (address, author) in baseline.Authors)
                sheet.CommentAuthors[address] = author;
        }

        private static void ReplaceDictionary<TValue>(
            Dictionary<uint, TValue> target,
            IReadOnlyDictionary<uint, TValue> source)
        {
            target.Clear();
            foreach (var (key, value) in source)
                target[key] = value;
        }

        private static void ReplaceSet(HashSet<uint> target, IReadOnlySet<uint> source)
        {
            target.Clear();
            foreach (var value in source)
                target.Add(value);
        }

        private static bool IsPatchableScalarValue(ScalarValue value) =>
            value is BlankValue or NumberValue or BoolValue or TextValue or DateTimeValue or ErrorValue;

        /// <summary>
        /// Structural equality for a cell's rich-text run sequence, used by patch-save's change
        /// detection (R61-io-rich-text-runs-6-1) to notice a per-run formatting edit even when the
        /// cell's plain Value and resolved StyleId are unchanged. Null and an empty list are treated
        /// as equivalent ("no run overrides") since <see cref="Sheet.RichTextRuns"/> never stores an
        /// empty list for a live entry, but a baseline/current snapshot could legitimately hold
        /// either depending on how it was captured.
        /// </summary>
        private static bool RichRunsEqual(IReadOnlyList<CellTextRun>? a, IReadOnlyList<CellTextRun>? b)
        {
            var aCount = a?.Count ?? 0;
            var bCount = b?.Count ?? 0;
            if (aCount != bCount)
                return false;

            for (var i = 0; i < aCount; i++)
            {
                if (!a![i].Equals(b![i]))
                    return false;
            }

            return true;
        }

        private bool TryGetSourceStyleIndex(StyleId styleId, out string? sourceStyleIndex) =>
            _sourceStyleIndexesByStyleId.TryGetValue(styleId, out sourceStyleIndex);

        private static void AddSourceStyleIndex(
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds,
            StyleId styleId,
            string? sourceStyleIndex)
        {
            if (ambiguousStyleIds.Contains(styleId))
                return;

            if (!sourceStyleIndexesByStyleId.TryGetValue(styleId, out var existingSourceStyleIndex))
            {
                sourceStyleIndexesByStyleId[styleId] = sourceStyleIndex;
                return;
            }

            if (string.Equals(existingSourceStyleIndex, sourceStyleIndex, StringComparison.Ordinal))
                return;

            sourceStyleIndexesByStyleId.Remove(styleId);
            ambiguousStyleIds.Add(styleId);
        }

        private abstract class XlsxSourceCellStyleIndexLookup
        {
            public abstract bool TryGetValue(uint row, uint col, out string? sourceStyleIndex);
        }

        private sealed class XlsxExplicitSourceCellStyleIndexLookup : XlsxSourceCellStyleIndexLookup
        {
            private readonly IReadOnlyList<(uint Row, uint Col, int StyleIndex)> _sourceEntries;
            private readonly (uint Row, uint Col, int StyleIndex)[]? _sortedSourceEntries;
            private readonly Dictionary<int, string?> _sourceStyleIndexCache;

            public XlsxExplicitSourceCellStyleIndexLookup(
                IReadOnlyList<(uint Row, uint Col, int StyleIndex)> sourceEntries,
                Dictionary<int, string?> sourceStyleIndexCache)
            {
                _sourceEntries = sourceEntries;
                _sourceStyleIndexCache = sourceStyleIndexCache;
                if (!IsSorted(sourceEntries))
                {
                    _sortedSourceEntries = sourceEntries.ToArray();
                    Array.Sort(_sortedSourceEntries, CompareSourceStyleEntries);
                }
            }

            public override bool TryGetValue(uint row, uint col, out string? sourceStyleIndex)
            {
                var low = 0;
                var high = (_sortedSourceEntries?.Length ?? _sourceEntries.Count) - 1;
                while (low <= high)
                {
                    var mid = low + ((high - low) / 2);
                    var entry = _sortedSourceEntries is null ? _sourceEntries[mid] : _sortedSourceEntries[mid];
                    var rowCompare = entry.Row.CompareTo(row);
                    var compare = rowCompare != 0 ? rowCompare : entry.Col.CompareTo(col);
                    if (compare < 0)
                    {
                        low = mid + 1;
                        continue;
                    }

                    if (compare > 0)
                    {
                        high = mid - 1;
                        continue;
                    }

                    if (entry.StyleIndex < 0)
                    {
                        sourceStyleIndex = null;
                        return false;
                    }

                    sourceStyleIndex = GetCachedSourceStyleIndex(_sourceStyleIndexCache, entry.StyleIndex);
                    return true;
                }

                sourceStyleIndex = null;
                return false;
            }

            private static bool IsSorted(IReadOnlyList<(uint Row, uint Col, int StyleIndex)> entries)
            {
                if (entries.Count < 2)
                    return true;

                var previous = entries[0];
                for (var index = 1; index < entries.Count; index++)
                {
                    var current = entries[index];
                    if (CompareSourceStyleEntries(previous, current) > 0)
                        return false;

                    previous = current;
                }

                return true;
            }
        }

        private sealed class XlsxDictionarySourceCellStyleIndexLookup(
            Dictionary<(uint Row, uint Col), string?> sourceCellStyleIndexes)
            : XlsxSourceCellStyleIndexLookup
        {
            public override bool TryGetValue(uint row, uint col, out string? sourceStyleIndex) =>
                sourceCellStyleIndexes.TryGetValue((row, col), out sourceStyleIndex);
        }

        private static int CompareSourceStyleEntries(
            (uint Row, uint Col, int StyleIndex) left,
            (uint Row, uint Col, int StyleIndex) right)
        {
            var rowCompare = left.Row.CompareTo(right.Row);
            return rowCompare != 0
                ? rowCompare
                : left.Col.CompareTo(right.Col);
        }

        private sealed record XlsxSourceCellStyleInfo(
            XlsxSourceCellStyleIndexLookup PopulatedCells,
            XlsxSourceStyleOnlyCellCollection StyleOnlyCells);

        private static XlsxSourceCellStyleInfo? ReadSourceCellStyleIndexes(
            SheetXmlLayout layout,
            Sheet sheet,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
            => ReadSourceCellStyleIndexes(
                layout.ExplicitPopulatedCellStyles,
                layout.ExplicitStyleOnlyCells,
                sheet,
                sourceStyleIndexesByStyleId,
                ambiguousStyleIds);

        private static XlsxSourceCellStyleInfo? ReadSourceCellStyleIndexes(
            XlsxCellPatchBaselineSheetFacts sheetFacts,
            Sheet sheet,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
            => ReadSourceCellStyleIndexes(
                sheetFacts.ExplicitPopulatedCellStyles,
                sheetFacts.ExplicitStyleOnlyCells,
                sheet,
                sourceStyleIndexesByStyleId,
                ambiguousStyleIds);

        private static XlsxSourceCellStyleInfo? ReadSourceCellStyleIndexes(
            IReadOnlyList<(uint Row, uint Col, int StyleIndex)> explicitPopulatedCellStyles,
            IReadOnlyList<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
            Sheet sheet,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
        {
            var sourceStyleIndexCache = new Dictionary<int, string?>();
            foreach (var (row, col, styleIndex) in explicitPopulatedCellStyles)
            {
                if (styleIndex < 0)
                    continue;

                if (sheet.GetCell(row, col) is not { } cell)
                    continue;

                var sourceStyleIndex = GetCachedSourceStyleIndex(sourceStyleIndexCache, styleIndex);
                AddSourceStyleIndex(
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds,
                    cell.StyleId,
                    sourceStyleIndex);
            }

            return new XlsxSourceCellStyleInfo(
                new XlsxExplicitSourceCellStyleIndexLookup(
                    explicitPopulatedCellStyles,
                    sourceStyleIndexCache),
                ReadSourceStyleOnlyCells(
                    explicitStyleOnlyCells,
                    sheet,
                    sourceStyleIndexCache,
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds));
        }

        private static string? GetCachedSourceStyleIndex(Dictionary<int, string?> cache, int styleIndex)
        {
            if (cache.TryGetValue(styleIndex, out var sourceStyleIndex))
                return sourceStyleIndex;

            sourceStyleIndex = NormalizeSourceStyleIndex(styleIndex);
            cache[styleIndex] = sourceStyleIndex;
            return sourceStyleIndex;
        }

        private static XlsxSourceStyleOnlyCellCollection ReadSourceStyleOnlyCells(
            IReadOnlyList<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
            Sheet sheet,
            Dictionary<int, string?> sourceStyleIndexCache,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
        {
            if (sheet.TryGetCompressedStyleOnlyRuns(out var runs) && runs.Count > 0)
            {
                return ReadCompressedSourceStyleOnlyCells(
                    explicitStyleOnlyCells,
                    runs,
                    sourceStyleIndexCache,
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds);
            }

            List<XlsxSourceStyleOnlyCellEntry>? result = null;
            foreach (var (row, col, styleIndex) in explicitStyleOnlyCells)
            {
                if (styleIndex < 0 ||
                    sheet.GetStyleOnly(row, col) is not { } styleOnlyStyleId)
                {
                    continue;
                }

                result ??= [];
                var sourceStyleIndex = GetCachedSourceStyleIndex(sourceStyleIndexCache, styleIndex);
                AddSourceStyleIndex(
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds,
                    styleOnlyStyleId,
                    sourceStyleIndex);
                result.Add(new XlsxSourceStyleOnlyCellEntry(
                    row,
                    col,
                    styleOnlyStyleId,
                    sourceStyleIndex));
            }

            return XlsxSourceStyleOnlyCellCollection.FromCells(SortSourceStyleOnlyCells(result));
        }

        private static XlsxSourceStyleOnlyCellCollection ReadCompressedSourceStyleOnlyCells(
            IReadOnlyList<(uint Row, uint Col, int StyleIndex)> explicitStyleOnlyCells,
            IReadOnlyList<StyleOnlyRun> runs,
            Dictionary<int, string?> sourceStyleIndexCache,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
        {
            List<XlsxSourceStyleOnlyRunEntry>? result = null;
            var runIndex = 0;
            foreach (var (row, col, styleIndex) in explicitStyleOnlyCells)
            {
                if (styleIndex < 0)
                    continue;

                while (runIndex < runs.Count && StyleOnlyRunIsBeforeCell(runs[runIndex], row, col))
                    runIndex++;

                if (runIndex >= runs.Count)
                    break;

                var run = runs[runIndex];
                if (!StyleOnlyRunContainsCell(run, row, col))
                    continue;

                var sourceStyleIndex = GetCachedSourceStyleIndex(sourceStyleIndexCache, styleIndex);
                AddSourceStyleIndex(
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds,
                    run.StyleId,
                    sourceStyleIndex);
                AddCompressedSourceStyleOnlyCell(
                    ref result,
                    row,
                    col,
                    run.StyleId,
                    sourceStyleIndex);
            }

            return XlsxSourceStyleOnlyCellCollection.FromRuns(result is { Count: > 0 } ? result.ToArray() : []);
        }

        private static void AddCompressedSourceStyleOnlyCell(
            ref List<XlsxSourceStyleOnlyRunEntry>? result,
            uint row,
            uint col,
            StyleId styleId,
            string? sourceStyleIndex)
        {
            result ??= [];
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Row == row &&
                    last.StyleId == styleId &&
                    string.Equals(last.SourceStyleIndex, sourceStyleIndex, StringComparison.Ordinal) &&
                    last.EndCol != uint.MaxValue &&
                    col == last.EndCol + 1)
                {
                    result[^1] = last with { EndCol = col };
                    return;
                }
            }

            result.Add(new XlsxSourceStyleOnlyRunEntry(row, col, col, styleId, sourceStyleIndex));
        }

        private static bool StyleOnlyRunIsBeforeCell(StyleOnlyRun run, uint row, uint col) =>
            run.Row < row || run.Row == row && run.EndCol < col;

        private static bool StyleOnlyRunContainsCell(StyleOnlyRun run, uint row, uint col) =>
            run.Row == row && col >= run.StartCol && col <= run.EndCol;

        private static XlsxSourceStyleOnlyCellEntry[] SortSourceStyleOnlyCells(List<XlsxSourceStyleOnlyCellEntry>? entries)
        {
            if (entries is not { Count: > 0 })
                return [];

            var result = entries.ToArray();
            Array.Sort(result, XlsxSourceStyleOnlyCellEntry.Compare);
            return result;
        }

        private static XlsxSourceCellStyleInfo? ReadSourceCellStyleIndexes(
            ZipArchive archive,
            string worksheetPath,
            Sheet sheet,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
        {
            var entry = archive.GetEntry(worksheetPath);
            if (entry is null)
                return null;

            var result = new Dictionary<(uint Row, uint Col), string?>(sheet.CellCount);
            List<XlsxSourceStyleOnlyCellEntry>? styleOnlyCells = null;
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element ||
                    !string.Equals(reader.LocalName, "c", StringComparison.Ordinal))
                {
                    continue;
                }

                var rawStyleIndex = reader.GetAttribute("s");
                if (!TryNormalizeSourceStyleIndex(rawStyleIndex, out var sourceStyleIndex))
                    continue;

                var reference = reader.GetAttribute("r");
                if (!TryParseCellReference(reference, out var row, out var col))
                    continue;

                if (sheet.GetCell(row, col) is { } cell)
                {
                    result[(row, col)] = sourceStyleIndex;
                    AddSourceStyleIndex(
                        sourceStyleIndexesByStyleId,
                        ambiguousStyleIds,
                        cell.StyleId,
                        sourceStyleIndex);
                    continue;
                }

                if (sheet.GetStyleOnly(row, col) is { } styleOnlyStyleId)
                {
                    AddSourceStyleIndex(
                        sourceStyleIndexesByStyleId,
                        ambiguousStyleIds,
                        styleOnlyStyleId,
                        sourceStyleIndex);
                    styleOnlyCells ??= [];
                    styleOnlyCells.Add(new XlsxSourceStyleOnlyCellEntry(row, col, styleOnlyStyleId, sourceStyleIndex));
                }
            }

            return new XlsxSourceCellStyleInfo(
                new XlsxDictionarySourceCellStyleIndexLookup(result),
                XlsxSourceStyleOnlyCellCollection.FromCells(SortSourceStyleOnlyCells(styleOnlyCells)));
        }

        private static IReadOnlyDictionary<CellAddress, XlsxSourceHyperlink> ReadSourceHyperlinks(
            ZipArchive archive,
            string worksheetPath,
            Sheet sheet)
        {
            if (sheet.Hyperlinks.Count == 0)
                return new Dictionary<CellAddress, XlsxSourceHyperlink>();

            var entry = archive.GetEntry(worksheetPath);
            if (entry is null)
                return new Dictionary<CellAddress, XlsxSourceHyperlink>();

            try
            {
                var result = new Dictionary<CellAddress, XlsxSourceHyperlink>();
                var ambiguous = new HashSet<CellAddress>();
                XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
                if (reader.NodeType == XmlNodeType.None)
                    reader.Read();
                if (reader.NodeType != XmlNodeType.Element)
                    reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element)
                    return result;

                var worksheetNamespace = reader.NamespaceURI;
                var rootDepth = reader.Depth;
                var hyperlinksDepth = -1;
                if (reader.IsEmptyElement)
                    return result;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.Depth == hyperlinksDepth)
                            hyperlinksDepth = -1;

                        if (reader.Depth == rootDepth)
                            break;

                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element ||
                        !string.Equals(reader.NamespaceURI, worksheetNamespace, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (reader.Depth == rootDepth + 1 &&
                        string.Equals(reader.LocalName, "hyperlinks", StringComparison.Ordinal))
                    {
                        hyperlinksDepth = reader.IsEmptyElement ? -1 : reader.Depth;
                        continue;
                    }

                    if (hyperlinksDepth < 0 ||
                        reader.Depth != hyperlinksDepth + 1 ||
                        !string.Equals(reader.LocalName, "hyperlink", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var reference = reader.GetAttribute("ref");
                    if (!TryParseSingleCellReference(reference, sheet.Id, out var address) ||
                        ambiguous.Contains(address))
                    {
                        continue;
                    }

                    var source = new XlsxSourceHyperlink(
                        address,
                        reference!,
                        reader.GetAttribute("id", relNs.NamespaceName) is not null,
                        reader.GetAttribute("location"),
                        reader.GetAttribute("tooltip"));
                    if (!result.TryAdd(address, source))
                    {
                        result.Remove(address);
                        ambiguous.Add(address);
                    }
                }

                return result;
            }
            catch
            {
                return new Dictionary<CellAddress, XlsxSourceHyperlink>();
            }
        }

        private static IReadOnlyDictionary<CellAddress, XlsxSourceComment> ReadSourceComments(
            ZipArchive archive,
            string worksheetPath,
            Sheet sheet)
        {
            var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
            var relationshipsEntry = archive.GetEntry(relationshipsPath);
            if (relationshipsEntry is null || sheet.Comments.Count == 0)
                return new Dictionary<CellAddress, XlsxSourceComment>();

            try
            {
                XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
                var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
                var commentPartPaths = relationshipsXml.Root?
                    .Elements(packageRelNs + "Relationship")
                    .Where(element =>
                        string.Equals(
                            element.Attribute("Type")?.Value,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments",
                            StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(element.Attribute("Target")?.Value))
                    .Select(element => XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, element.Attribute("Target")!.Value))
                    .Where(path => archive.GetEntry(path) is not null)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? [];
                if (commentPartPaths.Count != 1)
                    return new Dictionary<CellAddress, XlsxSourceComment>();

                var commentPartPath = commentPartPaths[0];
                var commentEntry = archive.GetEntry(commentPartPath);
                if (commentEntry is null)
                    return new Dictionary<CellAddress, XlsxSourceComment>();

                var commentsXml = XlsxPackageXmlEditor.LoadXml(commentEntry);
                var root = commentsXml.Root;
                if (root is null)
                    return new Dictionary<CellAddress, XlsxSourceComment>();

                var worksheetNs = root.Name.Namespace;
                var commentList = root.Element(worksheetNs + "commentList");
                if (commentList is null)
                    return new Dictionary<CellAddress, XlsxSourceComment>();

                var result = new Dictionary<CellAddress, XlsxSourceComment>();
                var ambiguous = new HashSet<CellAddress>();
                foreach (var comment in commentList.Elements(worksheetNs + "comment"))
                {
                    var reference = comment.Attribute("ref")?.Value;
                    if (!TryParseSingleCellReference(reference, sheet.Id, out var address) ||
                        ambiguous.Contains(address) ||
                        !sheet.Comments.TryGetValue(address, out var modelText) ||
                        !TryGetPatchableCommentTextElement(comment, worksheetNs, out var textElement))
                    {
                        continue;
                    }

                    var source = new XlsxSourceComment(
                        address,
                        commentPartPath,
                        reference!,
                        textElement.Value);
                    if (!string.Equals(source.Text, modelText, StringComparison.Ordinal))
                        continue;

                    if (result.TryAdd(address, source))
                        continue;

                    result.Remove(address);
                    ambiguous.Add(address);
                }

                return result;
            }
            catch
            {
                return new Dictionary<CellAddress, XlsxSourceComment>();
            }
        }

        private static string? NormalizeSourceStyleIndex(int sourceStyleIndex) =>
            sourceStyleIndex <= 0
                ? null
                : sourceStyleIndex.ToString(CultureInfo.InvariantCulture);

        private static bool TryNormalizeSourceStyleIndex(string? rawStyleIndex, out string? sourceStyleIndex)
        {
            sourceStyleIndex = null;
            if (string.IsNullOrWhiteSpace(rawStyleIndex))
                return false;

            var span = rawStyleIndex.AsSpan().Trim();
            if (!uint.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return false;

            if (parsed == 0)
                return true;

            sourceStyleIndex = parsed.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Returns true when any <c>&lt;row&gt;</c> element in <paramref name="sheetData"/> omits the
        /// optional <c>r</c> attribute.  Such r-less rows are schema-valid; their position is implied by
        /// document order.  Patch-save cannot reliably match or insert into them, so callers should fall
        /// back to a full ClosedXML save when this returns true.
        /// </summary>
        private static bool SheetDataHasRLessRows(XElement sheetData, XNamespace worksheetNs)
        {
            var rowName = worksheetNs + "row";
            foreach (var rowElement in sheetData.Elements(rowName))
            {
                if (rowElement.Attribute("r") is null)
                    return true;
            }

            return false;
        }

        private static XElement? FindCell(XElement sheetData, XNamespace worksheetNs, uint row, uint col)
        {
            var rowName = worksheetNs + "row";
            var cellName = worksheetNs + "c";
            var reference = ToReference(row, col);
            foreach (var rowElement in sheetData.Elements(rowName))
            {
                if (!uint.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber) ||
                    rowNumber != row)
                {
                    continue;
                }

                return FindCellByReference(rowElement, cellName, reference);
            }

            return null;
        }

        private static XElement? FindCellByReference(XElement rowElement, XName cellName, string reference)
        {
            foreach (var cell in rowElement.Elements(cellName))
            {
                if (CellReferenceMatches(cell, reference))
                    return cell;
            }

            return null;
        }

        private static bool CellReferenceMatches(XElement cell, string reference) =>
            string.Equals(cell.Attribute("r")?.Value, reference, StringComparison.OrdinalIgnoreCase);

        /// <returns>
        /// <c>true</c> if <paramref name="cell"/> was a shared-string reference (t="s") before this
        /// call -- i.e. exactly one reference to xl/sharedStrings.xml's shared-string table was just
        /// removed, since every branch below unconditionally replaces the cell's t attribute/value with
        /// a non-shared-string representation (or clears both entirely for a blank). Callers that patch
        /// xl/sharedStrings.xml's stale "count" total (see R52-io-sst-shared-inline-3-1) use this.
        /// </returns>
        private static bool RewriteLiteralCellValue(
            XElement cell,
            XNamespace worksheetNs,
            ScalarValue value,
            IReadOnlyList<CellTextRun>? richRuns = null,
            CellPhoneticGuide? phoneticGuide = null)
        {
            var wasSharedStringReference = string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal);

            // A rich value (linked data type, IMAGE()-produced value, etc.) is stored for backward
            // compatibility as a t="e" (#VALUE!) placeholder cell whose vm/cm attribute indexes into
            // xl/metadata.xml's valueMetadata/cellMetadata to resolve the real rich-value content. If the
            // user overwrites that cell's literal value, the vm/cm index would otherwise keep pointing at
            // now-unrelated rich-value metadata, so clear it here. Other metadataTypes (e.g. XLDAPR dynamic
            // array spill markers) are not represented with a t="e" placeholder and are intentionally left
            // untouched by this narrower check.
            var wasRichValuePlaceholder =
                string.Equals(cell.Attribute("t")?.Value, "e", StringComparison.Ordinal) &&
                (cell.Attribute("vm") is not null || cell.Attribute("cm") is not null);

            cell.Element(worksheetNs + "f")?.Remove();
            cell.Element(worksheetNs + "v")?.Remove();
            cell.Element(worksheetNs + "is")?.Remove();

            if (wasRichValuePlaceholder)
            {
                cell.Attribute("vm")?.Remove();
                cell.Attribute("cm")?.Remove();
            }

            switch (value)
            {
                case BlankValue:
                    cell.Attribute("t")?.Remove();
                    break;
                case TextValue text:
                    cell.SetAttributeValue("t", "inlineStr");
                    AddCellValueElement(cell, worksheetNs, richRuns is { Count: > 0 }
                        ? CreateRichInlineStringElement(worksheetNs, richRuns, phoneticGuide)
                        : new XElement(
                            worksheetNs + "is",
                            CreateInlineTextElement(worksheetNs, text.Value)));
                    break;
                case BoolValue boolean:
                    cell.SetAttributeValue("t", "b");
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", boolean.Value ? "1" : "0"));
                    break;
                case ErrorValue error:
                    cell.SetAttributeValue("t", "e");
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", error.Code));
                    break;
                case DateTimeValue dateTime:
                    cell.Attribute("t")?.Remove();
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", FormatNumber(dateTime.Value)));
                    break;
                case NumberValue number:
                    cell.Attribute("t")?.Remove();
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", FormatNumber(number.Value)));
                    break;
            }

            return wasSharedStringReference;
        }

        private static bool RewriteFormulaCachedCellValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
        {
            if (cell.Element(worksheetNs + "f") is null)
                return false;

            RewriteFormulaCachedValue(cell, worksheetNs, value);
            return true;
        }

        private static bool RewriteFormulaTextAndCachedCellValue(
            XElement cell,
            XNamespace worksheetNs,
            string? formulaText,
            ScalarValue value)
        {
            var formula = cell.Element(worksheetNs + "f");
            if (formula is null ||
                formula.HasAttributes ||
                string.IsNullOrWhiteSpace(formulaText))
            {
                return false;
            }

            formula.Value = XlsxClosedXmlCellMapper.NormalizeFormulaText(formulaText);
            RewriteFormulaCachedValue(cell, worksheetNs, value);
            return true;
        }

        private static void RewriteFormulaCachedValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
        {
            cell.Element(worksheetNs + "v")?.Remove();
            cell.Element(worksheetNs + "is")?.Remove();

            // A rich value (linked data type, IMAGE()-produced value, etc.) propagated onto a formula
            // cell is stored as a vm attribute (optionally paired with a cm attribute for the same
            // binding) indexing into xl/metadata.xml's valueMetadata/cellMetadata, describing exactly
            // the cached <t>/<v> this rewrite is about to replace. This helper is only ever invoked
            // (via RewriteFormulaCachedCellValue / RewriteFormulaTextAndCachedCellValue) when the
            // formula's cached value -- or the formula text itself -- has actually changed, so any
            // existing vm is now stale and must be dropped to avoid the fast patch-save path silently
            // reattaching mismatched rich-value metadata to the cell's new value. Mirrors the
            // RewriteLiteralCellValue guard above and the full-save CellValueMatchesCapturedNativeMetadata
            // guard in XlsxWorksheetMetadataPreserver.CellMetadata.cs.
            //
            // R82-io-cell-rich-metadata-5-2: cm alone (no vm) is a DIFFERENT metadataType -- most
            // commonly an XLDAPR dynamic-array marker, which describes the FORMULA's nature (it
            // spills) rather than its cached value, so it stays valid across an ordinary
            // recalculation and must not be stripped just because the cached value changed. Only drop
            // cm when it accompanies vm, i.e. when it is genuinely part of the same value-dependent
            // rich-value binding being invalidated here.
            var hadValueMetadataIndex = cell.Attribute("vm") is not null;
            cell.Attribute("vm")?.Remove();
            if (hadValueMetadataIndex)
                cell.Attribute("cm")?.Remove();

            switch (value)
            {
                case BlankValue:
                    cell.Attribute("t")?.Remove();
                    break;
                case TextValue text:
                    cell.SetAttributeValue("t", "str");
                    var textValueElement = new XElement(worksheetNs + "v", XlsxXmlTextEscaper.EscapeForXml(text.Value));
                    if (text.Value.Length > 0 &&
                        (char.IsWhiteSpace(text.Value[0]) || char.IsWhiteSpace(text.Value[^1])))
                    {
                        textValueElement.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                    }
                    AddCellValueElement(cell, worksheetNs, textValueElement);
                    break;
                case BoolValue boolean:
                    cell.SetAttributeValue("t", "b");
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", boolean.Value ? "1" : "0"));
                    break;
                case ErrorValue error:
                    cell.SetAttributeValue("t", "e");
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", error.Code));
                    break;
                case DateTimeValue dateTime:
                    cell.Attribute("t")?.Remove();
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", FormatNumber(dateTime.Value)));
                    break;
                case NumberValue number:
                    cell.Attribute("t")?.Remove();
                    AddCellValueElement(cell, worksheetNs, new XElement(worksheetNs + "v", FormatNumber(number.Value)));
                    break;
            }
        }

        private static void AddCellValueElement(XElement cell, XNamespace worksheetNs, XElement valueElement)
        {
            var extensionList = cell.Element(worksheetNs + "extLst");
            if (extensionList is null)
                cell.Add(valueElement);
            else
                extensionList.AddBeforeSelf(valueElement);
        }

        private static bool InsertLiteralCell(
            XElement sheetData,
            XNamespace worksheetNs,
            uint row,
            uint col,
            ScalarValue value,
            string? sourceStyleIndex,
            IReadOnlyList<CellTextRun>? richRuns = null,
            CellPhoneticGuide? phoneticGuide = null)
        {
            var rowElement = FindOrCreateRow(sheetData, worksheetNs, row);
            if (rowElement is null)
                return false;

            var cellElement = new XElement(worksheetNs + "c", new XAttribute("r", ToReference(row, col)));
            ApplyCellStyle(cellElement, sourceStyleIndex);
            RewriteLiteralCellValue(cellElement, worksheetNs, value, richRuns, phoneticGuide);
            InsertCellInColumnOrder(rowElement, worksheetNs, cellElement, col);
            return true;
        }

        private static bool RewriteStyleOnlyCellAsLiteral(
            XElement cell,
            XNamespace worksheetNs,
            ScalarValue value,
            string? sourceStyleIndex,
            IReadOnlyList<CellTextRun>? richRuns = null,
            CellPhoneticGuide? phoneticGuide = null)
        {
            if (cell.Elements().Any(child => child.Name != worksheetNs + "extLst"))
                return false;

            ApplyCellStyle(cell, sourceStyleIndex);
            RewriteLiteralCellValue(cell, worksheetNs, value, richRuns, phoneticGuide);
            return true;
        }

        private static void ApplyCellStyle(XElement cell, string? sourceStyleIndex)
        {
            if (string.IsNullOrEmpty(sourceStyleIndex))
                cell.Attribute("s")?.Remove();
            else
                cell.SetAttributeValue("s", sourceStyleIndex);
        }

        private static XElement? FindOrCreateRow(XElement sheetData, XNamespace worksheetNs, uint row)
        {
            var rowName = worksheetNs + "row";
            XElement? insertBefore = null;
            // Scan the whole row collection rather than breaking on the first row number greater
            // than the target: <row r="..."> elements are not guaranteed to appear in ascending
            // document order (schema-valid but non-Excel-authored sources can emit them out of
            // order). Breaking early would miss the true match further in the document and
            // fabricate a duplicate <row> for the same r value. Remember only the first
            // greater-numbered row seen, so the insertion point still matches the common
            // ascending-order case exactly.
            foreach (var rowElement in sheetData.Elements(rowName))
            {
                if (!uint.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
                    continue;

                if (rowNumber == row)
                    return rowElement;

                if (rowNumber > row && insertBefore is null)
                    insertBefore = rowElement;
            }

            var created = new XElement(rowName, new XAttribute("r", row.ToString(CultureInfo.InvariantCulture)));
            if (insertBefore is null)
                sheetData.Add(created);
            else
                insertBefore.AddBeforeSelf(created);

            return created;
        }

        private static void InsertCellInColumnOrder(
            XElement rowElement,
            XNamespace worksheetNs,
            XElement cellElement,
            uint col)
        {
            var cellName = worksheetNs + "c";
            foreach (var existingCell in rowElement.Elements(cellName))
            {
                if (TryGetCellColumn(existingCell.Attribute("r")?.Value, out var existingCol) &&
                    existingCol > col)
                {
                    existingCell.AddBeforeSelf(cellElement);
                    return;
                }
            }

            var extensionList = rowElement.Element(worksheetNs + "extLst");
            if (extensionList is null)
                rowElement.Add(cellElement);
            else
                extensionList.AddBeforeSelf(cellElement);
        }

        private static void UpdateDimension(
            XElement sheetData,
            XElement worksheetRoot,
            XNamespace worksheetNs)
        {
            var dimension = worksheetRoot.Element(worksheetNs + "dimension");
            if (dimension is null)
                return;

            uint minRow = uint.MaxValue;
            uint minCol = uint.MaxValue;
            uint maxRow = 0;
            uint maxCol = 0;
            foreach (var cell in sheetData.Descendants(worksheetNs + "c"))
            {
                if (!TryParseCellReference(cell.Attribute("r")?.Value, out var row, out var col))
                    continue;

                minRow = Math.Min(minRow, row);
                minCol = Math.Min(minCol, col);
                maxRow = Math.Max(maxRow, row);
                maxCol = Math.Max(maxCol, col);
            }

            if (maxRow == 0 || maxCol == 0)
            {
                // No cells remain in the sheet (e.g. the last cell(s) were just cleared by this
                // patch). Real Excel recomputes the used range on save and writes dimension
                // ref="A1" for a sheet with no cells, rather than leaving a stale far-from-A1
                // reference pointing at now-empty cells.
                dimension.SetAttributeValue("ref", "A1");
                return;
            }

            var start = ToReference(minRow, minCol);
            var end = ToReference(maxRow, maxCol);
            dimension.SetAttributeValue("ref", start == end ? start : $"{start}:{end}");
        }

        private static bool TryParseCellReference(string? reference, out uint row, out uint col)
        {
            row = 0;
            col = 0;
            if (string.IsNullOrWhiteSpace(reference))
                return false;

            if (!CellAddress.TryParse(reference, default, out var address))
                return false;

            row = address.Row;
            col = address.Col;
            return true;
        }

        private static bool TryParseSingleCellReference(
            string? reference,
            SheetId sheetId,
            out CellAddress address)
        {
            address = default;
            if (string.IsNullOrWhiteSpace(reference) ||
                reference.Contains(':', StringComparison.Ordinal) ||
                !TryParseCellReference(reference, out var row, out var col) ||
                !IsValidWorksheetRow(row) ||
                !IsValidWorksheetColumn(col))
            {
                return false;
            }

            address = new CellAddress(sheetId, row, col);
            return true;
        }

        private static bool TryGetCellColumn(string? reference, out uint col)
        {
            col = 0;
            if (!TryParseCellReference(reference, out _, out col))
                return false;

            return true;
        }

        private static string FormatNumber(double value) =>
            XlsxNumberFormatting.ToXmlString(value);

        private static XElement CreateInlineTextElement(XNamespace worksheetNs, string value)
        {
            var escaped = XlsxXmlTextEscaper.EscapeForXml(value);
            var text = new XElement(worksheetNs + "t", escaped);
            if (value.Length > 0 &&
                (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            {
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }

            return text;
        }

        /// <summary>
        /// Builds an OOXML <c>&lt;is&gt;</c> element with one <c>&lt;r&gt;</c> child per rich-text run.
        /// Null properties on a run mean "inherit from cell style"; they are omitted from <c>&lt;rPr&gt;</c>.
        /// </summary>
        private static XElement CreateRichInlineStringElement(
            XNamespace worksheetNs,
            IReadOnlyList<CellTextRun> runs,
            CellPhoneticGuide? phoneticGuide = null)
            => XlsxRichRunWriter.CreateRichInlineStringElement(worksheetNs, runs, phoneticGuide);

        private static string ToReference(uint row, uint col)
        {
            var columnName = CellAddress.NumberToColumnName(col);
            return string.Create(
                columnName.Length + GetRowDigitCount(row),
                (ColumnName: columnName, Row: row),
                static (destination, state) =>
                {
                    state.ColumnName.AsSpan().CopyTo(destination);
                    state.Row.TryFormat(destination[state.ColumnName.Length..], out _, provider: CultureInfo.InvariantCulture);
                });
        }

        private static int GetRowDigitCount(uint row) =>
            row < 10 ? 1 :
            row < 100 ? 2 :
            row < 1_000 ? 3 :
            row < 10_000 ? 4 :
            row < 100_000 ? 5 :
            row < 1_000_000 ? 6 : 7;
    }

    private sealed record XlsxWorksheetDimensionBaseline(
        double DefaultColumnWidth,
        double DefaultRowHeight,
        IReadOnlyDictionary<uint, double> RowHeights,
        IReadOnlyDictionary<uint, double> ColumnWidths,
        IReadOnlySet<uint> HiddenRows,
        IReadOnlySet<uint> FilterHiddenRows,
        IReadOnlySet<uint> HiddenCols,
        IReadOnlyDictionary<uint, int> RowOutlineLevels,
        IReadOnlyDictionary<uint, int> ColOutlineLevels,
        IReadOnlySet<uint> GroupHiddenRows,
        IReadOnlySet<uint> GroupHiddenCols,
        IReadOnlySet<uint> CollapsedAnchorRows,
        IReadOnlySet<uint> CollapsedAnchorCols,
        bool? OutlineSummaryBelow,
        bool? OutlineSummaryRight,
        bool? ShowOutlineSymbols,
        bool? ApplyOutlineStyles)
    {
        public static XlsxWorksheetDimensionBaseline Capture(Sheet sheet) => new(
            sheet.DefaultColumnWidth,
            sheet.DefaultRowHeight,
            CopyDictionary(sheet.RowHeights),
            CopyDictionary(sheet.ColumnWidths),
            CopySet(sheet.HiddenRows),
            CopySet(sheet.FilterHiddenRows),
            CopySet(sheet.HiddenCols),
            CopyDictionary(sheet.RowOutlineLevels),
            CopyDictionary(sheet.ColOutlineLevels),
            CopySet(sheet.GroupHiddenRows),
            CopySet(sheet.GroupHiddenCols),
            CopySet(sheet.CollapsedAnchorRows),
            CopySet(sheet.CollapsedAnchorCols),
            sheet.OutlineSummaryBelow,
            sheet.OutlineSummaryRight,
            sheet.ShowOutlineSymbols,
            sheet.ApplyOutlineStyles);

        public bool UnsupportedFieldsMatch(XlsxWorksheetDimensionBaseline current) =>
            DefaultColumnWidth.Equals(current.DefaultColumnWidth) &&
            DefaultRowHeight.Equals(current.DefaultRowHeight) &&
            SetEquals(FilterHiddenRows, current.FilterHiddenRows) &&
            DictionaryEquals(RowOutlineLevels, current.RowOutlineLevels) &&
            DictionaryEquals(ColOutlineLevels, current.ColOutlineLevels) &&
            SetEquals(GroupHiddenRows, current.GroupHiddenRows) &&
            SetEquals(GroupHiddenCols, current.GroupHiddenCols) &&
            SetEquals(CollapsedAnchorRows, current.CollapsedAnchorRows) &&
            SetEquals(CollapsedAnchorCols, current.CollapsedAnchorCols) &&
            OutlineSummaryBelow == current.OutlineSummaryBelow &&
            OutlineSummaryRight == current.OutlineSummaryRight &&
            ShowOutlineSymbols == current.ShowOutlineSymbols &&
            ApplyOutlineStyles == current.ApplyOutlineStyles;

        private static Dictionary<uint, TValue> CopyDictionary<TValue>(IReadOnlyDictionary<uint, TValue> source) =>
            new(source);

        private static HashSet<uint> CopySet(IEnumerable<uint> source) => [.. source];

        private static bool DictionaryEquals<TValue>(
            IReadOnlyDictionary<uint, TValue> left,
            IReadOnlyDictionary<uint, TValue> right)
            where TValue : IEquatable<TValue>
        {
            if (left.Count != right.Count)
                return false;

            foreach (var (key, value) in left)
            {
                if (!right.TryGetValue(key, out var other) || !value.Equals(other))
                    return false;
            }

            return true;
        }

        private static bool SetEquals(IReadOnlySet<uint> left, IReadOnlySet<uint> right) =>
            left.Count == right.Count && left.SetEquals(right);
    }

    private sealed record XlsxWorksheetDimensionPatch(
        SheetId SheetId,
        string WorksheetPath,
        XlsxWorksheetDimensionBaseline Original,
        XlsxWorksheetDimensionBaseline Current,
        IReadOnlyList<uint> ChangedRows,
        IReadOnlyList<uint> ChangedColumns)
    {
        public int ChangeCount => ChangedRows.Count + ChangedColumns.Count;

        public static bool TryCreate(
            SheetId sheetId,
            string worksheetPath,
            XlsxWorksheetDimensionBaseline original,
            XlsxWorksheetDimensionBaseline current,
            out XlsxWorksheetDimensionPatch? patch)
        {
            patch = null;
            if (!original.UnsupportedFieldsMatch(current) ||
                !HasValidRowHeights(current.RowHeights) ||
                !HasValidColumnWidths(current.ColumnWidths) ||
                !HasValidRows(current.HiddenRows) ||
                !HasValidColumns(current.HiddenCols))
            {
                return false;
            }

            var changedRows = GetChangedRows(original, current);
            var changedColumns = GetChangedColumns(original, current);
            if (changedRows.Count == 0 && changedColumns.Count == 0)
                return true;

            patch = new XlsxWorksheetDimensionPatch(
                sheetId,
                worksheetPath,
                original,
                current,
                changedRows,
                changedColumns);
            return true;
        }

        private static List<uint> GetChangedRows(
            XlsxWorksheetDimensionBaseline original,
            XlsxWorksheetDimensionBaseline current)
        {
            var rows = original.RowHeights.Keys
                .Concat(current.RowHeights.Keys)
                .Concat(original.HiddenRows)
                .Concat(current.HiddenRows)
                .Where(IsValidWorksheetRow)
                .Distinct()
                .OrderBy(row => row)
                .ToList();

            rows.RemoveAll(row =>
                TryGetFinitePositive(original.RowHeights, row, out var originalHeight) ==
                TryGetFinitePositive(current.RowHeights, row, out var currentHeight) &&
                originalHeight.Equals(currentHeight) &&
                original.HiddenRows.Contains(row) == current.HiddenRows.Contains(row));
            return rows;
        }

        private static List<uint> GetChangedColumns(
            XlsxWorksheetDimensionBaseline original,
            XlsxWorksheetDimensionBaseline current)
        {
            var columns = original.ColumnWidths.Keys
                .Concat(current.ColumnWidths.Keys)
                .Concat(original.HiddenCols)
                .Concat(current.HiddenCols)
                .Where(IsValidWorksheetColumn)
                .Distinct()
                .OrderBy(column => column)
                .ToList();

            columns.RemoveAll(column =>
                TryGetFinitePositive(original.ColumnWidths, column, out var originalWidth) ==
                TryGetFinitePositive(current.ColumnWidths, column, out var currentWidth) &&
                originalWidth.Equals(currentWidth) &&
                original.HiddenCols.Contains(column) == current.HiddenCols.Contains(column));
            return columns;
        }

        private static bool TryGetFinitePositive(
            IReadOnlyDictionary<uint, double> values,
            uint key,
            out double value)
        {
            if (values.TryGetValue(key, out value) &&
                double.IsFinite(value) &&
                value > 0)
            {
                return true;
            }

            value = 0;
            return false;
        }

        private static bool HasValidRowHeights(IReadOnlyDictionary<uint, double> rowHeights) =>
            rowHeights.All(pair => IsValidWorksheetRow(pair.Key) && double.IsFinite(pair.Value) && pair.Value > 0);

        private static bool HasValidColumnWidths(IReadOnlyDictionary<uint, double> columnWidths) =>
            columnWidths.All(pair => IsValidWorksheetColumn(pair.Key) && double.IsFinite(pair.Value) && pair.Value > 0);

        private static bool HasValidRows(IReadOnlySet<uint> rows) =>
            rows.All(IsValidWorksheetRow);

        private static bool HasValidColumns(IReadOnlySet<uint> columns) =>
            columns.All(IsValidWorksheetColumn);
    }

    private sealed record XlsxWorksheetMergeRegionPatch(
        SheetId SheetId,
        string WorksheetPath,
        IReadOnlyList<GridRange> Original,
        IReadOnlyList<GridRange> Current,
        int ChangeCount)
    {
        public static bool TryCreate(
            SheetId sheetId,
            string worksheetPath,
            IReadOnlyList<GridRange> original,
            IReadOnlyList<GridRange> current,
            out XlsxWorksheetMergeRegionPatch? patch)
        {
            patch = null;
            if (!ArePatchable(sheetId, original) || !ArePatchable(sheetId, current))
                return false;

            if (SequenceEqual(original, current))
                return true;

            patch = new XlsxWorksheetMergeRegionPatch(
                sheetId,
                worksheetPath,
                original,
                current.ToArray(),
                CountChangedReferences(original, current));
            return true;
        }

        public static bool ArePatchable(SheetId sheetId, IReadOnlyList<GridRange> regions)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in regions)
            {
                if (region.Start.Sheet != sheetId ||
                    region.End.Sheet != sheetId ||
                    region.CellCount <= 1 ||
                    !IsValidWorksheetRow(region.Start.Row) ||
                    !IsValidWorksheetRow(region.End.Row) ||
                    !IsValidWorksheetColumn(region.Start.Col) ||
                    !IsValidWorksheetColumn(region.End.Col) ||
                    !seen.Add($"{region.Start.Row}:{region.Start.Col}:{region.End.Row}:{region.End.Col}"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SequenceEqual(
            IReadOnlyList<GridRange> left,
            IReadOnlyList<GridRange> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        private static int CountChangedReferences(
            IReadOnlyList<GridRange> original,
            IReadOnlyList<GridRange> current)
        {
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var region in original)
                references.Add(ToChangeKey(region));

            var changed = 0;
            foreach (var region in current)
            {
                if (references.Remove(ToChangeKey(region)))
                    continue;

                changed++;
            }

            changed += references.Count;
            return Math.Max(changed, 1);
        }

        private static string ToChangeKey(GridRange region) =>
            $"{region.Start.Row}:{region.Start.Col}:{region.End.Row}:{region.End.Col}";
    }

    private sealed record XlsxWorksheetHyperlinkBaseline(
        IReadOnlyDictionary<CellAddress, XlsxPatchHyperlink> Hyperlinks)
    {
        public static XlsxWorksheetHyperlinkBaseline Capture(Sheet sheet)
        {
            var hyperlinks = new Dictionary<CellAddress, XlsxPatchHyperlink>(sheet.Hyperlinks.Count);
            foreach (var (address, target) in sheet.Hyperlinks)
            {
                sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
                metadata ??= new HyperlinkMetadata();
                hyperlinks[address] = new XlsxPatchHyperlink(
                    target,
                    metadata.LinkType,
                    metadata.ScreenTip,
                    metadata.Bookmark);
            }

            return new XlsxWorksheetHyperlinkBaseline(hyperlinks);
        }

        public bool EqualsModel(XlsxWorksheetHyperlinkBaseline current)
        {
            if (Hyperlinks.Count != current.Hyperlinks.Count)
                return false;

            foreach (var (address, hyperlink) in Hyperlinks)
            {
                if (!current.Hyperlinks.TryGetValue(address, out var currentHyperlink) ||
                    hyperlink != currentHyperlink)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed record XlsxWorksheetHyperlinkPatch(
        SheetId SheetId,
        string WorksheetPath,
        XlsxWorksheetHyperlinkBaseline Original,
        XlsxWorksheetHyperlinkBaseline Current,
        IReadOnlyDictionary<CellAddress, XlsxSourceHyperlink> CurrentSource,
        IReadOnlyList<XlsxHyperlinkPatchChange> Changes)
    {
        public int ChangeCount => Changes.Count;

        public static bool TryCreate(
            SheetId sheetId,
            string worksheetPath,
            XlsxWorksheetHyperlinkBaseline original,
            IReadOnlyDictionary<CellAddress, XlsxSourceHyperlink> originalSource,
            XlsxWorksheetHyperlinkBaseline current,
            out XlsxWorksheetHyperlinkPatch? patch)
        {
            patch = null;
            if (original.EqualsModel(current))
                return true;

            if (original.Hyperlinks.Count != current.Hyperlinks.Count)
                return false;

            var changes = new List<XlsxHyperlinkPatchChange>();
            var currentSource = new Dictionary<CellAddress, XlsxSourceHyperlink>(originalSource);
            foreach (var (address, currentHyperlink) in current.Hyperlinks)
            {
                if (!original.Hyperlinks.TryGetValue(address, out var originalHyperlink))
                    return false;

                if (originalHyperlink == currentHyperlink)
                    continue;

                if (!originalSource.TryGetValue(address, out var source) ||
                    source.HasRelationshipId ||
                    originalHyperlink.LinkType != HyperlinkTargetKind.PlaceInThisDocument ||
                    !TryGetInternalLocation(currentHyperlink, out var newLocation))
                {
                    return false;
                }

                var newTooltip = string.IsNullOrWhiteSpace(currentHyperlink.ScreenTip)
                    ? null
                    : currentHyperlink.ScreenTip;
                changes.Add(new XlsxHyperlinkPatchChange(source.Reference, newLocation, newTooltip));
                currentSource[address] = source with
                {
                    Location = newLocation,
                    Tooltip = newTooltip
                };
            }

            if (changes.Count == 0)
                return true;

            patch = new XlsxWorksheetHyperlinkPatch(
                sheetId,
                worksheetPath,
                original,
                current,
                currentSource,
                changes);
            return true;
        }

        private static bool TryGetInternalLocation(XlsxPatchHyperlink hyperlink, out string location)
        {
            location = "";
            if (hyperlink.LinkType != HyperlinkTargetKind.PlaceInThisDocument)
                return false;

            location = string.IsNullOrWhiteSpace(hyperlink.Bookmark)
                ? hyperlink.Target
                : hyperlink.Bookmark;
            return !string.IsNullOrWhiteSpace(location);
        }
    }

    private sealed record XlsxPatchHyperlink(
        string Target,
        HyperlinkTargetKind LinkType,
        string ScreenTip,
        string Bookmark);

    private sealed record XlsxSourceHyperlink(
        CellAddress Address,
        string Reference,
        bool HasRelationshipId,
        string? Location,
        string? Tooltip);

    private sealed record XlsxHyperlinkPatchChange(
        string Reference,
        string NewLocation,
        string? NewTooltip);

    private sealed record XlsxWorksheetCommentBaseline(
        IReadOnlyDictionary<CellAddress, string> Comments,
        IReadOnlyDictionary<CellAddress, string> Authors)
    {
        public static XlsxWorksheetCommentBaseline Capture(Sheet sheet) =>
            new(new Dictionary<CellAddress, string>(sheet.Comments),
                new Dictionary<CellAddress, string>(sheet.CommentAuthors));

        public bool EqualsModel(XlsxWorksheetCommentBaseline current)
        {
            if (Comments.Count != current.Comments.Count)
                return false;

            foreach (var (address, comment) in Comments)
            {
                if (!current.Comments.TryGetValue(address, out var currentComment) ||
                    !string.Equals(comment, currentComment, StringComparison.Ordinal))
                {
                    return false;
                }

                // GAP 2: an author-only change must also be detected so the full-save path
                // (which preserves the author, post GAP-1) is invoked rather than the patch
                // path (which only patches comment text and ignores authors).
                var baselineAuthor = Authors.TryGetValue(address, out var a) ? a : string.Empty;
                var currentAuthor = current.Authors.TryGetValue(address, out var ca) ? ca : string.Empty;
                if (!string.Equals(baselineAuthor, currentAuthor, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }

    private sealed record XlsxWorksheetCommentPatch(
        SheetId SheetId,
        string WorksheetPath,
        XlsxWorksheetCommentBaseline Original,
        XlsxWorksheetCommentBaseline Current,
        IReadOnlyDictionary<CellAddress, XlsxSourceComment> CurrentSource,
        string CommentPartPath,
        IReadOnlyList<XlsxCommentPatchChange> Changes)
    {
        public int ChangeCount => Changes.Count;

        public static bool TryCreate(
            SheetId sheetId,
            string worksheetPath,
            XlsxWorksheetCommentBaseline original,
            IReadOnlyDictionary<CellAddress, XlsxSourceComment> originalSource,
            XlsxWorksheetCommentBaseline current,
            out XlsxWorksheetCommentPatch? patch)
        {
            patch = null;
            if (original.EqualsModel(current))
                return true;

            if (original.Comments.Count != current.Comments.Count)
                return false;

            var changes = new List<XlsxCommentPatchChange>();
            var currentSource = new Dictionary<CellAddress, XlsxSourceComment>(originalSource);
            string? commentPartPath = null;
            foreach (var (address, currentComment) in current.Comments)
            {
                if (!original.Comments.TryGetValue(address, out var originalComment))
                    return false;

                if (string.Equals(originalComment, currentComment, StringComparison.Ordinal))
                    continue;

                if (string.IsNullOrEmpty(currentComment) ||
                    !originalSource.TryGetValue(address, out var source) ||
                    !string.Equals(source.Text, originalComment, StringComparison.Ordinal))
                {
                    return false;
                }

                if (commentPartPath is null)
                    commentPartPath = source.CommentPartPath;
                else if (!string.Equals(commentPartPath, source.CommentPartPath, StringComparison.OrdinalIgnoreCase))
                    return false;

                changes.Add(new XlsxCommentPatchChange(source.Reference, currentComment));
                currentSource[address] = source with { Text = currentComment };
            }

            if (changes.Count == 0)
                return true;

            patch = new XlsxWorksheetCommentPatch(
                sheetId,
                worksheetPath,
                original,
                current,
                currentSource,
                commentPartPath!,
                changes);
            return true;
        }
    }

    private sealed record XlsxSourceComment(
        CellAddress Address,
        string CommentPartPath,
        string Reference,
        string Text);

    private sealed record XlsxCommentPatchChange(
        string Reference,
        string NewText);

    private sealed record XlsxWorksheetViewBaseline(
        WorksheetViewMode ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        int ZoomPercent,
        bool ShowFormulas,
        bool IsRightToLeft,
        bool ShowZeros,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        uint? ActiveRow,
        uint? ActiveCol,
        uint? ViewTopRow,
        uint? ViewLeftCol)
    {
        public static XlsxWorksheetViewBaseline Capture(Sheet sheet) =>
            new(
                XlsxWorksheetValueSanitizer.ValidEnumOrDefault(sheet.ViewMode, WorksheetViewMode.Normal),
                sheet.ShowGridlines,
                sheet.ShowHeadings,
                sheet.ShowRulers,
                XlsxWorksheetValueSanitizer.ValidZoomPercentOrDefault(sheet.ZoomPercent),
                sheet.ShowFormulas,
                sheet.IsRightToLeft,
                sheet.ShowZeros,
                sheet.FrozenRows,
                sheet.FrozenCols,
                sheet.SplitRow,
                sheet.SplitColumn,
                sheet.ActiveRow,
                sheet.ActiveCol,
                sheet.ViewTopRow,
                sheet.ViewLeftCol);

        public int CountDifferences(XlsxWorksheetViewBaseline other)
        {
            var count = 0;
            if (ViewMode != other.ViewMode)
                count++;
            if (ShowGridlines != other.ShowGridlines)
                count++;
            if (ShowHeadings != other.ShowHeadings)
                count++;
            if (ShowRulers != other.ShowRulers)
                count++;
            if (ZoomPercent != other.ZoomPercent)
                count++;
            if (ShowFormulas != other.ShowFormulas)
                count++;
            if (IsRightToLeft != other.IsRightToLeft)
                count++;
            if (ShowZeros != other.ShowZeros)
                count++;
            if (FrozenRows != other.FrozenRows)
                count++;
            if (FrozenCols != other.FrozenCols)
                count++;
            if (SplitRow != other.SplitRow)
                count++;
            if (SplitColumn != other.SplitColumn)
                count++;
            if (ActiveRow != other.ActiveRow)
                count++;
            if (ActiveCol != other.ActiveCol)
                count++;
            if (ViewTopRow != other.ViewTopRow)
                count++;
            if (ViewLeftCol != other.ViewLeftCol)
                count++;

            return count;
        }

        public void ApplyTo(Sheet sheet)
        {
            sheet.ViewMode = ViewMode;
            sheet.ShowGridlines = ShowGridlines;
            sheet.ShowHeadings = ShowHeadings;
            sheet.ShowRulers = ShowRulers;
            sheet.ZoomPercent = ZoomPercent;
            sheet.ShowFormulas = ShowFormulas;
            sheet.IsRightToLeft = IsRightToLeft;
            sheet.ShowZeros = ShowZeros;
            sheet.FrozenRows = FrozenRows;
            sheet.FrozenCols = FrozenCols;
            sheet.SplitRow = SplitRow;
            sheet.SplitColumn = SplitColumn;
            sheet.ActiveRow = ActiveRow;
            sheet.ActiveCol = ActiveCol;
            sheet.ViewTopRow = ViewTopRow;
            sheet.ViewLeftCol = ViewLeftCol;
        }
    }

    private sealed record XlsxWorksheetViewPatch(
        SheetId SheetId,
        string WorksheetPath,
        XlsxWorksheetViewBaseline Original,
        XlsxWorksheetViewBaseline Current)
    {
        public int ChangeCount => Original.CountDifferences(Current);

        public static bool TryCreate(
            SheetId sheetId,
            string worksheetPath,
            XlsxWorksheetViewBaseline original,
            XlsxWorksheetViewBaseline current,
            out XlsxWorksheetViewPatch? patch)
        {
            patch = null;
            if (original == current)
                return true;

            patch = new XlsxWorksheetViewPatch(sheetId, worksheetPath, original, current);
            return true;
        }
    }

    private sealed record XlsxWorksheetTablePatchBaseline(
        IReadOnlyList<XlsxPatchStructuredTable> Tables)
    {
        public bool HasTables => Tables.Count > 0;

        public static XlsxWorksheetTablePatchBaseline Capture(Sheet sheet) =>
            new(sheet.StructuredTables.Select(XlsxPatchStructuredTable.Capture).ToArray());

        public bool EqualsModel(XlsxWorksheetTablePatchBaseline current)
        {
            if (Tables.Count != current.Tables.Count)
                return false;

            for (var i = 0; i < Tables.Count; i++)
            {
                if (!Tables[i].EqualsModel(current.Tables[i]))
                    return false;
            }

            return true;
        }

        public bool AllowsExistingScalarValueCellPatch(uint row, uint col)
        {
            foreach (var table in Tables)
            {
                if (!table.Contains(row, col))
                    continue;

                return table.AllowsExistingScalarDataBodyCellPatch(row, col);
            }

            return true;
        }

        public bool AllowsInsertedScalarValueCellPatch(uint row, uint col)
        {
            foreach (var table in Tables)
            {
                if (table.Contains(row, col))
                    return false;
            }

            return true;
        }
    }

    private sealed record XlsxPatchStructuredTable(
        string MetadataKey,
        GridRange Range,
        uint DataBodyStartRow,
        uint DataBodyEndRow,
        bool AllowsScalarDataBodyEdits,
        IReadOnlySet<uint> FilteredColumns,
        IReadOnlySet<uint> CalculatedFormulaColumns)
    {
        public static XlsxPatchStructuredTable Capture(StructuredTableModel table)
        {
            var rowCount = checked((int)table.Range.RowCount);
            var headerRows = Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount);
            var remainingRows = rowCount - headerRows;
            var totalsRows = table.TotalsRowShown
                ? Math.Clamp(table.TotalsRowCount ?? 1, 0, remainingRows)
                : 0;
            var dataRows = rowCount - headerRows - totalsRows;
            var dataBodyStartRow = table.Range.Start.Row + checked((uint)headerRows);
            var dataBodyEndRow = dataRows <= 0
                ? dataBodyStartRow - 1
                : dataBodyStartRow + checked((uint)dataRows) - 1;
            var allowsScalarDataBodyEdits = dataRows > 0 &&
                (table.NativeAutoFilterChildXmls?.Count ?? 0) == 0 &&
                string.IsNullOrWhiteSpace(table.NativeSortStateXml);
            var filteredColumns = table.FilterColumns
                .Where(filter => filter.ColumnId >= 0)
                .Select(filter => table.Range.Start.Col + checked((uint)filter.ColumnId))
                .Where(column => column >= table.Range.Start.Col && column <= table.Range.End.Col)
                .ToHashSet();
            var calculatedFormulaColumns = table.Columns
                .Where(column => !string.IsNullOrWhiteSpace(column.CalculatedColumnFormula))
                .Select(column => table.Range.Start.Col + checked((uint)column.Id) - 1)
                .Where(column => column >= table.Range.Start.Col && column <= table.Range.End.Col)
                .ToHashSet();

            return new XlsxPatchStructuredTable(
                CreateMetadataKey(table),
                table.Range,
                dataBodyStartRow,
                dataBodyEndRow,
                allowsScalarDataBodyEdits,
                filteredColumns,
                calculatedFormulaColumns);
        }

        public bool EqualsModel(XlsxPatchStructuredTable current) =>
            string.Equals(MetadataKey, current.MetadataKey, StringComparison.Ordinal);

        public bool Contains(uint row, uint col) =>
            row >= Range.Start.Row &&
            row <= Range.End.Row &&
            col >= Range.Start.Col &&
            col <= Range.End.Col;

        public bool AllowsExistingScalarDataBodyCellPatch(uint row, uint col) =>
            AllowsScalarDataBodyEdits &&
            row >= DataBodyStartRow &&
            row <= DataBodyEndRow &&
            col >= Range.Start.Col &&
            col <= Range.End.Col &&
            !FilteredColumns.Contains(col) &&
            !CalculatedFormulaColumns.Contains(col);

        private static string CreateMetadataKey(StructuredTableModel table)
        {
            var builder = new StringBuilder();
            Append(builder, table.Id);
            Append(builder, table.Name);
            Append(builder, table.DisplayName);
            Append(builder, table.Range.ToString());
            Append(builder, table.HasAutoFilter);
            Append(builder, table.TotalsRowShown);
            Append(builder, table.HeaderRowCount);
            Append(builder, table.TotalsRowCount);
            Append(builder, table.InsertRow);
            Append(builder, table.InsertRowShift);
            Append(builder, table.Published);
            Append(builder, table.Comment);
            Append(builder, table.StyleName);
            Append(builder, table.ShowFirstColumn);
            Append(builder, table.ShowLastColumn);
            Append(builder, table.ShowRowStripes);
            Append(builder, table.ShowColumnStripes);
            Append(builder, NormalizePackagePart(table.PackagePart));
            Append(builder, table.NativeSortStateXml);
            AppendDictionary(builder, table.NativeAttributes);
            AppendList(builder, table.NativeChildXmls);
            AppendDictionary(builder, table.NativeAutoFilterAttributes);
            AppendList(builder, table.NativeAutoFilterChildXmls);
            AppendDictionary(builder, table.NativeStyleInfoAttributes);
            AppendList(builder, table.NativeStyleInfoChildXmls);
            Append(builder, table.Columns.Count);
            foreach (var column in table.Columns)
            {
                Append(builder, column.Id);
                Append(builder, column.Name);
                Append(builder, column.TotalsRowLabel);
                Append(builder, column.TotalsRowFunction);
                Append(builder, column.CalculatedColumnFormula);
                Append(builder, column.TotalsRowFormula);
                AppendList(builder, column.NativeChildXmls);
                AppendDictionary(builder, column.NativeAttributes);
            }

            Append(builder, table.FilterColumns.Count);
            foreach (var filter in table.FilterColumns)
            {
                Append(builder, filter.ColumnId);
                AppendList(builder, filter.Values);
                Append(builder, filter.IncludeBlank);
                Append(builder, filter.CustomFiltersAnd);
                Append(builder, filter.CustomFiltersAndRaw);
                AppendDictionary(builder, filter.NativeCustomFiltersAttributes);
                AppendList(builder, filter.NativeFilterXmls);
                AppendDictionary(builder, filter.NativeAttributes);
                Append(builder, filter.CustomFilters.Count);
                foreach (var customFilter in filter.CustomFilters)
                {
                    Append(builder, customFilter.Operator);
                    Append(builder, customFilter.Value);
                    AppendDictionary(builder, customFilter.NativeAttributes);
                }
            }

            return builder.ToString();
        }

        private static string NormalizePackagePart(string packagePart) =>
            XlsxPackagePath.NormalizePackagePath(packagePart);

        private static void Append(StringBuilder builder, object? value)
        {
            var text = value switch
            {
                null => "",
                bool boolean => boolean ? "1" : "0",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            builder.Append(text.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(text);
            builder.Append('|');
        }

        private static void AppendList(StringBuilder builder, IReadOnlyList<string>? values)
        {
            Append(builder, values?.Count ?? 0);
            foreach (var value in values ?? [])
                Append(builder, value);
        }

        private static void AppendDictionary(StringBuilder builder, IReadOnlyDictionary<string, string>? values)
        {
            Append(builder, values?.Count ?? 0);
            if (values is null)
                return;

            foreach (var (key, value) in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Append(builder, key);
                Append(builder, value);
            }
        }
    }

    private sealed class XlsxChartSourceRangeIndex
    {
        private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
        private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        private const string ChartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";

        private readonly IReadOnlyList<XlsxChartSourceSheetBaseline> _sheets;
        private readonly IReadOnlyDictionary<SheetId, IReadOnlyList<GridRange>> _rangesBySheet;

        private XlsxChartSourceRangeIndex(
            IReadOnlyList<XlsxChartSourceSheetBaseline> sheets,
            IReadOnlyDictionary<SheetId, IReadOnlyList<GridRange>> rangesBySheet)
        {
            _sheets = sheets;
            _rangesBySheet = rangesBySheet;
        }

        public static XlsxChartSourceRangeIndex? TryCreate(
            ZipArchive archive,
            Workbook workbook,
            XlsxWorkbookWorksheetPathMap worksheetPathMap,
            out string? blockReason)
            => TryCreate(archive, workbook, worksheetPathMap, sheetXmlLayout: null, out blockReason);

        public static XlsxChartSourceRangeIndex? TryCreate(
            ZipArchive archive,
            Workbook workbook,
            XlsxWorkbookWorksheetPathMap worksheetPathMap,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout,
            out string? blockReason)
            => TryCreate(archive, workbook, worksheetPathMap.SheetPathsByName, sheetXmlLayout, out blockReason);

        public static XlsxChartSourceRangeIndex? TryCreate(
            ZipArchive archive,
            Workbook workbook,
            IReadOnlyDictionary<string, string> sheetPathsByName,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout,
            out string? blockReason)
        {
            try
            {
                return TryCreate(
                    workbook,
                    sheet =>
                    {
                        if (!sheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath) ||
                            !TryReadWorksheetChartParts(archive, worksheetPath, sheetXmlLayout, sheet, out var chartParts))
                        {
                            return (false, []);
                        }

                        return (true, chartParts);
                    },
                    out blockReason);
            }
            catch
            {
                blockReason = "baseline_chart_source_exception";
                return null;
            }
        }

        public static XlsxChartSourceRangeIndex? TryCreate(
            Workbook workbook,
            IReadOnlyDictionary<string, SheetXmlLayout> sheetXmlLayout,
            out string? blockReason)
        {
            try
            {
                return TryCreate(
                    workbook,
                    sheet =>
                    {
                        if (!sheetXmlLayout.TryGetValue(sheet.Name, out var layout))
                        {
                            return (false, []);
                        }

                        return (true, layout.ChartParts);
                    },
                    out blockReason);
            }
            catch
            {
                blockReason = "baseline_chart_source_exception";
                return null;
            }
        }

        private static XlsxChartSourceRangeIndex? TryCreate(
            Workbook workbook,
            Func<Sheet, (bool Success, IReadOnlyList<XlsxChartPackagePart> ChartParts)> readChartParts,
            out string? blockReason)
        {
            blockReason = null;
            var sheetIdsByName = workbook.Sheets.ToDictionary(
                sheet => sheet.Name,
                sheet => sheet.Id,
                StringComparer.OrdinalIgnoreCase);
            var rangesBySheet = new Dictionary<SheetId, List<GridRange>>();
            var sheetBaselines = new List<XlsxChartSourceSheetBaseline>(workbook.SheetCount);
            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Charts.Any(IsPatchUnsafeChartModel))
                {
                    blockReason = "baseline_chart_source_model";
                    return null;
                }

                var (success, chartParts) = readChartParts(sheet);
                if (!success)
                {
                    blockReason = "baseline_chart_source_graph";
                    return null;
                }

                if (chartParts.Count != sheet.Charts.Count)
                {
                    blockReason = "baseline_chart_source_count";
                    return null;
                }

                sheetBaselines.Add(new XlsxChartSourceSheetBaseline(sheet.Id, sheet.Name, sheet.Charts.Count));
                foreach (var chartPart in chartParts)
                {
                    if (!TryReadChartSourceRanges(
                            chartPart.Xml,
                            sheetIdsByName,
                            out var chartRanges))
                    {
                        blockReason = "baseline_chart_source_formula";
                        return null;
                    }

                    foreach (var range in chartRanges)
                    {
                        if (!rangesBySheet.TryGetValue(range.Start.Sheet, out var ranges))
                        {
                            ranges = [];
                            rangesBySheet[range.Start.Sheet] = ranges;
                        }

                        ranges.Add(range);
                    }
                }
            }

            return new XlsxChartSourceRangeIndex(
                sheetBaselines,
                rangesBySheet.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<GridRange>)pair.Value.ToArray()));
        }

        public bool Matches(Workbook workbook)
        {
            if (workbook.SheetCount != _sheets.Count)
                return false;

            for (var i = 0; i < _sheets.Count; i++)
            {
                var baseline = _sheets[i];
                var sheet = workbook.Sheets[i];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal) ||
                    sheet.Charts.Count != baseline.ChartCount ||
                    sheet.Charts.Any(IsPatchUnsafeChartModel))
                {
                    return false;
                }
            }

            return true;
        }

        public bool Contains(SheetId sheetId, uint row, uint col)
        {
            if (!_rangesBySheet.TryGetValue(sheetId, out var ranges))
                return false;

            var address = new CellAddress(sheetId, row, col);
            foreach (var range in ranges)
            {
                if (range.Contains(address))
                    return true;
            }

            return false;
        }

        private static bool IsPatchUnsafeChartModel(ChartModel chart) =>
            chart.ExternalData is not null ||
            chart.UserShapes is not null;

        private static bool TryReadWorksheetChartPaths(
            ZipArchive archive,
            string worksheetPath,
            out IReadOnlyList<string> chartPaths)
        {
            chartPaths = [];
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                return false;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var drawingElements = worksheetXml.Root?.Elements(worksheetNs + "drawing").ToList() ?? [];
            if (drawingElements.Count == 0)
                return true;
            if (drawingElements.Count > 1)
                return false;

            var drawingRelId = drawingElements[0].Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(drawingRelId))
                return false;

            var worksheetRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
            if (worksheetRelsEntry is null)
                return false;

            var worksheetRelsXml = XlsxPackageXmlEditor.LoadXml(worksheetRelsEntry);
            if (!TryGetRelationshipTarget(
                    worksheetRelsXml.Root?.Elements(packageRelNs + "Relationship").ToArray() ?? [],
                    drawingRelId,
                    DrawingRelationshipType,
                    out var drawingTarget))
            {
                return false;
            }

            var drawingPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, drawingTarget);
            var drawingEntry = archive.GetEntry(drawingPath);
            if (drawingEntry is null)
                return false;

            var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
            var chartElements = drawingXml
                .Descendants()
                .Where(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart")
                .ToArray();
            if (chartElements.Length == 0)
                return true;

            var drawingRelsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(drawingPath));
            if (drawingRelsEntry is null)
                return false;

            var drawingRelsXml = XlsxPackageXmlEditor.LoadXml(drawingRelsEntry);
            var relationships = drawingRelsXml.Root?.Elements(packageRelNs + "Relationship").ToArray() ?? [];
            var paths = new List<string>(chartElements.Length);
            foreach (var chartElement in chartElements)
            {
                var chartRelId = chartElement.Attribute(relNs + "id")?.Value;
                var chartRelationshipType = chartElement.Name == chartExNs + "chart"
                    ? ChartExRelationshipType
                    : ChartRelationshipType;
                if (string.IsNullOrWhiteSpace(chartRelId) ||
                    !TryGetRelationshipTarget(relationships, chartRelId, chartRelationshipType, out var chartTarget))
                {
                    return false;
                }

                paths.Add(XlsxPackagePath.ResolveRelationshipTarget(drawingPath, chartTarget));
            }

            chartPaths = paths;
            return true;
        }

        private static bool TryReadWorksheetChartParts(
            ZipArchive archive,
            string worksheetPath,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout,
            Sheet sheet,
            out IReadOnlyList<XlsxChartPackagePart> chartParts)
        {
            if (sheetXmlLayout is not null &&
                sheetXmlLayout.TryGetValue(sheet.Name, out var layout) &&
                string.Equals(layout.WorksheetPath, worksheetPath, StringComparison.OrdinalIgnoreCase))
            {
                chartParts = layout.ChartParts;
                return true;
            }

            chartParts = [];
            if (!TryReadWorksheetChartPaths(archive, worksheetPath, out var chartPaths))
                return false;

            if (chartPaths.Count == 0)
                return true;

            var parts = new List<XlsxChartPackagePart>(chartPaths.Count);
            foreach (var chartPath in chartPaths)
            {
                var chartEntry = archive.GetEntry(chartPath);
                if (chartEntry is null)
                    return false;

                parts.Add(new XlsxChartPackagePart(
                    XlsxPackageXmlEditor.LoadXml(chartEntry),
                    Relationships: null,
                    Name: null,
                    Anchor: null));
            }

            chartParts = parts;
            return true;
        }

        private static bool TryReadChartSourceRanges(
            ZipArchive archive,
            string chartPath,
            IReadOnlyDictionary<string, SheetId> sheetIdsByName,
            out IReadOnlyList<GridRange> ranges)
        {
            ranges = [];
            var chartEntry = archive.GetEntry(chartPath);
            if (chartEntry is null)
                return false;

            var chartXml = XlsxPackageXmlEditor.LoadXml(chartEntry);
            return TryReadChartSourceRanges(chartXml, sheetIdsByName, out ranges);
        }

        private static bool TryReadChartSourceRanges(
            XDocument chartXml,
            IReadOnlyDictionary<string, SheetId> sheetIdsByName,
            out IReadOnlyList<GridRange> ranges)
        {
            ranges = [];
            var formulas = chartXml
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "f", StringComparison.Ordinal))
                .Select(element => element.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (formulas.Length == 0)
                return false;

            var parsedRanges = new List<GridRange>(formulas.Length);
            foreach (var formula in formulas)
            {
                if (!TryParseSimpleChartFormulaRange(formula, sheetIdsByName, out var range))
                    return false;

                parsedRanges.Add(range);
            }

            ranges = parsedRanges;
            return true;
        }

        private static bool TryParseSimpleChartFormulaRange(
            string formula,
            IReadOnlyDictionary<string, SheetId> sheetIdsByName,
            out GridRange range)
        {
            range = default;
            var text = formula.Trim();
            if (text.StartsWith("=", StringComparison.Ordinal))
                text = text[1..].Trim();

            if (text.Length == 0 ||
                text.Contains('[', StringComparison.Ordinal) ||
                text.Contains(']', StringComparison.Ordinal) ||
                text.Contains(',', StringComparison.Ordinal) ||
                text.Contains(';', StringComparison.Ordinal) ||
                text.Contains('(', StringComparison.Ordinal) ||
                text.Contains(')', StringComparison.Ordinal) ||
                text.Contains('#', StringComparison.Ordinal))
            {
                return false;
            }

            var bang = text.LastIndexOf('!');
            if (bang <= 0 || bang == text.Length - 1)
                return false;

            var sheetNameToken = text[..bang].Trim();
            if (!TryUnquoteSheetName(sheetNameToken, out var sheetName) ||
                !sheetIdsByName.TryGetValue(sheetName, out var sheetId))
            {
                return false;
            }

            var referenceText = text[(bang + 1)..].Replace("$", "", StringComparison.Ordinal).Trim();
            if (referenceText.Length == 0 || referenceText.Any(char.IsWhiteSpace))
                return false;

            var parts = referenceText.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                if (!CellAddress.TryParse(parts[0], sheetId, out var address))
                    return false;

                range = new GridRange(address, address);
                return true;
            }

            if (parts.Length != 2 ||
                !CellAddress.TryParse(parts[0], sheetId, out var start) ||
                !CellAddress.TryParse(parts[1], sheetId, out var end))
            {
                return false;
            }

            range = new GridRange(start, end);
            return true;
        }

        private static bool TryUnquoteSheetName(string token, out string sheetName)
        {
            sheetName = "";
            if (string.IsNullOrWhiteSpace(token) || token.Contains(':', StringComparison.Ordinal))
                return false;

            if (token.StartsWith("'", StringComparison.Ordinal))
            {
                if (!token.EndsWith("'", StringComparison.Ordinal) || token.Length < 2)
                    return false;

                sheetName = token[1..^1].Replace("''", "'", StringComparison.Ordinal);
                return sheetName.Length > 0;
            }

            if (token.Contains('\'', StringComparison.Ordinal))
                return false;

            sheetName = token;
            return true;
        }

        private static bool TryGetRelationshipTarget(
            IReadOnlyList<XElement> relationships,
            string relationshipId,
            string relationshipType,
            out string target)
        {
            var relationship = FindInternalRelationshipByIdAndType(relationships, relationshipId, relationshipType);
            target = relationship?.Attribute("Target")?.Value ?? "";
            return !string.IsNullOrWhiteSpace(target);
        }

        private static XElement? FindInternalRelationshipByIdAndType(
            IEnumerable<XElement> relationships,
            string relationshipId,
            string relationshipType) =>
            relationships.SingleOrDefault(relationship =>
                RelationshipHasId(relationship, relationshipId) &&
                RelationshipHasType(relationship, relationshipType) &&
                RelationshipHasInternalTarget(relationship));

        private static bool RelationshipHasId(XElement relationship, string relationshipId) =>
            string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal);

        private static bool RelationshipHasType(XElement relationship, string relationshipType) =>
            string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase);

        private static bool RelationshipHasInternalTarget(XElement relationship) =>
            relationship.Attribute("TargetMode") is null;
    }

    private sealed record XlsxChartSourceSheetBaseline(
        SheetId SheetId,
        string SheetName,
        int ChartCount);

    private sealed class XlsxPivotSourceRangeIndex
    {
        private readonly int _pivotCacheCount;
        private readonly IReadOnlyList<XlsxPivotSourceSheetBaseline> _sheets;
        private readonly IReadOnlyDictionary<SheetId, IReadOnlyList<GridRange>> _rangesBySheet;

        private XlsxPivotSourceRangeIndex(
            int pivotCacheCount,
            IReadOnlyList<XlsxPivotSourceSheetBaseline> sheets,
            IReadOnlyDictionary<SheetId, IReadOnlyList<GridRange>> rangesBySheet)
        {
            _pivotCacheCount = pivotCacheCount;
            _sheets = sheets;
            _rangesBySheet = rangesBySheet;
        }

        public static XlsxPivotSourceRangeIndex? TryCreate(
            Workbook workbook,
            out string? blockReason)
        {
            blockReason = null;
            if (workbook.Slicers.Count > 0 || workbook.Timelines.Count > 0)
            {
                blockReason = "baseline_pivot_source_slicer_timeline";
                return null;
            }

            var sheetIdsByName = CreateSheetIdLookup(workbook);
            var rangesBySheet = new Dictionary<SheetId, List<GridRange>>();
            foreach (var cache in workbook.PivotCaches)
            {
                if (IsPatchUnsafePivotCache(cache) ||
                    !TryGetPivotSourceSheetId(sheetIdsByName, cache, out var sourceSheetId))
                {
                    blockReason = "baseline_pivot_source_model";
                    return null;
                }

                GridRange range;
                try
                {
                    range = GridRange.Parse(cache.SourceReference!, sourceSheetId);
                }
                catch
                {
                    blockReason = "baseline_pivot_source_range";
                    return null;
                }

                if (!rangesBySheet.TryGetValue(sourceSheetId, out var ranges))
                {
                    ranges = [];
                    rangesBySheet[sourceSheetId] = ranges;
                }

                ranges.Add(range);
            }

            return new XlsxPivotSourceRangeIndex(
                workbook.PivotCaches.Count,
                workbook.Sheets
                    .Select(sheet => new XlsxPivotSourceSheetBaseline(sheet.Id, sheet.Name, sheet.PivotTables.Count))
                    .ToArray(),
                rangesBySheet.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<GridRange>)pair.Value.ToArray()));
        }

        public bool Matches(Workbook workbook)
        {
            if (workbook.PivotCaches.Count != _pivotCacheCount ||
                workbook.Slicers.Count > 0 ||
                workbook.Timelines.Count > 0 ||
                workbook.SheetCount != _sheets.Count ||
                workbook.PivotCaches.Any(IsPatchUnsafePivotCache))
            {
                return false;
            }

            for (var i = 0; i < _sheets.Count; i++)
            {
                var baseline = _sheets[i];
                var sheet = workbook.Sheets[i];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal) ||
                    sheet.PivotTables.Count != baseline.PivotTableCount)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Contains(SheetId sheetId, uint row, uint col)
        {
            if (!_rangesBySheet.TryGetValue(sheetId, out var ranges))
                return false;

            var address = new CellAddress(sheetId, row, col);
            foreach (var range in ranges)
            {
                if (range.Contains(address))
                    return true;
            }

            return false;
        }

        private static bool IsPatchUnsafePivotCache(PivotCacheModel cache) =>
            cache.SourceType != PivotCacheSourceType.WorksheetRange ||
            string.IsNullOrWhiteSpace(cache.SourceSheetName) ||
            string.IsNullOrWhiteSpace(cache.SourceReference) ||
            !string.IsNullOrWhiteSpace(cache.SourceTableName) ||
            cache.ConnectionId is not null ||
            cache.IsOlap;

        private static IReadOnlyDictionary<string, SheetId> CreateSheetIdLookup(Workbook workbook) =>
            workbook.Sheets.ToDictionary(
                sheet => sheet.Name,
                sheet => sheet.Id,
                StringComparer.OrdinalIgnoreCase);

        private static bool TryGetPivotSourceSheetId(
            IReadOnlyDictionary<string, SheetId> sheetIdsByName,
            PivotCacheModel cache,
            out SheetId sourceSheetId)
        {
            sourceSheetId = default;
            return !string.IsNullOrWhiteSpace(cache.SourceSheetName) &&
                   sheetIdsByName.TryGetValue(cache.SourceSheetName, out sourceSheetId);
        }
    }

    private sealed record XlsxPivotSourceSheetBaseline(
        SheetId SheetId,
        string SheetName,
        int PivotTableCount);

    private sealed record XlsxWorksheetCellPatchBaseline(
        SheetId SheetId,
        string SheetName,
        string WorksheetPath,
        int CellCount,
        int StyleOnlyCellCount,
        XlsxWorksheetDimensionBaseline Dimensions,
        IReadOnlyList<GridRange> MergedRegions,
        XlsxWorksheetHyperlinkBaseline Hyperlinks,
        IReadOnlyDictionary<CellAddress, XlsxSourceHyperlink> SourceHyperlinks,
        XlsxWorksheetCommentBaseline Comments,
        IReadOnlyDictionary<CellAddress, XlsxSourceComment> SourceComments,
        XlsxWorksheetViewBaseline View,
        XlsxWorksheetTablePatchBaseline Tables,
        XlsxSourceStyleOnlyCellCollection SourceStyleOnlyCells,
        XlsxPatchCellEntry[] Cells)
    {
        public bool TryGetCell(uint row, uint col, out XlsxPatchCell cell)
        {
            var low = 0;
            var high = Cells.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                ref readonly var entry = ref Cells[mid];
                var compare = entry.CompareTo(row, col);
                if (compare < 0)
                {
                    low = mid + 1;
                    continue;
                }

                if (compare > 0)
                {
                    high = mid - 1;
                    continue;
                }

                cell = entry.Cell;
                return true;
            }

            cell = default;
            return false;
        }

        public bool TryGetSourceStyleOnlyCell(uint row, uint col, out XlsxSourceStyleOnlyCellEntry cell) =>
            SourceStyleOnlyCells.TryGet(row, col, out cell);

        public XlsxPatchCellEntry[] WithAppliedCellChanges(IReadOnlyList<XlsxCellValuePatch> changes)
        {
            if (changes.Count == 0)
                return Cells;

            var cells = new XlsxPatchCellEntry[checked(Cells.Length + CountCellPatchChanges(changes, XlsxCellValuePatchKind.InsertedLiteralValue))];
            var writeIndex = 0;
            foreach (var entry in Cells)
            {
                var current = entry.Cell;
                var deleted = false;
                foreach (var change in changes)
                {
                    if (change.Row != entry.Row || change.Col != entry.Col)
                        continue;

                    if (change.Kind == XlsxCellValuePatchKind.DeletedCell)
                    {
                        deleted = true;
                        break;
                    }

                    current = current with
                    {
                        Value = change.NewValue,
                        FormulaText = change.Kind == XlsxCellValuePatchKind.FormulaTextAndCachedValue
                            ? change.NewFormulaText
                            : current.FormulaText,
                        ArrayMode = change.NewArrayMode,
                        StyleId = change.NewStyleId,
                        SourceStyleIndex = change.HasStyleChange
                            ? change.NewSourceStyleIndex
                            : current.SourceStyleIndex,
                        // R61-io-rich-text-runs-6-1: advance the baseline's rich-run snapshot only
                        // when this change actually carries updated run content -- a LiteralValue
                        // patch always reflects the cell's current runs (attached unconditionally
                        // above), while a CellStyle patch only does when RichRunsChanged is true.
                        // Any other kind (formula/deleted-cell) leaves the prior snapshot in place.
                        RichRuns = change.Kind == XlsxCellValuePatchKind.LiteralValue ||
                                   (change.Kind == XlsxCellValuePatchKind.CellStyle && change.RichRunsChanged)
                            ? change.RichRuns
                            : current.RichRuns
                    };
                }

                if (!deleted)
                    cells[writeIndex++] = entry with { Cell = current };
            }

            foreach (var change in changes)
            {
                if (change.Kind != XlsxCellValuePatchKind.InsertedLiteralValue)
                    continue;

                cells[writeIndex++] = new XlsxPatchCellEntry(
                    change.Row,
                    change.Col,
                    new XlsxPatchCell(
                        change.NewValue,
                        null,
                        FormulaArrayMode.Dynamic,
                        change.NewStyleId,
                        change.NewSourceStyleIndex,
                        false,
                        change.RichRuns));
            }

            if (writeIndex != cells.Length)
                Array.Resize(ref cells, writeIndex);

            Array.Sort(cells, XlsxPatchCellEntry.Compare);
            return cells;
        }

        public XlsxSourceStyleOnlyCellCollection WithConsumedSourceStyleOnlyCells(IReadOnlyList<XlsxCellValuePatch> changes) =>
            SourceStyleOnlyCells.WithConsumed(changes);
    }

    private static int CountCellPatchChanges(
        IReadOnlyList<XlsxCellValuePatch>? changes,
        XlsxCellValuePatchKind kind)
    {
        if (changes is null || changes.Count == 0)
            return 0;

        var count = 0;
        foreach (var change in changes)
        {
            if (change.Kind == kind)
                count++;
        }

        return count;
    }

    private static int CountConsumedSourceStyleOnlyCells(IReadOnlyList<XlsxCellValuePatch>? changes)
    {
        if (changes is null || changes.Count == 0)
            return 0;

        var count = 0;
        foreach (var change in changes)
        {
            if (change.ConsumesSourceStyleOnlyCell)
                count++;
        }

        return count;
    }

    private sealed class XlsxSourceStyleOnlyCellCollection
    {
        public static XlsxSourceStyleOnlyCellCollection Empty { get; } = new([], []);

        private readonly XlsxSourceStyleOnlyCellEntry[] _cells;
        private readonly XlsxSourceStyleOnlyRunEntry[] _runs;

        private XlsxSourceStyleOnlyCellCollection(
            XlsxSourceStyleOnlyCellEntry[] cells,
            XlsxSourceStyleOnlyRunEntry[] runs)
        {
            _cells = cells;
            _runs = runs;
        }

        private bool IsEmpty => _cells.Length == 0 && _runs.Length == 0;

        public static XlsxSourceStyleOnlyCellCollection FromCells(XlsxSourceStyleOnlyCellEntry[] cells) =>
            cells.Length == 0 ? Empty : new XlsxSourceStyleOnlyCellCollection(cells, []);

        public static XlsxSourceStyleOnlyCellCollection FromRuns(XlsxSourceStyleOnlyRunEntry[] runs) =>
            runs.Length == 0 ? Empty : new XlsxSourceStyleOnlyCellCollection([], runs);

        public bool TryGet(uint row, uint col, out XlsxSourceStyleOnlyCellEntry cell)
        {
            if (_runs.Length > 0)
                return TryGetFromRuns(row, col, out cell);

            var low = 0;
            var high = _cells.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                ref readonly var entry = ref _cells[mid];
                var compare = entry.CompareTo(row, col);
                if (compare < 0)
                {
                    low = mid + 1;
                    continue;
                }

                if (compare > 0)
                {
                    high = mid - 1;
                    continue;
                }

                cell = entry;
                return true;
            }

            cell = default;
            return false;
        }

        public XlsxSourceStyleOnlyCellCollection WithConsumed(IReadOnlyList<XlsxCellValuePatch> changes)
        {
            if (IsEmpty || CountConsumedSourceStyleOnlyCells(changes) == 0)
                return this;

            var consumedCells = GetConsumedSourceStyleOnlyCells(changes);
            return _runs.Length > 0
                ? WithoutConsumedRunCells(consumedCells)
                : WithoutConsumedCellEntries(consumedCells);
        }

        private bool TryGetFromRuns(uint row, uint col, out XlsxSourceStyleOnlyCellEntry cell)
        {
            var low = 0;
            var high = _runs.Length - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                ref readonly var run = ref _runs[mid];
                var compare = run.CompareTo(row, col);
                if (compare < 0)
                {
                    low = mid + 1;
                    continue;
                }

                if (compare > 0)
                {
                    high = mid - 1;
                    continue;
                }

                cell = new XlsxSourceStyleOnlyCellEntry(row, col, run.StyleId, run.SourceStyleIndex);
                return true;
            }

            cell = default;
            return false;
        }

        private XlsxSourceStyleOnlyCellCollection WithoutConsumedCellEntries(
            IReadOnlyList<(uint Row, uint Col)> consumedCells)
        {
            var result = new List<XlsxSourceStyleOnlyCellEntry>(_cells.Length);
            foreach (var entry in _cells)
            {
                if (!ContainsCell(consumedCells, entry.Row, entry.Col))
                    result.Add(entry);
            }

            return result.Count == _cells.Length ? this : FromCells(result.ToArray());
        }

        private XlsxSourceStyleOnlyCellCollection WithoutConsumedRunCells(
            IReadOnlyList<(uint Row, uint Col)> consumedCells)
        {
            var result = new List<XlsxSourceStyleOnlyRunEntry>(_runs.Length);
            var consumedIndex = 0;
            var changed = false;
            foreach (var run in _runs)
            {
                while (consumedIndex < consumedCells.Count && CellIsBeforeRun(consumedCells[consumedIndex], run))
                    consumedIndex++;

                var startCol = run.StartCol;
                while (consumedIndex < consumedCells.Count &&
                       consumedCells[consumedIndex].Row == run.Row &&
                       consumedCells[consumedIndex].Col <= run.EndCol)
                {
                    var consumedCol = consumedCells[consumedIndex].Col;
                    if (consumedCol >= startCol)
                    {
                        changed = true;
                        if (consumedCol > startCol)
                            result.Add(run with { StartCol = startCol, EndCol = consumedCol - 1 });

                        startCol = consumedCol == uint.MaxValue ? uint.MaxValue : consumedCol + 1;
                    }

                    consumedIndex++;
                }

                if (startCol <= run.EndCol)
                    result.Add(run with { StartCol = startCol });
            }

            return changed ? FromRuns(result.ToArray()) : this;
        }

        private static IReadOnlyList<(uint Row, uint Col)> GetConsumedSourceStyleOnlyCells(
            IReadOnlyList<XlsxCellValuePatch> changes)
        {
            var consumedCells = new List<(uint Row, uint Col)>();
            foreach (var change in changes)
            {
                if (change.ConsumesSourceStyleOnlyCell)
                    consumedCells.Add((change.Row, change.Col));
            }

            consumedCells.Sort(static (left, right) =>
            {
                var rowCompare = left.Row.CompareTo(right.Row);
                return rowCompare != 0 ? rowCompare : left.Col.CompareTo(right.Col);
            });
            return consumedCells;
        }

        private static bool ContainsCell(IReadOnlyList<(uint Row, uint Col)> cells, uint row, uint col)
        {
            foreach (var cell in cells)
            {
                if (cell.Row == row && cell.Col == col)
                    return true;
            }

            return false;
        }

        private static bool CellIsBeforeRun((uint Row, uint Col) cell, XlsxSourceStyleOnlyRunEntry run) =>
            cell.Row < run.Row || cell.Row == run.Row && cell.Col < run.StartCol;
    }

    private readonly record struct XlsxSourceStyleOnlyCellEntry(
        uint Row,
        uint Col,
        StyleId StyleId,
        string? SourceStyleIndex)
    {
        public static int Compare(XlsxSourceStyleOnlyCellEntry left, XlsxSourceStyleOnlyCellEntry right)
        {
            var rowCompare = left.Row.CompareTo(right.Row);
            return rowCompare != 0
                ? rowCompare
                : left.Col.CompareTo(right.Col);
        }

        public int CompareTo(uint row, uint col)
        {
            var rowCompare = Row.CompareTo(row);
            return rowCompare != 0
                ? rowCompare
                : Col.CompareTo(col);
        }
    }

    private readonly record struct XlsxSourceStyleOnlyRunEntry(
        uint Row,
        uint StartCol,
        uint EndCol,
        StyleId StyleId,
        string? SourceStyleIndex)
    {
        public int CompareTo(uint row, uint col)
        {
            if (Row < row || Row == row && EndCol < col)
                return -1;

            if (Row > row || Row == row && StartCol > col)
                return 1;

            return 0;
        }
    }

    private readonly record struct XlsxPatchCellEntry(uint Row, uint Col, XlsxPatchCell Cell)
    {
        public static int Compare(XlsxPatchCellEntry left, XlsxPatchCellEntry right)
        {
            var rowCompare = left.Row.CompareTo(right.Row);
            return rowCompare != 0
                ? rowCompare
                : left.Col.CompareTo(right.Col);
        }

        public int CompareTo(uint row, uint col)
        {
            var rowCompare = Row.CompareTo(row);
            return rowCompare != 0
                ? rowCompare
                : Col.CompareTo(col);
        }
    }

    private readonly record struct XlsxPatchCell(
        ScalarValue Value,
        string? FormulaText,
        FormulaArrayMode ArrayMode,
        StyleId StyleId,
        string? SourceStyleIndex,
        bool IgnoreFormulaError,
        // R61-io-rich-text-runs-6-1: the baseline's own snapshot of Sheet.RichTextRuns for this
        // cell, captured alongside Value/StyleId so patch-save's change detection can notice a
        // per-run formatting edit that leaves the cell's plain Value and resolved StyleId
        // unchanged (e.g. ApplyStyleCommand.ClearOverriddenRunProperties clearing stale run
        // overrides when a whole-cell style command supersedes them). Null/empty are equivalent
        // (no rich-run overrides) — see RichRunsEqual.
        IReadOnlyList<CellTextRun>? RichRuns = null);

    private sealed record XlsxCellValuePatch(
        XlsxCellValuePatchKind Kind,
        SheetId SheetId,
        string WorksheetPath,
        uint Row,
        uint Col,
        ScalarValue OriginalValue,
        ScalarValue NewValue,
        string? OriginalFormulaText,
        string? NewFormulaText,
        FormulaArrayMode OriginalArrayMode,
        FormulaArrayMode NewArrayMode,
        StyleId OriginalStyleId,
        StyleId NewStyleId,
        string? OriginalSourceStyleIndex,
        string? NewSourceStyleIndex,
        bool OriginalIgnoreFormulaError,
        bool ConsumesSourceStyleOnlyCell = false,
        IReadOnlyList<CellTextRun>? RichRuns = null,
        // R61-io-rich-text-runs-6-1: true only for a CellStyle-kind patch whose rich-text runs
        // differ from the baseline — signals that ApplyChanges/ApplySimpleExistingCellChange must
        // rewrite the cell's <is>/run content (via RichRuns) even though Value/StyleId look
        // unchanged. Distinct from "RichRuns is null", which for a CellStyle-kind patch is
        // ambiguous between "runs didn't change" (leave content alone) and "runs were cleared to
        // none" (must still rewrite as plain text) — see the CellStyle branches below.
        bool RichRunsChanged = false,
        // R76-io-richtext-runs-4-1: the cell's preserved phonetic-guide (furigana) passthrough,
        // set only when RichRuns is also being rewritten (RichRunsChanged for a CellStyle-kind
        // patch) so a run-formatting-only edit (e.g. Bold the whole cell) re-emits the original
        // <rPh>/<phoneticPr> alongside the rebuilt <r> runs instead of silently dropping them.
        CellPhoneticGuide? PhoneticGuide = null)
    {
        public bool HasStyleChange => OriginalStyleId != NewStyleId;
    }

    private enum XlsxCellValuePatchKind
    {
        LiteralValue,
        FormulaCachedValue,
        FormulaTextAndCachedValue,
        CellStyle,
        InsertedLiteralValue,
        DeletedCell
    }
}
