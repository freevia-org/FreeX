using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetOleControlNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string OleObjectRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject";
    private const string OleObjectContentType = "application/vnd.openxmlformats-officedocument.oleObject";
    private const string DrawingRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string DrawingContentType = "application/vnd.openxmlformats-officedocument.drawing+xml";
    private const string ControlPropertiesRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp";
    private const string LegacyControlPropertiesRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/control";
    private const string ControlPropertiesContentType = "application/vnd.ms-excel.controlproperties+xml";

    private static readonly HashSet<string> NoAttributes = [];

    private static readonly HashSet<string> OleObjectAttributes =
    [
        "progId",
        "dvAspect",
        "link",
        "oleUpdate",
        "autoLoad",
        "shapeId"
    ];

    private static readonly HashSet<string> ControlAttributes =
    [
        "shapeId",
        "name"
    ];

    private static readonly HashSet<string> ControlPropertiesAttributes =
    [
        "locked",
        "defaultSize",
        "print",
        "disabled",
        "recalcAlways",
        "uiObject",
        "autoFill",
        "autoLine",
        "autoPict",
        "macro",
        "altText",
        "linkedCell",
        "listFillRange",
        "cf"
    ];

    private static readonly HashSet<string> ObjectPropertiesAttributes =
    [
        "locked",
        "defaultSize",
        "print",
        "disabled",
        "uiObject",
        "autoFill",
        "autoLine",
        "autoPict",
        "macro",
        "altText",
        "dde"
    ];

    private static readonly HashSet<string> OleUpdateValues =
    [
        "OLEUPDATE_ALWAYS",
        "OLEUPDATE_ONCALL"
    ];

    public static void NormalizeWorksheets(ZipArchive archive)
        => NormalizePackage(archive);

    public static void NormalizePackage(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = NormalizeWorksheetRoot(root);
            changed |= RebindOleObjectRelationships(archive, worksheetEntry.FullName, worksheetXml);
            changed |= RebindControlPropertiesRelationships(archive, worksheetEntry.FullName, worksheetXml);
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        changed |= NormalizeOleObjects(worksheetRoot);
        changed |= NormalizeControls(worksheetRoot);
        return changed;
    }

    private static bool NormalizeOleObjects(XElement worksheetRoot)
    {
        var changed = false;
        var keptOleObjects = false;
        foreach (var oleObjects in worksheetRoot.Elements(WorksheetNs + "oleObjects").ToList())
        {
            if (keptOleObjects)
            {
                oleObjects.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeOleObjectsElement(oleObjects);
            if (!oleObjects.Elements(WorksheetNs + "oleObject").Any())
            {
                oleObjects.Remove();
                changed = true;
                continue;
            }

            keptOleObjects = true;
        }

        return changed;
    }

    private static bool NormalizeControls(XElement worksheetRoot)
    {
        var changed = false;
        var keptControls = false;
        foreach (var controls in worksheetRoot.Elements(WorksheetNs + "controls").ToList())
        {
            if (keptControls)
            {
                controls.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeControlsElement(controls);
            if (!EnumerateControlElements(controls).Any())
            {
                controls.Remove();
                changed = true;
                continue;
            }

            keptControls = true;
        }

        return changed;
    }

    private static bool NormalizeOleObjectsElement(XElement oleObjects)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(oleObjects, NoAttributes, RelNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(oleObjects, WorksheetNs + "oleObject");

        foreach (var oleObject in oleObjects.Elements(WorksheetNs + "oleObject").ToList())
        {
            changed |= NormalizeOleObjectElement(oleObject);
            if (!ShouldRemoveRelationshipBackedElement(oleObject))
                continue;

            oleObject.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeControlsElement(XElement controls)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(controls, NoAttributes, RelNs + "id");
        changed |= RemoveNonControlChildren(controls);

        foreach (var control in EnumerateControlElements(controls).ToList())
        {
            changed |= NormalizeControlElement(control);
            if (!ShouldRemoveRelationshipBackedElement(control))
                continue;

            RemoveControlElement(control);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Prune everything under <c>&lt;controls&gt;</c> that is not a form control. Excel wraps each
    /// individual <c>&lt;control&gt;</c> in its own <c>mc:AlternateContent</c>/<c>mc:Choice</c> (a
    /// valid x14 forward-compatibility shape), so a plain "keep only direct <c>&lt;control&gt;</c>
    /// children" filter would strip every control on the sheet and leave the block empty — after
    /// which <see cref="NormalizeControls"/> deletes the whole block, silently destroying every
    /// legacy form control on the next save. Keep any wrapper that still carries a control.
    /// </summary>
    private static bool RemoveNonControlChildren(XElement controls)
    {
        var changed = false;
        foreach (var child in controls.Elements().ToList())
        {
            if (child.Name == WorksheetNs + "control")
                continue;

            if (child.Name == McNs + "AlternateContent" && EnumerateControlElements(child).Any())
                continue;

            child.Remove();
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Removes a <c>&lt;control&gt;</c> and then any now-empty
    /// <c>mc:Choice</c>/<c>mc:Fallback</c>/<c>mc:AlternateContent</c> ancestor chain it was wrapped
    /// in, so normalization never leaves a hollow wrapper behind. Mirrors
    /// <c>XlsxWorksheetFormControlPreserver.RemoveControlElement</c>; the walk stops at the enclosing
    /// <c>&lt;controls&gt;</c>, which <see cref="NormalizeControls"/> drops separately once no
    /// controls remain.
    /// </summary>
    private static void RemoveControlElement(XElement control)
    {
        var parent = control.Parent;
        control.Remove();

        while (parent is not null &&
               parent.Name.Namespace == McNs &&
               parent.Name.LocalName is "Choice" or "Fallback" or "AlternateContent" &&
               !parent.Elements().Any())
        {
            var grandParent = parent.Parent;
            parent.Remove();
            parent = grandParent;
        }
    }

    /// <summary>
    /// Enumerates <c>&lt;control&gt;</c> elements in document order, descending through
    /// <c>mc:AlternateContent</c>/<c>mc:Choice</c>/<c>mc:Fallback</c> wrappers exactly like
    /// <c>XlsxWorksheetFormControlPreserver.EnumerateControlElements</c> and
    /// <c>XlsxFormControlMapper.EnumerateDescendantsThroughAlternateContent</c>, so the normalizer
    /// sees the same controls the loader and preserver do. Only the first <c>mc:Choice</c> of each
    /// AlternateContent is followed, to avoid double-visiting the equivalent Fallback markup.
    /// </summary>
    private static IEnumerable<XElement> EnumerateControlElements(XElement element)
    {
        foreach (var child in element.Elements())
        {
            if (child.Name == WorksheetNs + "control")
            {
                yield return child;
                continue;
            }

            if (child.Name == McNs + "AlternateContent")
            {
                var preferred = child.Element(McNs + "Choice") ?? child.Element(McNs + "Fallback");
                if (preferred is not null)
                {
                    foreach (var match in EnumerateControlElements(preferred))
                        yield return match;
                }

                continue;
            }

            foreach (var match in EnumerateControlElements(child))
                yield return match;
        }
    }

    private static bool NormalizeOleObjectElement(XElement oleObject)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(oleObject, OleObjectAttributes, RelNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(oleObject, WorksheetNs + "objectPr");
        changed |= NormalizeObjectProperties(oleObject);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "shapeId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "autoLoad", XlsxXmlNormalizationHelpers.NormalizeBoolean);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "oleUpdate", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, OleUpdateValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "progId", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "dvAspect", NormalizeOptionalText);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(oleObject, "link", NormalizeOptionalText);
        return changed;
    }

    private static bool NormalizeObjectProperties(XElement oleObject)
    {
        var changed = false;
        var keptObjectProperties = false;
        foreach (var objectProperties in oleObject.Elements(WorksheetNs + "objectPr").ToList())
        {
            if (keptObjectProperties)
            {
                objectProperties.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(objectProperties, ObjectPropertiesAttributes, RelNs + "id");
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(objectProperties, WorksheetNs + "anchor");
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "locked", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "defaultSize", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "print", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "disabled", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "uiObject", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "autoFill", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "autoLine", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "autoPict", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "dde", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "macro", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(objectProperties, "altText", NormalizeOptionalText);

            if (objectProperties.Attribute(RelNs + "id") is null && !objectProperties.HasAttributes && !objectProperties.HasElements)
            {
                objectProperties.Remove();
                changed = true;
                continue;
            }

            keptObjectProperties = true;
        }

        return changed;
    }

    private static bool NormalizeControlElement(XElement control)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(control, ControlAttributes, RelNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(control, WorksheetNs + "controlPr");
        changed |= NormalizeControlProperties(control);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(control, "shapeId", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(control, "name", NormalizeOptionalText);
        return changed;
    }

    private static bool NormalizeControlProperties(XElement control)
    {
        var changed = false;
        var keptControlProperties = false;
        foreach (var controlProperties in control.Elements(WorksheetNs + "controlPr").ToList())
        {
            if (keptControlProperties)
            {
                controlProperties.Remove();
                changed = true;
                continue;
            }

            changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(controlProperties, ControlPropertiesAttributes, RelNs + "id");
            changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(controlProperties, WorksheetNs + "anchor");
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "locked", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "defaultSize", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "print", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "disabled", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "recalcAlways", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "uiObject", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "autoFill", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "autoLine", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "autoPict", XlsxXmlNormalizationHelpers.NormalizeBoolean);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "macro", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "altText", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "linkedCell", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "listFillRange", NormalizeOptionalText);
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(controlProperties, "cf", NormalizeOptionalText);

            if (controlProperties.Attribute(RelNs + "id") is null && !controlProperties.HasAttributes && !controlProperties.HasElements)
            {
                controlProperties.Remove();
                changed = true;
                continue;
            }

            keptControlProperties = true;
        }

        return changed;
    }

    /// <summary>
    /// An ActiveX <c>&lt;control&gt;</c> always requires its <c>r:id</c> relationship to a
    /// <c>ctrlProp</c> part, so a missing <c>r:id</c> means it's orphaned/invalid and should be
    /// removed. A <c>&lt;oleObject&gt;</c>, however, can legitimately have NO <c>r:id</c> at all
    /// when it is a LINKED object (created via Insert &gt; Object &gt; Create from File &gt; Link
    /// to file): per CT_OleObject, <c>r:id</c> is optional precisely because a linked object has no
    /// embedded <c>xl/embeddings/*.bin</c> part to relate to — its target lives in the <c>link</c>
    /// attribute instead. Only remove such an element when it has NEITHER an embed relationship NOR
    /// a link target (i.e. it is invalid under either interpretation of the schema).
    /// </summary>
    private static bool ShouldRemoveRelationshipBackedElement(XElement element)
    {
        if (element.Attribute("shapeId") is null)
            return true;

        if (element.Attribute(RelNs + "id") is not null)
            return false;

        return string.IsNullOrWhiteSpace(element.Attribute("link")?.Value);
    }

    private static bool RebindOleObjectRelationships(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        var oleObjectsContainer = worksheetXml.Root?.Element(WorksheetNs + "oleObjects");
        var oleObjects = oleObjectsContainer?
            .Elements(WorksheetNs + "oleObject")
            .ToList()
            ?? [];

        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsXml = archive.GetEntry(relationshipsPath) is { } relationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(relationshipsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationshipsChanged = RemoveInvalidPackageRelationships(
            relationshipsXml,
            worksheetPath,
            archive,
            IsOleObjectRelationshipType,
            IsValidOleObjectRelationship);

        var oleObjectRelationships = relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => IsValidOleObjectRelationship(worksheetPath, relationship, archive))
            .ToList()
            ?? [];

        if (oleObjects.Count == 0)
        {
            if (relationshipsChanged)
                XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

            return false;
        }

        var oleObjectParts = archive.Entries
            .Select(XlsxPackagePath.NormalizeEntryPath)
            .Where(IsOleObjectPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextOleObjectPartIndex = 0;
        var worksheetChanged = false;
        foreach (var oleObject in oleObjects.ToList())
        {
            // A pure-link <oleObject link="..."> with no r:id has no embed relationship to
            // rebind by design (see ShouldRemoveRelationshipBackedElement). Leave it untouched
            // rather than treating the absent relationship as "orphaned" and removing it, or
            // stealing an unrelated embedded object's relationship id for it.
            if (oleObject.Attribute(RelNs + "id") is null &&
                !string.IsNullOrWhiteSpace(oleObject.Attribute("link")?.Value))
            {
                continue;
            }

            var relationship = FindUnusedValidPackageRelationship(
                oleObjectRelationships,
                [oleObject.Attribute(RelNs + "id")?.Value],
                usedRelationshipIds);
            if (relationship is null)
                relationship = FindNextUnusedPackageRelationship(oleObjectRelationships, usedRelationshipIds);

            if (relationship is null)
            {
                while (nextOleObjectPartIndex < oleObjectParts.Count &&
                       oleObjectRelationships.Any(candidate =>
                           string.Equals(
                               ResolveRelationshipTarget(worksheetPath, candidate),
                               oleObjectParts[nextOleObjectPartIndex],
                               StringComparison.OrdinalIgnoreCase)))
                {
                    nextOleObjectPartIndex++;
                }

                if (nextOleObjectPartIndex < oleObjectParts.Count)
                {
                    var targetPart = oleObjectParts[nextOleObjectPartIndex++];
                    relationship = AddPackageRelationship(
                        relationshipsXml,
                        worksheetPath,
                        targetPart,
                        OleObjectRelationshipType);
                    oleObjectRelationships.Add(relationship);
                    relationshipsChanged = true;
                }
            }

            if (relationship is null)
            {
                oleObject.Remove();
                worksheetChanged = true;
                continue;
            }

            var reboundId = GetPackageRelationshipId(relationship);
            if (string.IsNullOrWhiteSpace(reboundId))
            {
                oleObject.Remove();
                worksheetChanged = true;
                continue;
            }

            usedRelationshipIds.Add(reboundId);
            worksheetChanged |= SetRelationshipId(oleObject, reboundId);
            EnsureOleObjectContentType(archive, relationship, worksheetPath);
        }

        worksheetChanged |= RebindObjectPropertiesRelationships(
            archive,
            worksheetPath,
            relationshipsXml,
            oleObjectsContainer?.Elements(WorksheetNs + "oleObject").ToList() ?? [],
            ref relationshipsChanged);

        if (oleObjectsContainer is not null && !oleObjectsContainer.Elements(WorksheetNs + "oleObject").Any())
        {
            oleObjectsContainer.Remove();
            worksheetChanged = true;
        }

        if (relationshipsChanged)
            XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

        return worksheetChanged;
    }

    private static bool RebindObjectPropertiesRelationships(
        ZipArchive archive,
        string worksheetPath,
        XDocument relationshipsXml,
        IReadOnlyList<XElement> oleObjects,
        ref bool relationshipsChanged)
    {
        var objectPropertiesElements = oleObjects
            .SelectMany(oleObject => oleObject.Elements(WorksheetNs + "objectPr"))
            .Where(objectProperties => objectProperties.Attribute(RelNs + "id") is not null)
            .ToList();
        if (objectPropertiesElements.Count == 0)
            return false;

        var drawingRelationships = relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => IsValidDrawingRelationship(worksheetPath, relationship, archive))
            .ToList()
            ?? [];
        var drawingParts = archive.Entries
            .Select(XlsxPackagePath.NormalizeEntryPath)
            .Where(IsDrawingPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextDrawingPartIndex = 0;
        var worksheetChanged = false;
        foreach (var objectProperties in objectPropertiesElements)
        {
            var relationshipId = objectProperties.Attribute(RelNs + "id")?.Value;
            var relationship = FindUnusedValidPackageRelationship(
                drawingRelationships,
                [relationshipId],
                usedRelationshipIds);

            if (relationship is null)
            {
                relationshipsChanged |= RemoveInvalidObjectPropertiesRelationship(
                    relationshipsXml,
                    worksheetPath,
                    archive,
                    relationshipId);
                relationship = FindNextUnusedPackageRelationship(drawingRelationships, usedRelationshipIds);
            }

            if (relationship is null)
            {
                while (nextDrawingPartIndex < drawingParts.Count &&
                       drawingRelationships.Any(candidate =>
                           string.Equals(
                               ResolveRelationshipTarget(worksheetPath, candidate),
                               drawingParts[nextDrawingPartIndex],
                               StringComparison.OrdinalIgnoreCase)))
                {
                    nextDrawingPartIndex++;
                }

                if (nextDrawingPartIndex < drawingParts.Count)
                {
                    var targetPart = drawingParts[nextDrawingPartIndex++];
                    relationship = AddPackageRelationship(
                        relationshipsXml,
                        worksheetPath,
                        targetPart,
                        DrawingRelationshipType);
                    drawingRelationships.Add(relationship);
                    relationshipsChanged = true;
                }
            }

            var reboundId = relationship is null ? null : GetPackageRelationshipId(relationship);
            if (string.IsNullOrWhiteSpace(reboundId))
            {
                objectProperties.SetAttributeValue(RelNs + "id", null);
                worksheetChanged = true;
                continue;
            }

            usedRelationshipIds.Add(reboundId);
            worksheetChanged |= SetRelationshipId(objectProperties, reboundId);
            EnsureDrawingContentType(archive, relationship!, worksheetPath);
        }

        return worksheetChanged;
    }

    private static bool RebindControlPropertiesRelationships(
        ZipArchive archive,
        string worksheetPath,
        XDocument worksheetXml)
    {
        // <control> is not necessarily a DIRECT child of <controls>: Excel wraps each one in its own
        // mc:AlternateContent/mc:Choice, so descend through those wrappers (see
        // EnumerateControlElements) rather than silently failing to rebind every control's r:id.
        var controlsContainer = worksheetXml.Root?.Element(WorksheetNs + "controls");
        var controls = controlsContainer is null
            ? null
            : EnumerateControlElements(controlsContainer).ToList();
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsXml = archive.GetEntry(relationshipsPath) is { } relationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(relationshipsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationshipsChanged = RemoveInvalidPackageRelationships(
            relationshipsXml,
            worksheetPath,
            archive,
            IsControlPropertiesRelationshipType,
            IsControlPropertiesRelationship);

        var controlPropertiesRelationships = relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Where(relationship => IsControlPropertiesRelationship(worksheetPath, relationship, archive))
            .ToList()
            ?? [];

        if (controls is null || controls.Count == 0)
        {
            if (relationshipsChanged)
                XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

            return false;
        }

        var controlPropertiesParts = archive.Entries
            .Select(XlsxPackagePath.NormalizeEntryPath)
            .Where(IsControlPropertiesPart)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextControlPropertiesPartIndex = 0;
        var worksheetChanged = false;
        foreach (var control in controls.ToList())
        {
            var relationship = FindUnusedValidPackageRelationship(
                controlPropertiesRelationships,
                EnumerateRelationshipIds(control, WorksheetNs + "controlPr"),
                usedRelationshipIds);
            if (relationship is null)
            {
                relationship = FindNextUnusedPackageRelationship(controlPropertiesRelationships, usedRelationshipIds);
            }

            if (relationship is null)
            {
                while (nextControlPropertiesPartIndex < controlPropertiesParts.Count &&
                       controlPropertiesRelationships.Any(candidate =>
                           string.Equals(
                               ResolveRelationshipTarget(worksheetPath, candidate),
                               controlPropertiesParts[nextControlPropertiesPartIndex],
                               StringComparison.OrdinalIgnoreCase)))
                {
                    nextControlPropertiesPartIndex++;
                }

                if (nextControlPropertiesPartIndex < controlPropertiesParts.Count)
                {
                    var targetPart = controlPropertiesParts[nextControlPropertiesPartIndex++];
                    relationship = AddPackageRelationship(
                        relationshipsXml,
                        worksheetPath,
                        targetPart,
                        ControlPropertiesRelationshipType);
                    controlPropertiesRelationships.Add(relationship);
                    relationshipsChanged = true;
                }
            }

            if (relationship is null)
            {
                RemoveControlElement(control);
                worksheetChanged = true;
                continue;
            }

            var reboundId = GetPackageRelationshipId(relationship);
            if (string.IsNullOrWhiteSpace(reboundId))
            {
                RemoveControlElement(control);
                worksheetChanged = true;
                continue;
            }

            usedRelationshipIds.Add(reboundId);
            worksheetChanged |= SetRelationshipId(control, reboundId);
            foreach (var controlProperties in control.Elements(WorksheetNs + "controlPr"))
                worksheetChanged |= SetRelationshipId(controlProperties, reboundId);
            EnsureControlPropertiesContentType(archive, relationship, worksheetPath);
        }

        if (controlsContainer is not null && !EnumerateControlElements(controlsContainer).Any())
        {
            controlsContainer.Remove();
            worksheetChanged = true;
        }

        if (relationshipsChanged)
            XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);

        return worksheetChanged;
    }

    private static IEnumerable<string?> EnumerateRelationshipIds(XElement element, XName nestedRelationshipElementName)
    {
        yield return element.Attribute(RelNs + "id")?.Value;
        foreach (var nestedElement in element.Elements(nestedRelationshipElementName))
            yield return nestedElement.Attribute(RelNs + "id")?.Value;
    }

    private static XElement? FindPackageRelationshipById(
        IEnumerable<XElement> relationships,
        string? relationshipId)
    {
        if (string.IsNullOrWhiteSpace(relationshipId))
            return null;

        foreach (var relationship in relationships)
        {
            if (PackageRelationshipIdEquals(relationship, relationshipId))
                return relationship;
        }

        return null;
    }

    private static XElement? FindFirstPackageRelationship(IReadOnlyList<XElement> relationships)
    {
        foreach (var relationship in relationships)
            return relationship;

        return null;
    }

    private static bool PackageRelationshipIdEquals(XElement relationship, string relationshipId)
        => string.Equals(GetPackageRelationshipId(relationship), relationshipId, StringComparison.OrdinalIgnoreCase);

    private static string? GetPackageRelationshipId(XElement relationship)
        => relationship.Attribute("Id")?.Value;

    private static XElement? FindUnusedValidPackageRelationship(
        IReadOnlyList<XElement> relationships,
        IEnumerable<string?> relationshipIds,
        ISet<string> usedRelationshipIds)
    {
        foreach (var relationshipId in relationshipIds)
        {
            if (string.IsNullOrWhiteSpace(relationshipId) || usedRelationshipIds.Contains(relationshipId))
                continue;

            var relationship = FindPackageRelationshipById(relationships, relationshipId);
            if (relationship is not null)
                return relationship;
        }

        return null;
    }

    private static XElement? FindNextUnusedPackageRelationship(
        IReadOnlyList<XElement> relationships,
        ISet<string> usedRelationshipIds)
    {
        foreach (var relationship in relationships)
        {
            var relationshipId = GetPackageRelationshipId(relationship);
            if (!string.IsNullOrWhiteSpace(relationshipId) && !usedRelationshipIds.Contains(relationshipId))
                return relationship;
        }

        return null;
    }

    private static XElement AddPackageRelationship(
        XDocument relationshipsXml,
        string worksheetPath,
        string targetPart,
        string relationshipType)
    {
        var root = relationshipsXml.Root;
        if (root is null)
        {
            root = new XElement(PackageRelNs + "Relationships");
            relationshipsXml.Add(root);
        }

        var relationship = new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, PackageRelNs)),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(worksheetPath, targetPart)));
        root.Add(relationship);
        return relationship;
    }

    private static bool SetRelationshipId(XElement element, string relationshipId)
    {
        if (string.Equals(element.Attribute(RelNs + "id")?.Value, relationshipId, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(RelNs + "id", relationshipId);
        return true;
    }

    private static bool IsControlPropertiesRelationship(
        string worksheetPath,
        XElement relationship,
        ZipArchive archive)
    {
        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsControlPropertiesRelationshipType(relationship))
            return false;

        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        return IsControlPropertiesPart(targetPart) &&
               archive.GetEntry(targetPart) is not null;
    }

    private static bool IsOleObjectRelationship(
        string worksheetPath,
        XElement relationship,
        ZipArchive archive)
    {
        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsOleObjectRelationshipType(relationship))
            return false;

        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        return IsOleObjectPart(targetPart) &&
               archive.GetEntry(targetPart) is not null;
    }

    private static bool IsValidDrawingRelationship(
        string worksheetPath,
        XElement relationship,
        ZipArchive archive)
    {
        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!IsDrawingRelationshipType(relationship))
            return false;

        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        return IsDrawingPart(targetPart) &&
               archive.GetEntry(targetPart) is not null;
    }

    private static bool IsValidOleObjectRelationship(
        string worksheetPath,
        XElement relationship,
        ZipArchive archive)
        => IsOleObjectRelationship(worksheetPath, relationship, archive);

    private static bool IsControlPropertiesRelationshipType(XElement relationship)
    {
        var relationshipType = relationship.Attribute("Type")?.Value;
        return string.Equals(relationshipType, ControlPropertiesRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, LegacyControlPropertiesRelationshipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOleObjectRelationshipType(XElement relationship)
        => string.Equals(relationship.Attribute("Type")?.Value, OleObjectRelationshipType, StringComparison.OrdinalIgnoreCase);

    private static bool IsDrawingRelationshipType(XElement relationship)
        => string.Equals(relationship.Attribute("Type")?.Value, DrawingRelationshipType, StringComparison.OrdinalIgnoreCase);

    private static bool IsControlPropertiesPart(string path) =>
        path.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsOleObjectPart(string path) =>
        path.StartsWith("xl/embeddings/", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) &&
        !path.EndsWith("/", StringComparison.Ordinal);

    private static bool IsDrawingPart(string path) =>
        path.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);

    private static bool RemoveInvalidPackageRelationships(
        XDocument relationshipsXml,
        string worksheetPath,
        ZipArchive archive,
        Func<XElement, bool> isOwnedRelationshipType,
        Func<string, XElement, ZipArchive, bool> isValidRelationship)
    {
        var changed = false;
        foreach (var relationship in relationshipsXml.Root?
                     .Elements(PackageRelNs + "Relationship")
                     .Where(isOwnedRelationshipType)
                     .ToList()
                 ?? [])
        {
            if (isValidRelationship(worksheetPath, relationship, archive))
                continue;

            relationship.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveInvalidObjectPropertiesRelationship(
        XDocument relationshipsXml,
        string worksheetPath,
        ZipArchive archive,
        string? relationshipId)
    {
        if (string.IsNullOrWhiteSpace(relationshipId))
            return false;

        var relationship = FindPackageRelationshipById(
            relationshipsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [],
            relationshipId);
        if (relationship is null ||
            !IsDrawingRelationshipType(relationship) ||
            IsValidDrawingRelationship(worksheetPath, relationship, archive))
        {
            return false;
        }

        relationship.Remove();
        return true;
    }

    private static string ResolveRelationshipTarget(string worksheetPath, XElement relationship)
    {
        var target = relationship.Attribute("Target")?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? ""
            : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static void EnsureControlPropertiesContentType(
        ZipArchive archive,
        XElement relationship,
        string worksheetPath)
    {
        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        if (!string.IsNullOrWhiteSpace(targetPart))
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, targetPart, ControlPropertiesContentType);
    }

    private static void EnsureOleObjectContentType(
        ZipArchive archive,
        XElement relationship,
        string worksheetPath)
    {
        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        if (!string.IsNullOrWhiteSpace(targetPart) &&
            targetPart.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, targetPart, OleObjectContentType);
        }
    }

    private static void EnsureDrawingContentType(
        ZipArchive archive,
        XElement relationship,
        string worksheetPath)
    {
        var targetPart = ResolveRelationshipTarget(worksheetPath, relationship);
        if (!string.IsNullOrWhiteSpace(targetPart))
            XlsxPackageXmlEditor.EnsureSpecificContentType(archive, targetPart, DrawingContentType);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

}
