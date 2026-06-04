using System.Security.Cryptography;
using System.Globalization;
using System.IO.Compression;
using System.Xml;
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
        bool AllowsCellPatchSave,
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
                AllowsCellPatchSaveForPackage(bytes, 0, bytes.Length, workbook),
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

        public static XlsxSourcePackage Capture(
            MemoryStream stream,
            Workbook workbook,
            bool allowBufferReuse,
            IReadOnlySet<string>? worksheetsWithPreservableSourceMetadata,
            bool? hasUnsupportedConditionalFormatting,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout)
            => Capture(
                stream,
                workbook,
                allowBufferReuse,
                currentModelFingerprint: null,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                sheetXmlLayout);

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
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout = null)
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
                        AllowsCellPatchSaveForPackage(buffer.Array, buffer.Offset, (int)stream.Length, workbook),
                        XlsxCellPatchBaseline.TryCreate(
                            buffer.Array,
                            buffer.Offset,
                            (int)stream.Length,
                            workbook,
                            CellPatchBaselineLimit,
                            sheetXmlLayout));
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
                    AllowsCellPatchSaveForPackage(copiedBytes, 0, copiedBytes.Length, workbook),
                    XlsxCellPatchBaseline.TryCreate(
                        copiedBytes,
                        0,
                        copiedBytes.Length,
                        workbook,
                        CellPatchBaselineLimit,
                        sheetXmlLayout));
            }

            var bytes = ReadBytes(stream);
            return new XlsxSourcePackage(
                bytes,
                0,
                bytes.Length,
                fingerprint,
                worksheetsWithPreservableSourceMetadata,
                hasUnsupportedConditionalFormatting,
                AllowsCellPatchSaveForPackage(bytes, 0, bytes.Length, workbook),
                XlsxCellPatchBaseline.TryCreate(
                    bytes,
                    0,
                    bytes.Length,
                    workbook,
                    CellPatchBaselineLimit,
                    sheetXmlLayout));
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
            ref string? currentModelFingerprint)
        {
            if (!AllowsCellPatchSave ||
                CellPatchBaseline is null ||
                !CellPatchBaseline.TryGetPatchableValueChanges(
                    workbook,
                    CellPatchChangeLimit,
                    currentModelFingerprint,
                    out var changes,
                    out var dimensionChanges))
            {
                return false;
            }

            currentModelFingerprint = GetModelFingerprint(workbook, currentModelFingerprint);
            var patchedModelFingerprint = currentModelFingerprint ?? CreateModelFingerprint(workbook);
            currentModelFingerprint = patchedModelFingerprint;
            if (changes.Count == 0 && dimensionChanges.Count == 0)
            {
                CopyTo(stream);
                return true;
            }

            using var patchedPackage = new MemoryStream(Count + 4096);
            patchedPackage.Write(Buffer, Offset, Count);
            using (var archive = new ZipArchive(patchedPackage, ZipArchiveMode.Update, leaveOpen: true))
            {
                var cellChangesByWorksheet = changes
                    .GroupBy(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
                var dimensionChangesByWorksheet = dimensionChanges
                    .ToDictionary(change => change.WorksheetPath, StringComparer.OrdinalIgnoreCase);
                var worksheetPaths = cellChangesByWorksheet.Keys
                    .Concat(dimensionChangesByWorksheet.Keys)
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var worksheetPath in worksheetPaths)
                {
                    var worksheetEntry = archive.GetEntry(worksheetPath);
                    if (worksheetEntry is null)
                        return false;

                    var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                    if (cellChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetCellChanges) &&
                        !XlsxCellPatchBaseline.ApplyChanges(worksheetXml, worksheetCellChanges))
                    {
                        return false;
                    }

                    if (dimensionChangesByWorksheet.TryGetValue(worksheetPath, out var worksheetDimensionPatch) &&
                        !XlsxCellPatchBaseline.ApplyDimensionChanges(worksheetXml, worksheetDimensionPatch))
                    {
                        return false;
                    }

                    XlsxPackageXmlEditor.ReplaceXml(archive, worksheetPath, worksheetXml);
                }

                if (changes.Any(change =>
                        change.Kind == XlsxCellValuePatchKind.FormulaTextAndCachedValue ||
                        (change.Kind == XlsxCellValuePatchKind.DeletedCell && change.OriginalFormulaText is not null)))
                {
                    XlsxExcelCompatibilityNormalizer.RemoveCalcChain(archive);
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

            SourcePackages.Remove(workbook);
            if (patchedPackage.TryGetBuffer(out var patchedBuffer) &&
                patchedBuffer.Array is not null &&
                patchedPackage.Length <= int.MaxValue)
            {
                SourcePackages.Add(workbook, new XlsxSourcePackage(
                    patchedBuffer.Array,
                    patchedBuffer.Offset,
                    (int)patchedPackage.Length,
                    patchedModelFingerprint,
                    WorksheetsWithPreservableSourceMetadata,
                    HasUnsupportedConditionalFormatting,
                    AllowsCellPatchSave,
                    CellPatchBaseline.WithAppliedChanges(changes, dimensionChanges, patchedModelFingerprint)));
            }
            else
            {
                patchedPackage.Position = 0;
                SourcePackages.Add(workbook, Capture(
                    patchedPackage,
                    workbook,
                    currentModelFingerprint,
                    WorksheetsWithPreservableSourceMetadata,
                    HasUnsupportedConditionalFormatting));
            }

            return true;
        }

        private static bool AllowsCellPatchSaveForPackage(
            byte[] package,
            int offset,
            int count,
            Workbook workbook)
        {
            if (WorkbookRequiresFullSavePostProcessing(workbook))
                return false;

            try
            {
                using var packageStream = new MemoryStream(package, offset, count, writable: false);
                using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
                return PackageAllowsCellPatchSave(archive);
            }
            catch
            {
                return false;
            }
        }

        private static bool WorkbookRequiresFullSavePostProcessing(Workbook workbook)
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.CustomProperties.Count > 0 ||
                    sheet.Charts.Count > 0 ||
                    sheet.PivotTables.Count > 0 ||
                    sheet.Pictures.Count > 0 ||
                    sheet.TextBoxes.Count > 0 ||
                    sheet.DrawingShapes.Count > 0 ||
                    sheet.Sparklines.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PackageAllowsCellPatchSave(ZipArchive archive)
        {
            foreach (var entry in archive.Entries)
            {
                var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
                if (IsPatchUnsafePackagePart(path))
                    return false;

                if (path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
                    !IsValidRelationshipPart(entry))
                {
                    return false;
                }
            }

            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            if (workbookEntry is null)
                return false;

            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            if (workbookXml.Root is null ||
                workbookXml.Root.Element(workbookNs + "customWorkbookViews") is not null ||
                HasOfficeRevisionAttributes(workbookXml.Root))
            {
                return false;
            }

            foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry))
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is null ||
                    root.Element(workbookNs + "customSheetViews") is not null ||
                    root.Element(workbookNs + "customProperties") is not null ||
                    root.Element(workbookNs + "drawing") is not null ||
                    HasWorksheetTableParts(root, workbookNs) ||
                    HasOfficeRevisionAttributes(root))
                {
                    return false;
                }
            }

            return !HasUnsupportedRichSharedStringFonts(archive, workbookNs);
        }

        private static bool IsPatchUnsafePackagePart(string path) =>
            path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/revisionHeaders/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("xl/revisions/", StringComparison.OrdinalIgnoreCase);

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

        private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
        {
            var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
            return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                   path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasOfficeRevisionAttributes(XElement root) =>
            root.DescendantsAndSelf()
                .SelectMany(element => element.Attributes())
                .Any(attribute =>
                    string.Equals(attribute.Name.LocalName, "uid", StringComparison.Ordinal) &&
                    attribute.Name.NamespaceName.Contains("/revision", StringComparison.Ordinal));

        private static bool HasWorksheetTableParts(XElement worksheetRoot, XNamespace workbookNs)
        {
            var tableParts = worksheetRoot.Element(workbookNs + "tableParts");
            if (tableParts is null)
                return false;

            return tableParts.Elements(workbookNs + "tablePart").Any() ||
                   !string.Equals(tableParts.Attribute("count")?.Value, "0", StringComparison.Ordinal);
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
        private readonly IReadOnlyDictionary<StyleId, string?> _sourceStyleIndexesByStyleId;
        private readonly string _modelFingerprint;

        private XlsxCellPatchBaseline(
            IReadOnlyList<XlsxWorksheetCellPatchBaseline> worksheets,
            IReadOnlyDictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            string modelFingerprint)
        {
            _worksheets = worksheets;
            _sourceStyleIndexesByStyleId = sourceStyleIndexesByStyleId;
            _modelFingerprint = modelFingerprint;
        }

        public static XlsxCellPatchBaseline? TryCreate(
            byte[] package,
            int offset,
            int count,
            Workbook workbook,
            int cellLimit,
            IReadOnlyDictionary<string, SheetXmlLayout>? sheetXmlLayout = null)
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
                var sourceStyleIndexesByStyleId = new Dictionary<StyleId, string?>();
                var ambiguousSourceStyleIds = new HashSet<StyleId>();
                sourceStyleIndexesByStyleId[StyleId.Default] = null;
                foreach (var sheet in workbook.Sheets)
                {
                    if (!worksheetPathMap.SheetPathsByName.TryGetValue(sheet.Name, out var worksheetPath))
                        return null;

                    var sourceCellStyleIndexes =
                        sheetXmlLayout is not null &&
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
                    if (sourceCellStyleIndexes is null)
                        return null;

                    var cells = new Dictionary<(uint Row, uint Col), XlsxPatchCell>(sheet.CellCount);
                    foreach (var ((row, col), cell) in sheet.GetOccupiedCellMap())
                    {
                        var hasExplicitSourceStyleIndex = sourceCellStyleIndexes.TryGetValue((row, col), out var sourceStyleIndex);
                        if (cell.StyleId == StyleId.Default || hasExplicitSourceStyleIndex)
                        {
                            AddSourceStyleIndex(
                                sourceStyleIndexesByStyleId,
                                ambiguousSourceStyleIds,
                                cell.StyleId,
                                sourceStyleIndex);
                        }

                        cells[(row, col)] = new XlsxPatchCell(
                            cell.Value,
                            cell.FormulaText,
                            cell.StyleId,
                            sourceStyleIndex,
                            cell.IgnoreFormulaError);
                    }

                    worksheets.Add(new XlsxWorksheetCellPatchBaseline(
                        sheet.Id,
                        sheet.Name,
                        worksheetPath,
                        sheet.CellCount,
                        sheet.StyleOnlyCellCount,
                        XlsxWorksheetDimensionBaseline.Capture(sheet),
                        cells));
                }

                return new XlsxCellPatchBaseline(
                    worksheets,
                    sourceStyleIndexesByStyleId,
                    CreateSourceModelFingerprint(workbook));
            }
            catch
            {
                return null;
            }
        }

        public bool TryGetPatchableValueChanges(
            Workbook workbook,
            int changeLimit,
            string? currentModelFingerprint,
            out List<XlsxCellValuePatch> changes,
            out List<XlsxWorksheetDimensionPatch> dimensionChanges)
        {
            changes = [];
            dimensionChanges = [];
            if (workbook.SheetCount != _worksheets.Count)
                return false;

            for (var sheetIndex = 0; sheetIndex < _worksheets.Count; sheetIndex++)
            {
                var baseline = _worksheets[sheetIndex];
                var sheet = workbook.Sheets[sheetIndex];
                if (sheet.Id != baseline.SheetId ||
                    !string.Equals(sheet.Name, baseline.SheetName, StringComparison.Ordinal) ||
                    sheet.StyleOnlyCellCount != baseline.StyleOnlyCellCount)
                {
                    return false;
                }

                if (!XlsxWorksheetDimensionPatch.TryCreate(
                        baseline.SheetId,
                        baseline.WorksheetPath,
                        baseline.Dimensions,
                        XlsxWorksheetDimensionBaseline.Capture(sheet),
                        out var dimensionPatch))
                {
                    return false;
                }

                if (dimensionPatch is not null)
                {
                    if (dimensionPatch.ChangeCount > changeLimit)
                        return false;

                    dimensionChanges.Add(dimensionPatch);
                }

                var addedCells = 0;
                var currentCells = sheet.GetOccupiedCellMap();
                foreach (var ((row, col), cell) in currentCells)
                {
                    if (!baseline.Cells.TryGetValue((row, col), out var original))
                    {
                        if (cell.HasFormula ||
                            cell.IgnoreFormulaError ||
                            cell.Value is BlankValue ||
                            !IsPatchableScalarValue(cell.Value) ||
                            !TryGetSourceStyleIndex(cell.StyleId, out var insertedSourceStyleIndex))
                        {
                            return false;
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
                            OriginalStyleId: StyleId.Default,
                            NewStyleId: cell.StyleId,
                            OriginalSourceStyleIndex: null,
                            NewSourceStyleIndex: insertedSourceStyleIndex,
                            OriginalIgnoreFormulaError: false));
                        if (changes.Count > changeLimit)
                            return false;

                        addedCells++;
                        continue;
                    }

                    if (cell.IgnoreFormulaError != original.IgnoreFormulaError)
                    {
                        return false;
                    }

                    var styleChanged = cell.StyleId != original.StyleId;
                    var newSourceStyleIndex = original.SourceStyleIndex;
                    if (styleChanged && !TryGetSourceStyleIndex(cell.StyleId, out newSourceStyleIndex))
                        return false;

                    var formulaChanged = !string.Equals(cell.FormulaText, original.FormulaText, StringComparison.Ordinal);
                    var valueChanged = !Equals(cell.Value, original.Value);
                    if (!formulaChanged && !valueChanged && !styleChanged)
                        continue;

                    if ((formulaChanged || valueChanged) && !IsPatchableScalarValue(cell.Value))
                    {
                        return false;
                    }

                    XlsxCellValuePatchKind patchKind;
                    if (!formulaChanged && !valueChanged)
                    {
                        patchKind = XlsxCellValuePatchKind.CellStyle;
                    }
                    else if (formulaChanged)
                    {
                        if (string.IsNullOrWhiteSpace(original.FormulaText) ||
                            string.IsNullOrWhiteSpace(cell.FormulaText))
                        {
                            return false;
                        }

                        patchKind = XlsxCellValuePatchKind.FormulaTextAndCachedValue;
                    }
                    else
                    {
                        patchKind = cell.HasFormula
                            ? XlsxCellValuePatchKind.FormulaCachedValue
                            : XlsxCellValuePatchKind.LiteralValue;
                        if (patchKind == XlsxCellValuePatchKind.LiteralValue && original.FormulaText is not null)
                            return false;
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
                        original.StyleId,
                        cell.StyleId,
                        original.SourceStyleIndex,
                        newSourceStyleIndex,
                        original.IgnoreFormulaError));
                    if (changes.Count > changeLimit)
                        return false;
                }

                var deletedCells = 0;
                foreach (var ((row, col), original) in baseline.Cells)
                {
                    if (currentCells.ContainsKey((row, col)))
                        continue;

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
                        original.StyleId,
                        NewStyleId: StyleId.Default,
                        original.SourceStyleIndex,
                        NewSourceStyleIndex: null,
                        original.IgnoreFormulaError));
                    if (changes.Count > changeLimit)
                        return false;

                    deletedCells++;
                }

                if (sheet.CellCount != baseline.CellCount + addedCells - deletedCells)
                    return false;
            }

            return changes.Count == 0 && dimensionChanges.Count == 0 && currentModelFingerprint is not null
                ? string.Equals(_modelFingerprint, currentModelFingerprint, StringComparison.Ordinal)
                : ModelMatchesWithOriginalValues(workbook, changes, dimensionChanges);
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
                if (change.Kind == XlsxCellValuePatchKind.InsertedLiteralValue)
                {
                    if (cell is not null ||
                        !InsertLiteralCell(
                            sheetData,
                            worksheetNs,
                            change.Row,
                            change.Col,
                            change.NewValue,
                            change.NewSourceStyleIndex))
                    {
                        return false;
                    }

                    continue;
                }

                if (cell is null)
                    return false;

                if (change.Kind == XlsxCellValuePatchKind.DeletedCell)
                {
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
                    // Style-only changes intentionally leave cell contents and formulas untouched.
                }
                else
                {
                    RewriteLiteralCellValue(cell, worksheetNs, change.NewValue);
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
            string modelFingerprint)
        {
            if (changes.Count == 0 && dimensionChanges.Count == 0)
                return new XlsxCellPatchBaseline(_worksheets, _sourceStyleIndexesByStyleId, modelFingerprint);

            var changesBySheet = changes
                .GroupBy(change => change.SheetId)
                .ToDictionary(group => group.Key, group => group.ToList());
            var dimensionChangesBySheet = dimensionChanges
                .ToDictionary(change => change.SheetId);
            var worksheets = new List<XlsxWorksheetCellPatchBaseline>(_worksheets.Count);
            foreach (var baseline in _worksheets)
            {
                changesBySheet.TryGetValue(baseline.SheetId, out var sheetChanges);
                dimensionChangesBySheet.TryGetValue(baseline.SheetId, out var dimensionPatch);
                if ((sheetChanges is null || sheetChanges.Count == 0) && dimensionPatch is null)
                {
                    worksheets.Add(baseline);
                    continue;
                }

                var cells = new Dictionary<(uint Row, uint Col), XlsxPatchCell>(baseline.Cells);
                var inserted = 0;
                var deleted = 0;
                foreach (var change in sheetChanges ?? [])
                {
                    var key = (change.Row, change.Col);
                    if (change.Kind == XlsxCellValuePatchKind.InsertedLiteralValue)
                    {
                        cells[key] = new XlsxPatchCell(
                            change.NewValue,
                            null,
                            change.NewStyleId,
                            change.NewSourceStyleIndex,
                            false);
                        inserted++;
                        continue;
                    }

                    if (change.Kind == XlsxCellValuePatchKind.DeletedCell)
                    {
                        if (cells.Remove(key))
                            deleted++;
                        continue;
                    }

                    if (!cells.TryGetValue(key, out var original))
                        continue;

                    cells[key] = original with
                    {
                        Value = change.NewValue,
                        FormulaText = change.Kind == XlsxCellValuePatchKind.FormulaTextAndCachedValue
                            ? change.NewFormulaText
                            : original.FormulaText,
                        StyleId = change.NewStyleId,
                        SourceStyleIndex = change.HasStyleChange
                            ? change.NewSourceStyleIndex
                            : original.SourceStyleIndex
                    };
                }

                worksheets.Add(baseline with
                {
                    CellCount = baseline.CellCount + inserted - deleted,
                    Dimensions = dimensionPatch?.Current ?? baseline.Dimensions,
                    Cells = cells
                });
            }

            return new XlsxCellPatchBaseline(worksheets, _sourceStyleIndexesByStyleId, modelFingerprint);
        }

        private bool ModelMatchesWithOriginalValues(
            Workbook workbook,
            IReadOnlyList<XlsxCellValuePatch> changes,
            IReadOnlyList<XlsxWorksheetDimensionPatch> dimensionChanges)
        {
            var restoredCells = new List<(
                Cell Cell,
                ScalarValue CurrentValue,
                string? CurrentFormulaText,
                StyleId CurrentStyleId,
                bool CurrentIgnoreFormulaError)>(changes.Count);
            var insertedCells = new List<(Sheet Sheet, uint Row, uint Col, Cell CurrentCell)>();
            var deletedCells = new List<(Sheet Sheet, uint Row, uint Col)>();
            var restoredDimensions = new List<(Sheet Sheet, XlsxWorksheetDimensionBaseline Current)>(dimensionChanges.Count);
            try
            {
                foreach (var dimensionChange in dimensionChanges)
                {
                    var sheet = workbook.GetSheet(dimensionChange.SheetId);
                    if (sheet is null)
                        return false;

                    restoredDimensions.Add((sheet, XlsxWorksheetDimensionBaseline.Capture(sheet)));
                    ApplyDimensionBaseline(sheet, dimensionChange.Original);
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
                        changedCell.StyleId,
                        changedCell.IgnoreFormulaError));
                    changedCell.Value = change.OriginalValue;
                    changedCell.FormulaText = change.OriginalFormulaText;
                    changedCell.StyleId = change.OriginalStyleId;
                    changedCell.IgnoreFormulaError = change.OriginalIgnoreFormulaError;
                }

                return string.Equals(
                    CreateSourceModelFingerprint(workbook),
                    _modelFingerprint,
                    StringComparison.Ordinal);
            }
            finally
            {
                foreach (var (cell, currentValue, currentFormulaText, currentStyleId, currentIgnoreFormulaError) in restoredCells)
                {
                    cell.Value = currentValue;
                    cell.FormulaText = currentFormulaText;
                    cell.StyleId = currentStyleId;
                    cell.IgnoreFormulaError = currentIgnoreFormulaError;
                }

                foreach (var (sheet, row, col, currentCell) in insertedCells)
                    sheet.SetCell(new CellAddress(sheet.Id, row, col), currentCell);

                foreach (var (sheet, row, col) in deletedCells)
                    sheet.ClearCell(row, col);

                foreach (var (sheet, current) in restoredDimensions)
                    ApplyDimensionBaseline(sheet, current);
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
            sheet.OutlineSummaryBelow = baseline.OutlineSummaryBelow;
            sheet.OutlineSummaryRight = baseline.OutlineSummaryRight;
            sheet.ShowOutlineSymbols = baseline.ShowOutlineSymbols;
            sheet.ApplyOutlineStyles = baseline.ApplyOutlineStyles;
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

        private static Dictionary<(uint Row, uint Col), string?>? ReadSourceCellStyleIndexes(
            SheetXmlLayout layout,
            Sheet sheet,
            Dictionary<StyleId, string?> sourceStyleIndexesByStyleId,
            HashSet<StyleId> ambiguousStyleIds)
        {
            var result = new Dictionary<(uint Row, uint Col), string?>(Math.Min(sheet.CellCount, layout.ExplicitPopulatedCellStyles.Count));
            var sourceStyleIndexCache = new Dictionary<int, string?>();
            foreach (var (row, col, styleIndex) in layout.ExplicitPopulatedCellStyles)
            {
                if (styleIndex < 0)
                    continue;

                if (sheet.GetCell(row, col) is not { } cell)
                    continue;

                var sourceStyleIndex = GetCachedSourceStyleIndex(sourceStyleIndexCache, styleIndex);
                result[(row, col)] = sourceStyleIndex;
                AddSourceStyleIndex(
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds,
                    cell.StyleId,
                    sourceStyleIndex);
            }

            foreach (var (row, col, styleIndex) in layout.ExplicitStyleOnlyCells)
            {
                if (styleIndex < 0)
                    continue;

                if (sheet.GetStyleOnly(row, col) is not { } styleOnlyStyleId)
                    continue;

                AddSourceStyleIndex(
                    sourceStyleIndexesByStyleId,
                    ambiguousStyleIds,
                    styleOnlyStyleId,
                    GetCachedSourceStyleIndex(sourceStyleIndexCache, styleIndex));
            }

            return result;
        }

        private static string? GetCachedSourceStyleIndex(Dictionary<int, string?> cache, int styleIndex)
        {
            if (cache.TryGetValue(styleIndex, out var sourceStyleIndex))
                return sourceStyleIndex;

            sourceStyleIndex = NormalizeSourceStyleIndex(styleIndex);
            cache[styleIndex] = sourceStyleIndex;
            return sourceStyleIndex;
        }

        private static Dictionary<(uint Row, uint Col), string?>? ReadSourceCellStyleIndexes(
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
                }
            }

            return result;
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

            switch (value)
            {
                case BlankValue:
                    cell.Attribute("t")?.Remove();
                    break;
                case TextValue text:
                    cell.SetAttributeValue("t", "str");
                    cell.Add(new XElement(worksheetNs + "v", text.Value));
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

        private static bool InsertLiteralCell(
            XElement sheetData,
            XNamespace worksheetNs,
            uint row,
            uint col,
            ScalarValue value,
            string? sourceStyleIndex)
        {
            var rowElement = FindOrCreateRow(sheetData, worksheetNs, row);
            if (rowElement is null)
                return false;

            var cellElement = new XElement(worksheetNs + "c", new XAttribute("r", ToReference(row, col)));
            ApplyCellStyle(cellElement, sourceStyleIndex);
            RewriteLiteralCellValue(cellElement, worksheetNs, value);
            InsertCellInColumnOrder(rowElement, worksheetNs, cellElement, col);
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
            foreach (var rowElement in sheetData.Elements(rowName))
            {
                if (!uint.TryParse(rowElement.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber))
                    continue;

                if (rowNumber == row)
                    return rowElement;

                if (rowNumber > row)
                {
                    insertBefore = rowElement;
                    break;
                }
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

            rowElement.Add(cellElement);
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
                return;

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

            var index = 0;
            while (index < reference.Length && char.IsAsciiLetter(reference[index]))
            {
                col = checked((col * 26) + (uint)(char.ToUpperInvariant(reference[index]) - 'A' + 1));
                index++;
            }

            if (col == 0 || index == reference.Length)
                return false;

            var rowSpan = reference.AsSpan(index);
            return uint.TryParse(rowSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out row) && row > 0;
        }

        private static bool TryGetCellColumn(string? reference, out uint col)
        {
            col = 0;
            if (!TryParseCellReference(reference, out _, out col))
                return false;

            return true;
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

    private sealed record XlsxWorksheetCellPatchBaseline(
        SheetId SheetId,
        string SheetName,
        string WorksheetPath,
        int CellCount,
        int StyleOnlyCellCount,
        XlsxWorksheetDimensionBaseline Dimensions,
        IReadOnlyDictionary<(uint Row, uint Col), XlsxPatchCell> Cells);

    private sealed record XlsxPatchCell(
        ScalarValue Value,
        string? FormulaText,
        StyleId StyleId,
        string? SourceStyleIndex,
        bool IgnoreFormulaError);

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
        StyleId OriginalStyleId,
        StyleId NewStyleId,
        string? OriginalSourceStyleIndex,
        string? NewSourceStyleIndex,
        bool OriginalIgnoreFormulaError)
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
