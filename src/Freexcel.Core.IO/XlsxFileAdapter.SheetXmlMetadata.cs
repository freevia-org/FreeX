using System.Globalization;
using System.Xml.Linq;

using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static WorksheetDimensionMetadataModel? ReadWorksheetDimensionMetadata(XElement? dimension)
    {
        if (dimension is null)
            return null;

        var model = new WorksheetDimensionMetadataModel();
        foreach (var attribute in dimension.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || string.Equals(attribute.Name.LocalName, "ref", StringComparison.Ordinal))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 ? null : model;
    }

    private static WorksheetSheetPropertiesMetadataModel? ReadWorksheetSheetPropertiesMetadata(XElement? sheetProperties)
    {
        if (sheetProperties is null)
            return null;

        var model = new WorksheetSheetPropertiesMetadataModel
        {
            NativeChildXmls = sheetProperties.Elements()
                .Where(element => !IsModeledSheetPropertiesElement(element.Name.LocalName))
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in sheetProperties.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledSheetPropertiesAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledSheetPropertiesAttribute(string name) =>
        name is "codeName";

    private static bool IsModeledSheetPropertiesElement(string name) =>
        name is "tabColor" or "outlinePr" or "pageSetUpPr";

    private static WorksheetPrimaryViewMetadataModel? ReadWorksheetPrimaryViewMetadata(XElement? sheetView)
    {
        if (sheetView is null)
            return null;

        var model = new WorksheetPrimaryViewMetadataModel
        {
            NativeChildXmls = sheetView.Elements()
                .Where(element => !IsModeledPrimaryViewElement(element.Name.LocalName))
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in sheetView.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledPrimaryViewAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledPrimaryViewAttribute(string name) =>
        name is "workbookViewId" or "view" or "showGridLines" or "showRowColHeaders" or "showRuler" or
            "zoomScale" or "showFormulas" or "topLeftCell";

    private static bool IsModeledPrimaryViewElement(string name) =>
        name is "pane";

    private static WorksheetPageBreaksMetadataModel? ReadWorksheetPageBreaksMetadata(XElement? pageBreaks, uint maxBreakId)
    {
        if (pageBreaks is null)
            return null;

        var model = new WorksheetPageBreaksMetadataModel();
        foreach (var attribute in pageBreaks.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || string.Equals(attribute.Name.LocalName, "count", StringComparison.Ordinal))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        foreach (var breakElement in pageBreaks.Elements().Where(element => string.Equals(element.Name.LocalName, "brk", StringComparison.Ordinal)))
        {
            if (!TryReadPageBreakId(breakElement, maxBreakId, out var id))
                continue;

            var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in breakElement.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || string.Equals(attribute.Name.LocalName, "id", StringComparison.Ordinal))
                    continue;

                attributes[attribute.Name.ToString()] = attribute.Value;
            }

            if (attributes.Count > 0)
                model.BreakNativeAttributes[id] = attributes;
        }

        return model.NativeAttributes.Count == 0 && model.BreakNativeAttributes.Count == 0
            ? null
            : model;
    }

    private static bool TryReadPageBreakId(XElement breakElement, uint maxBreakId, out uint id)
    {
        id = 0;
        return uint.TryParse(breakElement.Attribute("id")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
            id >= 2 &&
            id <= maxBreakId;
    }

    private static WorksheetHeaderFooterMetadataModel? ReadWorksheetHeaderFooterMetadata(XElement? headerFooter)
    {
        if (headerFooter is null)
            return null;

        var model = new WorksheetHeaderFooterMetadataModel();
        foreach (var attribute in headerFooter.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledHeaderFooterAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        model.NativeChildXmls = headerFooter.Elements()
            .Where(element => !IsModeledHeaderFooterElement(element.Name.LocalName))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledHeaderFooterAttribute(string name) =>
        name is "differentOddEven" or "differentFirst" or "scaleWithDoc" or "alignWithMargins";

    private static bool IsModeledHeaderFooterElement(string name) =>
        name is "oddHeader" or "oddFooter" or "evenHeader" or "evenFooter" or "firstHeader" or "firstFooter";

    private static WorksheetPageMarginsMetadataModel? ReadWorksheetPageMarginsMetadata(XElement? pageMargins)
    {
        if (pageMargins is null)
            return null;

        var model = new WorksheetPageMarginsMetadataModel
        {
            NativeChildXmls = pageMargins.Elements()
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in pageMargins.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledPageMarginsAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledPageMarginsAttribute(string name) =>
        name is "left" or "right" or "top" or "bottom" or "header" or "footer";

    private static WorksheetSheetFormatMetadataModel? ReadWorksheetSheetFormatMetadata(XElement? sheetFormatProperties)
    {
        if (sheetFormatProperties is null)
            return null;

        var model = new WorksheetSheetFormatMetadataModel
        {
            NativeChildXmls = sheetFormatProperties.Elements()
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in sheetFormatProperties.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledSheetFormatAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledSheetFormatAttribute(string name) =>
        name is "defaultColWidth" or "defaultRowHeight";

    private static WorksheetPrintOptionsMetadataModel? ReadWorksheetPrintOptionsMetadata(XElement? printOptions)
    {
        if (printOptions is null)
            return null;

        var model = new WorksheetPrintOptionsMetadataModel
        {
            NativeChildXmls = printOptions.Elements()
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in printOptions.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledPrintOptionsAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledPrintOptionsAttribute(string name) =>
        name is "gridLines" or "headings" or "horizontalCentered" or "verticalCentered";

    private static WorksheetPageSetupMetadataModel? ReadWorksheetPageSetupMetadata(XElement? pageSetup)
    {
        if (pageSetup is null)
            return null;

        var model = new WorksheetPageSetupMetadataModel
        {
            NativeChildXmls = pageSetup.Elements()
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in pageSetup.Attributes())
        {
            if (attribute.IsNamespaceDeclaration || IsModeledPageSetupAttribute(attribute.Name.LocalName))
                continue;

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }

    private static bool IsModeledPageSetupAttribute(string name) =>
        name is "paperSize" or "scale" or "firstPageNumber" or "fitToWidth" or "fitToHeight" or
            "pageOrder" or "orientation" or "usePrinterDefaults" or "blackAndWhite" or "draft" or
            "cellComments" or "useFirstPageNumber" or "errors" or "horizontalDpi" or "verticalDpi" or
            "copies";

    private static WorksheetProtectionMetadataModel? ReadWorksheetProtectionMetadata(XElement? protection)
    {
        if (protection is null)
            return null;

        var model = new WorksheetProtectionMetadataModel
        {
            NativeChildXmls = protection.Elements()
                .Select(element => element.ToString(SaveOptions.DisableFormatting))
                .ToList()
        };

        foreach (var attribute in protection.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                string.Equals(attribute.Name.LocalName, "sheet", StringComparison.Ordinal) ||
                string.Equals(attribute.Name.LocalName, "password", StringComparison.Ordinal))
            {
                continue;
            }

            model.NativeAttributes[attribute.Name.ToString()] = attribute.Value;
        }

        return model.NativeAttributes.Count == 0 && model.NativeChildXmls.Count == 0
            ? null
            : model;
    }
}
