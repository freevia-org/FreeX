using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Host;

/// <summary>
/// Host-side localization adapter for the portable AutoFilter criteria catalog. The Presentation layer owns the
/// filter-family descriptors, resource keys, criteria prefixes, and value requirements; Host supplies localized
/// strings and renders the WPF dialog.
/// </summary>
internal static class AutoFilterCriteriaLabels
{
    public static string GetFilterFamilyHeader(AutoFilterMenuFilterKind filterKind) =>
        UiText.Get(AutoFilterMenuCatalog.GetFilterFamilyDescriptor(filterKind).ResourceKey);

    public static IReadOnlyList<AutoFilterCriteriaOption> GetCriteriaOptions(AutoFilterMenuFilterKind filterKind)
    {
        var descriptors = AutoFilterMenuCatalog.GetCriteriaDescriptors(filterKind);
        var options = new List<AutoFilterCriteriaOption>(descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            options.Add(new AutoFilterCriteriaOption(
                UiText.Get(descriptor.ResourceKey),
                descriptor.CriteriaPrefix,
                descriptor.RequiresValue));
        }

        return options;
    }
}
