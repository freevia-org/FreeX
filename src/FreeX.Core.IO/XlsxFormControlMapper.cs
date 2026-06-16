using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads legacy Excel form-control state (the <c>formControlPr</c> element stored in
/// <c>xl/ctrlProps/ctrlPropN.xml</c>) into a <see cref="FormControlModel"/> so that form
/// controls are no longer silently dropped on load. The underlying VML/ctrlProps package
/// parts are round-tripped verbatim by the preservation layer, so this mapper only needs to
/// surface the modeled state (type, checked/value/min/max, linked cell, list fill range).
/// </summary>
internal static class XlsxFormControlMapper
{
    /// <summary>
    /// Parse a <c>formControlPr</c> element (from a ctrlProp part) into a model. Returns
    /// <see langword="null"/> only when the element is null.
    /// </summary>
    public static FormControlModel? ReadControlProperties(XElement? formControlPr)
    {
        if (formControlPr is null)
            return null;

        var model = new FormControlModel
        {
            Kind = MapObjectType(formControlPr.Attribute("objectType")?.Value),
            IsChecked = string.Equals(
                formControlPr.Attribute("checked")?.Value,
                "Checked",
                StringComparison.OrdinalIgnoreCase),
            LinkedCell = NullIfWhiteSpace(formControlPr.Attribute("fmlaLink")?.Value),
            ListFillRange = NullIfWhiteSpace(formControlPr.Attribute("fmlaRange")?.Value),
            Value = ReadInt(formControlPr, "val"),
            Min = ReadInt(formControlPr, "min"),
            Max = ReadInt(formControlPr, "max"),
            Increment = ReadInt(formControlPr, "inc"),
            PageChange = ReadInt(formControlPr, "page"),
            SelectedIndex = ReadInt(formControlPr, "sel"),
        };

        return model;
    }

    private static FormControlKind MapObjectType(string? objectType) => objectType switch
    {
        null => FormControlKind.Unknown,
        _ when objectType.Equals("Button", StringComparison.OrdinalIgnoreCase) => FormControlKind.Button,
        _ when objectType.Equals("CheckBox", StringComparison.OrdinalIgnoreCase) => FormControlKind.CheckBox,
        _ when objectType.Equals("Radio", StringComparison.OrdinalIgnoreCase) => FormControlKind.OptionButton,
        _ when objectType.Equals("Option", StringComparison.OrdinalIgnoreCase) => FormControlKind.OptionButton,
        _ when objectType.Equals("Drop", StringComparison.OrdinalIgnoreCase) => FormControlKind.DropDown,
        _ when objectType.Equals("List", StringComparison.OrdinalIgnoreCase) => FormControlKind.ListBox,
        _ when objectType.Equals("GBox", StringComparison.OrdinalIgnoreCase) => FormControlKind.GroupBox,
        _ when objectType.Equals("Label", StringComparison.OrdinalIgnoreCase) => FormControlKind.Label,
        _ when objectType.Equals("Scroll", StringComparison.OrdinalIgnoreCase) => FormControlKind.ScrollBar,
        _ when objectType.Equals("Spin", StringComparison.OrdinalIgnoreCase) => FormControlKind.Spinner,
        _ => FormControlKind.Unknown,
    };

    private static int? ReadInt(XElement element, string attributeName)
    {
        var value = element.Attribute(attributeName)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
