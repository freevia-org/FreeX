using Free.Shared.Localization;
using FreeX.App.Presentation.Filtering;

namespace FreeX.App.Presentation;

/// <summary>Portable localized-text adapters used by FreeX application planners.</summary>
public sealed class FreeXPlannerTextResources
{
    public FreeXPlannerTextResources(
        Func<string, string> get,
        Func<string, object?[], string> format)
    {
        Text = new ResourceKeyTextResolver(get, format);
        AutoFilter = new AutoFilterMenuTextResolver(Text);
    }

    public ResourceKeyTextResolver Text { get; }

    public AutoFilterMenuTextResolver AutoFilter { get; }
}

public sealed class AutoFilterMenuTextResolver(ResourceKeyTextResolver text) : IAutoFilterMenuTextProvider
{
    public string BlankDisplayText => text.Get("AutoFilter_BlankDisplayText");

    public string Get(string resourceKey) => text.Get(resourceKey);

    public string Format(string resourceKey, string value) => text.Format(resourceKey, value);
}
