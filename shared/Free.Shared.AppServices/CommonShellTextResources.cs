namespace Free.Shared.AppServices;

public static class CommonShellResourceKeys
{
    public const string Location = "Common_Location";
    public const string NotSavedYet = "Common_NotSavedYet";
    public const string Properties = "Common_Properties";
    public const string Statistics = "Common_Statistics";
    public const string UnsavedChangesSuffix = "Common_UnsavedChangesSuffix";
    public const string Title = "Common_Title";
    public const string Author = "Common_Author";
    public const string Subject = "Common_Subject";
    public const string Keywords = "Common_Keywords";
    public const string EmptyValue = "Common_EmptyValue";
    public const string RecentFilesKept = "Common_RecentFilesKept";
    public const string DefaultSaveFormat = "Common_DefaultSaveFormat";
    public const string UiLanguage = "Common_UiLanguage";
    public const string DataFolder = "Common_DataFolder";
    public const string SystemDefault = "Common_SystemDefault";
    public const string FindReplaceSearchTermRequired = "Common_FindReplace_SearchTermRequired";
    public const string FindReplaceNoMatches = "Common_FindReplace_NoMatchesFound";
    public const string FindReplaceNotFoundFormat = "Common_FindReplace_NotFoundFormat";
    public const string FindReplaceMatchFormat = "Common_FindReplace_MatchStatusFormat";
}

/// <summary>Context-neutral shell text shared by the sister applications.</summary>
public static class CommonShellTextResources
{
    public static ResourceTextDescriptor Location { get; } = Text(CommonShellResourceKeys.Location, "Location");
    public static ResourceTextDescriptor NotSavedYet { get; } = Text(CommonShellResourceKeys.NotSavedYet, "Not saved yet");
    public static ResourceTextDescriptor Properties { get; } = Text(CommonShellResourceKeys.Properties, "Properties");
    public static ResourceTextDescriptor Statistics { get; } = Text(CommonShellResourceKeys.Statistics, "Statistics");
    public static ResourceTextDescriptor UnsavedChangesSuffix { get; } = Text(CommonShellResourceKeys.UnsavedChangesSuffix, "  (unsaved changes)");
    public static ResourceTextDescriptor Title { get; } = Text(CommonShellResourceKeys.Title, "Title");
    public static ResourceTextDescriptor Author { get; } = Text(CommonShellResourceKeys.Author, "Author");
    public static ResourceTextDescriptor Subject { get; } = Text(CommonShellResourceKeys.Subject, "Subject");
    public static ResourceTextDescriptor Keywords { get; } = Text(CommonShellResourceKeys.Keywords, "Keywords");
    public static ResourceTextDescriptor EmptyValue { get; } = Text(CommonShellResourceKeys.EmptyValue, "\u2014");
    public static ResourceTextDescriptor RecentFilesKept { get; } = Text(CommonShellResourceKeys.RecentFilesKept, "Recent files kept");
    public static ResourceTextDescriptor DefaultSaveFormat { get; } = Text(CommonShellResourceKeys.DefaultSaveFormat, "Default save format");
    public static ResourceTextDescriptor UiLanguage { get; } = Text(CommonShellResourceKeys.UiLanguage, "UI language");
    public static ResourceTextDescriptor DataFolder { get; } = Text(CommonShellResourceKeys.DataFolder, "Data folder");
    public static ResourceTextDescriptor SystemDefault { get; } = Text(CommonShellResourceKeys.SystemDefault, "System default");
    public static ResourceTextDescriptor FindReplaceSearchTermRequired { get; } = Text(CommonShellResourceKeys.FindReplaceSearchTermRequired, "Enter a search term.");
    public static ResourceTextDescriptor FindReplaceNoMatches { get; } = Text(CommonShellResourceKeys.FindReplaceNoMatches, "No matches found.");
    public static ResourceTextDescriptor FindReplaceNotFoundFormat { get; } = Text(CommonShellResourceKeys.FindReplaceNotFoundFormat, "\"{0}\" not found.");
    public static ResourceTextDescriptor FindReplaceMatchFormat { get; } = Text(CommonShellResourceKeys.FindReplaceMatchFormat, "Match {0} of {1}");

    private static ResourceTextDescriptor Text(string key, string fallbackText) => new(key, fallbackText);
}
