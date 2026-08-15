using Free.Shared.AppServices;

namespace Free.Shared.Shell;

public sealed record BackstageCorePropertiesTextSpec(
    string TitleLabel,
    string AuthorLabel,
    string SubjectLabel,
    string KeywordsLabel,
    string EmptyValue)
{
    public static BackstageCorePropertiesTextSpec NeutralEnglish { get; } =
        new("Title", "Author", "Subject", "Keywords", "\u2014");

    public static BackstageCorePropertiesTextSpec FromDescriptor(
        SisterBackstageCorePropertiesTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new BackstageCorePropertiesTextSpec(
            descriptor.TitleLabel.Resolve(getText),
            descriptor.AuthorLabel.Resolve(getText),
            descriptor.SubjectLabel.Resolve(getText),
            descriptor.KeywordsLabel.Resolve(getText),
            descriptor.EmptyValue.Resolve(getText));
    }
}

public sealed record BackstageInfoPaneTextSpec(
    string Heading,
    string LocationLabel,
    string NotSavedYet,
    string PropertiesHeading,
    string StatisticsHeading,
    string DirtySuffix,
    BackstageCorePropertiesTextSpec CoreProperties)
{
    public static BackstageInfoPaneTextSpec NeutralEnglish { get; } = new(
        "Info",
        "Location",
        "Not saved yet",
        "Properties",
        "Statistics",
        "  (unsaved changes)",
        BackstageCorePropertiesTextSpec.NeutralEnglish);

    public static BackstageInfoPaneTextSpec FromDescriptor(
        SisterBackstageInfoPaneTextDescriptor descriptor,
        Func<string, string?>? getText = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new BackstageInfoPaneTextSpec(
            descriptor.Heading.Resolve(getText),
            descriptor.LocationLabel.Resolve(getText),
            descriptor.NotSavedYet.Resolve(getText),
            descriptor.PropertiesHeading.Resolve(getText),
            descriptor.StatisticsHeading.Resolve(getText),
            descriptor.DirtySuffix.Resolve(getText),
            BackstageCorePropertiesTextSpec.FromDescriptor(descriptor.CoreProperties, getText));
    }
}

/// <summary>Shared labels used by the WPF-authority Info pane and its Avalonia peer.</summary>
public static class BackstageInfoPaneText
{
    public const string Title = "Info";
    public const string LocationLabel = "Location";
    public const string PropertiesHeading = "Properties";
    public const string NotSavedYet = "Not saved yet";
    public const string StatisticsHeading = "Statistics";
}
