using System.Xml.Linq;

namespace FreeX.Core.Model;

public static class ConditionalFormatNativeMetadata
{
    public static IReadOnlyList<string>? RemoveX14IdNativeChildXmls(IReadOnlyList<string>? nativeChildXmls)
    {
        if (nativeChildXmls is null)
            return null;

        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        var result = new List<string>();
        foreach (var xml in nativeChildXmls)
        {
            var element = TryParseNativeChildXml(xml);
            if (element is null)
            {
                result.Add(xml);
                continue;
            }

            try
            {
                var idExtensions = new List<XElement>();
                foreach (var id in element.Descendants(x14Ns + "id"))
                {
                    var idExtension = id;
                    foreach (var ancestor in id.AncestorsAndSelf())
                    {
                        if (ancestor.Name.LocalName != "ext")
                            continue;

                        idExtension = ancestor;
                        break;
                    }

                    if (!idExtensions.Contains(idExtension))
                        idExtensions.Add(idExtension);
                }

                if (idExtensions.Contains(element))
                    continue;

                foreach (var idExtension in idExtensions)
                    idExtension.Remove();

                if (element.Name.LocalName == "extLst" && !element.Elements().Any())
                    continue;

                result.Add(element.ToString(SaveOptions.DisableFormatting));
            }
            catch
            {
                // Preserve malformed native payloads; the writer already ignores them defensively.
                result.Add(xml);
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static XElement? TryParseNativeChildXml(string xml)
    {
        try
        {
            return XElement.Parse(xml);
        }
        catch
        {
            return null;
        }
    }
}
