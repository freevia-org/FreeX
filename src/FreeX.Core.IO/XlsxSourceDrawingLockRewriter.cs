using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>Updates lock properties on source-loaded drawing objects preserved verbatim by the writer.</summary>
internal static class XlsxSourceDrawingLockRewriter
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private sealed record LockState(bool Locked, bool PrintObject, bool? Aspect, bool? NoMove, bool? NoResize, IReadOnlyDictionary<string, string>? Other, string? Children);

    internal static void Save(Stream stream, Workbook workbook, XlsxWorkbookWorksheetPathMap? paths)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        foreach (var sheet in workbook.Sheets)
        {
            var names = sheet.Pictures.Select(p => p.IsSourceLoaded ? p.SourceXlsxName : p.Name).Concat(sheet.TextBoxes.Select(t => t.IsSourceLoaded ? t.SourceXlsxName : t.Name))
                .Concat(sheet.DrawingShapes.Select(s => s.IsSourceLoaded ? s.SourceXlsxName : s.Name)).Concat(sheet.Charts.Select(c => c.Name))
                .Where(name => !string.IsNullOrEmpty(name)).GroupBy(name => name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var states = new Dictionary<string, LockState>(StringComparer.Ordinal);
            var changedNames = new HashSet<string>(StringComparer.Ordinal);
            static bool SameAttributes(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right) =>
                (left?.Count ?? 0) == (right?.Count ?? 0) &&
                (left is null || left.All(pair => right is not null && right.TryGetValue(pair.Key, out var value) && pair.Value == value));
            void Add(string? name, bool source, LockState state, bool changed)
            {
                if (!source) return;
                if (string.IsNullOrEmpty(name) || names.GetValueOrDefault(name) != 1)
                {
                    if (changed) throw new InvalidOperationException("Cannot safely save modified locks for an unnamed or duplicate-named source drawing.");
                    return;
                }
                states[name] = state;
                if (changed) changedNames.Add(name);
            }
            foreach (var picture in sheet.Pictures) Add(picture.SourceXlsxName, picture.IsSourceLoaded,
                new(picture.Locked, picture.PrintObject, picture.LockAspectRatio, picture.DrawingNoMove, picture.DrawingNoResize, picture.DrawingLockAttributes, picture.DrawingLockChildrenXml),
                picture.SourceXlsxLocked != picture.Locked || picture.SourceXlsxPrintObject != picture.PrintObject || picture.SourceXlsxAspectLock != picture.LockAspectRatio || picture.SourceXlsxNoMove != picture.DrawingNoMove ||
                picture.SourceXlsxNoResize != picture.DrawingNoResize || !SameAttributes(picture.SourceXlsxLockAttributes, picture.DrawingLockAttributes) || picture.SourceXlsxLockChildrenXml != picture.DrawingLockChildrenXml);
            foreach (var textBox in sheet.TextBoxes) Add(textBox.SourceXlsxName, textBox.IsSourceLoaded,
                new(textBox.Locked, textBox.PrintObject, null, textBox.DrawingNoMove, textBox.DrawingNoResize, textBox.DrawingLockAttributes, textBox.DrawingLockChildrenXml),
                textBox.SourceXlsxLocked != textBox.Locked || textBox.SourceXlsxPrintObject != textBox.PrintObject || textBox.SourceXlsxNoMove != textBox.DrawingNoMove || textBox.SourceXlsxNoResize != textBox.DrawingNoResize ||
                !SameAttributes(textBox.SourceXlsxLockAttributes, textBox.DrawingLockAttributes) || textBox.SourceXlsxLockChildrenXml != textBox.DrawingLockChildrenXml);
            foreach (var shape in sheet.DrawingShapes) Add(shape.SourceXlsxName, shape.IsSourceLoaded,
                new(shape.Locked, shape.PrintObject, null, shape.DrawingNoMove, shape.DrawingNoResize, shape.DrawingLockAttributes, shape.DrawingLockChildrenXml),
                shape.SourceXlsxLocked != shape.Locked || shape.SourceXlsxPrintObject != shape.PrintObject || shape.SourceXlsxNoMove != shape.DrawingNoMove || shape.SourceXlsxNoResize != shape.DrawingNoResize ||
                !SameAttributes(shape.SourceXlsxLockAttributes, shape.DrawingLockAttributes) || shape.SourceXlsxLockChildrenXml != shape.DrawingLockChildrenXml);
            // Chart anchors are regenerated before this pass, unlike the preserved source objects.
            foreach (var chart in sheet.Charts) Add(chart.Name, chart.SourceXlsxLocked.HasValue,
                new(chart.Locked, chart.PrintObject, null, null, null, chart.DrawingLockAttributes, chart.DrawingLockChildrenXml), chart.SourceXlsxLocked != chart.Locked || chart.SourceXlsxPrintObject != chart.PrintObject ||
                !SameAttributes(chart.SourceXlsxLockAttributes, chart.DrawingLockAttributes) || chart.SourceXlsxLockChildrenXml != chart.DrawingLockChildrenXml);
            if (states.Count == 0) continue;
            var worksheetPath = paths?.SheetPathsByName.GetValueOrDefault(sheet.Name);
            if (string.IsNullOrWhiteSpace(worksheetPath))
            {
                if (changedNames.Count != 0) throw new InvalidOperationException("Cannot locate the worksheet for modified source drawing locks.");
                continue;
            }
            var drawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(archive, worksheetPath,
                XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main"), Rel, OpcRelationships.Namespace);
            if (string.IsNullOrWhiteSpace(drawingPath) || archive.GetEntry(drawingPath) is not { } entry)
            {
                if (changedNames.Count != 0) throw new InvalidOperationException("Cannot locate the drawing for modified source drawing locks.");
                continue;
            }
            XDocument document;
            try { document = XlsxPackageXmlEditor.LoadXml(entry); }
            catch (Exception exception) when (exception is System.Xml.XmlException or InvalidDataException)
            {
                if (changedNames.Count != 0) throw new InvalidOperationException("Cannot read modified source drawing locks.", exception);
                continue;
            }
            if (document.Root is null)
            {
                if (changedNames.Count != 0) throw new InvalidOperationException("Cannot read modified source drawing locks.");
                continue;
            }
            var before = document.ToString(SaveOptions.DisableFormatting);
            var operations = new List<(XElement Element, XElement? Anchor, XElement? Properties, string Locks, LockState State)>();
            foreach (var element in document.Descendants().Where(e => e.Name == Xdr + "pic" || e.Name == Xdr + "sp" || e.Name == Xdr + "cxnSp" || e.Name == Xdr + "grpSp" || e.Name == Xdr + "graphicFrame"))
            {
                var (nv, props, locks) = element.Name.LocalName switch
                {
                    "pic" => ("nvPicPr", "cNvPicPr", "picLocks"),
                    "sp" => ("nvSpPr", "cNvSpPr", "spLocks"),
                    "cxnSp" => ("nvCxnSpPr", "cNvCxnSpPr", "cxnSpLocks"),
                    "graphicFrame" => ("nvGraphicFramePr", "cNvGraphicFramePr", "graphicFrameLocks"),
                    _ => ("nvGrpSpPr", "cNvGrpSpPr", "grpSpLocks")
                };
                var nonVisual = element.Element(Xdr + nv);
                var name = nonVisual?.Element(Xdr + "cNvPr")?.Attribute("name")?.Value;
                if (name is null || !states.TryGetValue(name, out var state)) continue;
                var anchor = element.Ancestors().FirstOrDefault(e => e.Name == Xdr + "oneCellAnchor" || e.Name == Xdr + "twoCellAnchor" || e.Name == Xdr + "absoluteAnchor");
                operations.Add((element, anchor, nonVisual?.Element(Xdr + props), locks, state));
            }
            foreach (var name in changedNames)
            {
                var matches = operations.Where(item => item.Element.Elements().FirstOrDefault()?.Element(Xdr + "cNvPr")?.Attribute("name")?.Value == name).ToArray();
                if (matches.Length != 1 || matches[0].Anchor is null || (matches[0].Properties is null && matches[0].Element.Name != Xdr + "grpSp" && matches[0].Element.Name != Xdr + "graphicFrame"))
                    throw new InvalidOperationException("Cannot safely locate the modified source drawing lock.");
            }
            foreach (var group in operations.Where(item => item.Anchor is not null).GroupBy(item => item.Anchor!))
            {
                if (group.Select(item => (item.State.Locked, item.State.PrintObject)).Distinct().Skip(1).Any())
                    throw new InvalidOperationException("Drawing objects sharing one anchor cannot have different sheet-protection or printing flags.");
                var anchor = group.Key;
                var raw = anchor.Element(Xdr + "clientData")?.Attribute("fLocksWithSheet")?.Value;
                var oldLocked = raw is not ("0" or "false");
                var printRaw = anchor.Element(Xdr + "clientData")?.Attribute("fPrintsWithSheet")?.Value;
                var oldPrintObject = printRaw is not ("0" or "false");
                if ((oldLocked == group.First().State.Locked && oldPrintObject == group.First().State.PrintObject) || !anchor.Descendants(Xdr + "grpSp").Any()) continue;
                var modeled = group.Select(item => item.Element).ToHashSet();
                var unmatchedChild = anchor.Descendants().Any(item =>
                    (item.Name == Xdr + "pic" || item.Name == Xdr + "sp" || item.Name == Xdr + "cxnSp" || item.Name == Xdr + "graphicFrame" || item.Name == Xdr + "contentPart") &&
                    !modeled.Contains(item));
                if (unmatchedChild)
                    throw new InvalidOperationException("Cannot change group anchor flags while an unmodeled drawing object shares it.");
            }
            foreach (var (element, anchor, properties, locks, state) in operations)
            {
                if (anchor is not null)
                {
                    var clientData = anchor.Element(Xdr + "clientData");
                    if (clientData is null) { clientData = new XElement(Xdr + "clientData"); anchor.Add(clientData); }
                    var raw = clientData.Attribute("fLocksWithSheet")?.Value;
                    var oldLocked = raw is not ("0" or "false");
                    if (oldLocked != state.Locked) clientData.SetAttributeValue("fLocksWithSheet", state.Locked ? null : "0");
                    var printRaw = clientData.Attribute("fPrintsWithSheet")?.Value;
                    var oldPrintObject = printRaw is not ("0" or "false");
                    if (oldPrintObject != state.PrintObject) clientData.SetAttributeValue("fPrintsWithSheet", state.PrintObject ? null : "0");
                }
                if (properties is null || element.Name == Xdr + "grpSp") continue;
                var lockElement = properties.Element(Drawing + locks);
                if (lockElement is null && (state.Aspect == true || state.NoMove is not null || state.NoResize is not null || state.Other is { Count: > 0 } || state.Children is not null))
                {
                    lockElement = new XElement(Drawing + locks);
                    properties.AddFirst(lockElement);
                }
                if (lockElement is null) continue;
                if (state.Aspect is { } aspect) lockElement.SetAttributeValue("noChangeAspect", aspect ? "1" : null);
                if (element.Name != Xdr + "graphicFrame")
                {
                    lockElement.SetAttributeValue("noMove", state.NoMove is { } noMove ? noMove ? "1" : "0" : null);
                    lockElement.SetAttributeValue("noResize", state.NoResize is { } noResize ? noResize ? "1" : "0" : null);
                }
                var preservedChildren = state.Children is null ? Array.Empty<XElement>() : XElement.Parse(state.Children).Elements().Select(child => new XElement(child)).ToArray();
                lockElement.RemoveNodes();
                lockElement.Add(preservedChildren);
                var other = state.Other ?? new Dictionary<string, string>();
                foreach (var attribute in lockElement.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration &&
                    (element.Name == Xdr + "graphicFrame" || (attribute.Name != "noMove" && attribute.Name != "noResize")) && (state.Aspect is null || attribute.Name != "noChangeAspect")).ToArray())
                    if (!other.ContainsKey(attribute.Name.ToString())) attribute.Remove();
                foreach (var pair in other)
                    if ((element.Name == Xdr + "graphicFrame" || pair.Key is not ("noMove" or "noResize")) && (state.Aspect is null || pair.Key != "noChangeAspect"))
                        lockElement.SetAttributeValue(XName.Get(pair.Key), pair.Value);
            }
            if (before != document.ToString(SaveOptions.DisableFormatting)) XlsxPackageXmlEditor.ReplaceXml(archive, drawingPath, document);
        }
    }
}
