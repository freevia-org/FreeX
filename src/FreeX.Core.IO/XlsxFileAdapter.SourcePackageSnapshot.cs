using System.Security.Cryptography;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    public static void ForgetLoadedPackageSnapshot(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        SourcePackages.Remove(workbook);
    }

    private static string CreateSourceModelFingerprint(Workbook workbook)
    {
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write, leaveOpen: true);
        new NativeJsonAdapter().Save(workbook, stream);
        stream.FlushFinalBlock();
        return Convert.ToHexString(hash.Hash ?? []);
    }

    private sealed record XlsxSourcePackage(
        byte[] Buffer,
        int Offset,
        int Count,
        string? ModelFingerprint,
        IReadOnlySet<string>? WorksheetsWithPreservableSourceMetadata,
        bool? HasUnsupportedConditionalFormatting,
        XlsxCellPatchBaseline? CellPatchBaseline)
    {
        private const int FingerprintCellLimit = 25_000;
        private const int CellPatchBaselineLimit = 250_000;
        private const int CellPatchChangeLimit = 256;

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
            var cellPatchBaseline = XlsxCellPatchBaseline.TryCreate(bytes, 0, bytes.Length, workbook, CellPatchBaselineLimit);
            return new XlsxSourcePackage(
                bytes,
                0,
                bytes.Length,
                fingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                cellPatchBaseline);
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
            bool? hasUnsupportedConditionalFormatting)
        {
            var fingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
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
                        XlsxCellPatchBaseline.TryCreate(
                            buffer.Array,
                            buffer.Offset,
                            (int)stream.Length,
                            workbook,
                            CellPatchBaselineLimit));
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
                    XlsxCellPatchBaseline.TryCreate(copiedBytes, 0, copiedBytes.Length, workbook, CellPatchBaselineLimit));
            }

            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(
                bytes,
                0,
                bytes.Length,
                fingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                XlsxCellPatchBaseline.TryCreate(bytes, 0, bytes.Length, workbook, CellPatchBaselineLimit));
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

        public bool TrySavePatchedCellValues(
            Workbook workbook,
            Stream stream,
            out string? currentModelFingerprint)
        {
            currentModelFingerprint = null;
            if (CellPatchBaseline is null ||
                !CellPatchBaseline.TryGetLiteralValueChanges(workbook, CellPatchChangeLimit, out var changes))
            {
                return false;
            }

            currentModelFingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
            if (changes.Count == 0)
            {
                CopyTo(stream);
                return true;
            }

            using var patchedPackage = new MemoryStream(Count + 4096);
            patchedPackage.Write(Buffer, Offset, Count);
            using (var archive = new ZipArchive(patchedPackage, ZipArchiveMode.Update, leaveOpen: true))
            {
                foreach (var group in changes.GroupBy(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase))
                {
                    var worksheetEntry = archive.GetEntry(group.Key);
                    if (worksheetEntry is null)
                        return false;

                    var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                    if (!XlsxCellPatchBaseline.ApplyChanges(worksheetXml, group))
                        return false;

                    XlsxPackageXmlEditor.ReplaceXml(archive, group.Key, worksheetXml);
                }
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

            patchedPackage.Position = 0;
            SourcePackages.Remove(workbook);
            SourcePackages.Add(workbook, Capture(
                patchedPackage,
                workbook,
                currentModelFingerprint,
                WorksheetsWithPreservableSourceMetadata,
                HasUnsupportedConditionalFormatting));
            return true;
        }

        private static bool ShouldCaptureModelFingerprint(Workbook workbook)
        {
            var cellCount = 0;
            foreach (var sheet in workbook.Sheets)
            {
                cellCount += sheet.CellCount;
                if (cellCount > FingerprintCellLimit)
                    return false;

                if (!sheet.HasStyleOnlyCells)
                    continue;

                foreach (var _ in sheet.GetStyleOnlyEntries())
                {
                    cellCount++;
                    if (cellCount > FingerprintCellLimit)
                        return false;
                }
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

    private sealed class XlsxCellPatchBaseline
    {
        private readonly IReadOnlyList<XlsxWorksheetCellPatchBaseline> _worksheets;
        private readonly string _modelFingerprint;

        private XlsxCellPatchBaseline(
            IReadOnlyList<XlsxWorksheetCellPatchBaseline> worksheets,
            string modelFingerprint)
        {
            _worksheets = worksheets;
            _modelFingerprint = modelFingerprint;
        }

        public static XlsxCellPatchBaseline? TryCreate(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            int cellLimit)
        {
            try
            {
                var totalCells = 0;
                foreach (var sheet in workbook.Sheets)
                {
                    totalCells += sheet.CellCount;
                    if (totalCells > cellLimit)
                        return null;
                }

                using var packageStream = new MemoryStream(package, offset, count, writable: false);
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
                var worksheetPathMap = XlsxWorkbookWorksheetPathMap.TryCreate(archive);
                if (worksheetPathMap is null)
                    return null;

                var worksheets = new List<XlsxWorksheetCellPatchBaseline>(workbook.SheetCount);
                foreach (var sheet in workbook.Sheets)
                {
                    if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                        return null;

                    var cells = new Dictionary<(uint Row, uint Col), XlsxPatchCell>(sheet.CellCount);
                    foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                    {
                        cells[(row, col)] = new XlsxPatchCell(
                            cell.Value,
                            cell.FormulaText,
                            cell.StyleId,
                            cell.IgnoreFormulaError);
                    }

                    worksheets.Add(new XlsxWorksheetCellPatchBaseline(
                        sheet.Id,
                        sheet.Name,
                        worksheetPath,
                        sheet.CellCount,
                        sheet.StyleOnlyCellCount,
                        cells));
                }

                return new XlsxCellPatchBaseline(worksheets, CreateSourceModelFingerprint(workbook));
            }
            catch
            {
                return null;
            }
        }

        public bool TryGetLiteralValueChanges(
            Workbook workbook,
            int changeLimit,
            out List<XlsxCellValuePatch> changes)
        {
            changes = [];
            if (workbook.SheetCount != _worksheets.Count)
                return false;

            for (var sheetIndex = 0; sheetIndex < _worksheets.Count; sheetIndex++)
            {
                var baseline = _worksheets[sheetIndex];
                var sheet = workbook.Sheets[sheetIndex];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal) ||
                    sheet.CellCount != baseline.CellCount ||
                    sheet.StyleOnlyCellCount != baseline.StyleOnlyCellCount)
                {
                    return false;
                }

                foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                {
                    if (!baseline.Cells.TryGetValue((row, col), out var original) ||
                        cell.StyleId != original.StyleId ||
                        cell.IgnoreFormulaError != original.IgnoreFormulaError ||
                        !string.Equals(cell.FormulaText, original.FormulaText, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (Equals(cell.Value, original.Value))
                        continue;

                    if (cell.HasFormula || original.FormulaText is not null || !IsPatchableLiteralValue(cell.Value))
                        return false;

                    changes.Add(new XlsxCellValuePatch(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        row,
                        col,
                        original.Value,
                        cell.Value));
                    if (changes.Count > changeLimit)
                        return false;
                }
            }

            return ModelMatchesWithOriginalValues(workbook, changes);
        }

        public static bool ApplyChanges(XDocument worksheetXml, IEnumerable<XlsxCellValuePatch> changes)
        {
            var root = worksheetXml.Root;
            if (root is null)
                return false;

            var worksheetNs = root.Name.Namespace;
            var sheetData = root.Element(worksheetNs + "sheetData");
            if (sheetData is null)
                return false;

            foreach (var change in changes)
            {
                var cell = FindCell(sheetData, worksheetNs, change.Row, change.Col);
                if (cell is null)
                    return false;

                RewriteLiteralCellValue(cell, worksheetNs, change.NewValue);
            }

            return true;
        }

        private bool ModelMatchesWithOriginalValues(Workbook workbook, IReadOnlyList<XlsxCellValuePatch> changes)
        {
            var restoredCells = new List<(Cell Cell, ScalarValue CurrentValue)>(changes.Count);
            try
            {
                foreach (var change in changes)
                {
                    var sheet = workbook.GetSheet(change.SheetId);
                    var cell = sheet?.GetCell(change.Row, change.Col);
                    if (cell is null)
                        return false;

                    restoredCells.Add((cell, cell.Value));
                    cell.Value = change.OriginalValue;
                }

                return string.Equals(
                    CreateSourceModelFingerprint(workbook),
                    _modelFingerprint,
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var (cell, currentValue) in restoredCells)
                    cell.Value = currentValue;
            }
        }

        private static bool IsPatchableLiteralValue(ScalarValue value) =>
            value is BlankValue or NumberValue or BoolValue or TextValue or DateTimeValue or ErrorValue;

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

                return rowElement
                    .Elements(cellName)
                    .FirstOrDefault(cell => string.Equals(cell.Attribute("r")?.Value, reference, StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        private static void RewriteLiteralCellValue(XElement cell, XNamespace worksheetNs, ScalarValue value)
        {
            cell.Element(worksheetNs + "f")?.Remove();
            cell.Element(worksheetNs + "v")?.Remove();
            cell.Element(worksheetNs + "is")?.Remove();

            switch (value)
            {
                case BlankValue:
                    cell.Attribute("t")?.Remove();
                    break;
                case TextValue text:
                    cell.SetAttributeValue("t", "inlineStr");
                    cell.Add(new XElement(
                        worksheetNs + "is",
                        CreateInlineTextElement(worksheetNs, text.Value)));
                    break;
                case BoolValue boolean:
                    cell.SetAttributeValue("t", "b");
                    cell.Add(new XElement(worksheetNs + "v", boolean.Value ? "1" : "0"));
                    break;
                case ErrorValue error:
                    cell.SetAttributeValue("t", "e");
                    cell.Add(new XElement(worksheetNs + "v", error.Code));
                    break;
                case DateTimeValue dateTime:
                    cell.Attribute("t")?.Remove();
                    cell.Add(new XElement(worksheetNs + "v", FormatNumber(dateTime.Value)));
                    break;
                case NumberValue number:
                    cell.Attribute("t")?.Remove();
                    cell.Add(new XElement(worksheetNs + "v", FormatNumber(number.Value)));
                    break;
            }
        }

        private static string FormatNumber(double value) =>
            value.ToString("G17", CultureInfo.InvariantCulture);

        private static XElement CreateInlineTextElement(XNamespace worksheetNs, string value)
        {
            var text = new XElement(worksheetNs + "t", value);
            if (value.Length > 0 &&
                (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            {
                text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
            }

            return text;
        }

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

    private sealed record XlsxWorksheetCellPatchBaseline(
        SheetId SheetId,
        string SheetName,
        string WorksheetPath,
        int CellCount,
        int StyleOnlyCellCount,
        IReadOnlyDictionary<(uint Row, uint Col), XlsxPatchCell> Cells);

    private sealed record XlsxPatchCell(
        ScalarValue Value,
        string? FormulaText,
        StyleId StyleId,
        bool IgnoreFormulaError);

    private sealed record XlsxCellValuePatch(
        SheetId SheetId,
        string WorksheetPath,
        uint Row,
        uint Col,
        ScalarValue OriginalValue,
        ScalarValue NewValue);
}
