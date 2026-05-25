using System.Xml.Linq;

namespace Freexcel.Core.Model;

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
            try
            {
                var element = XElement.Parse(xml);
                var idExtensions = element
                    .Descendants(x14Ns + "id")
                    .Select(id => id.AncestorsAndSelf().FirstOrDefault(e => e.Name.LocalName == "ext") ?? id)
                    .Distinct()
                    .ToList();

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
}
