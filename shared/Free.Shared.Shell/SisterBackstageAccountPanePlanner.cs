using System.Globalization;

namespace Free.Shared.Shell;

public sealed record SisterBackstageAccountPaneTextSpec(
    string Heading,
    string DescriptionFormat,
    string ProductInformationHeading,
    string ProductLabel,
    string VersionLabel,
    string DeviceLabel,
    string UserInformationHeading,
    string WindowsUserLabel,
    string DataFolderLabel,
    string ConnectedServicesLabel,
    string ConnectedServicesValue,
    string OptionsTextFormat,
    string MissingValueText)
{
    public static SisterBackstageAccountPaneTextSpec NeutralEnglish { get; } = new(
        Heading: "Account",
        DescriptionFormat: "View local product and user information for this {0} installation.",
        ProductInformationHeading: "Product Information",
        ProductLabel: "Product",
        VersionLabel: "Version",
        DeviceLabel: "Device",
        UserInformationHeading: "User Information",
        WindowsUserLabel: "Windows user",
        DataFolderLabel: "Data folder",
        ConnectedServicesLabel: "Connected services",
        ConnectedServicesValue: "Local desktop app",
        OptionsTextFormat: "{0} Options...",
        MissingValueText: "Not available");

    public string FormatDescription(string productName) =>
        string.Format(CultureInfo.CurrentCulture, DescriptionFormat, productName);

    public string FormatOptionsText(string productName) =>
        string.Format(CultureInfo.CurrentCulture, OptionsTextFormat, productName);
}

public sealed record SisterBackstageAccountPaneContext(
    string ProductName,
    string Version,
    string UserName,
    string MachineName,
    string DataFolder);

/// <summary>
/// Captures local account metadata without forcing renderers to repeat environment exception policy.
/// </summary>
public static class SisterBackstageAccountPaneContextPlanner
{
    public static SisterBackstageAccountPaneContext BuildLocal(
        string productName,
        string version,
        string dataFolder,
        Func<string>? getUserName = null,
        Func<string>? getMachineName = null)
    {
        ArgumentNullException.ThrowIfNull(productName);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(dataFolder);

        return new SisterBackstageAccountPaneContext(
            productName,
            version,
            SafeRead(getUserName ?? (() => Environment.UserName)),
            SafeRead(getMachineName ?? (() => Environment.MachineName)),
            dataFolder);
    }

    private static string SafeRead(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
        catch (PlatformNotSupportedException)
        {
            return string.Empty;
        }
    }
}

public sealed record SisterBackstageAccountFieldGroup(
    string Heading,
    IReadOnlyList<BackstageFieldRow> Fields);

public sealed record SisterBackstageAccountPanePlan(
    string Heading,
    string Description,
    IReadOnlyList<SisterBackstageAccountFieldGroup> Groups,
    string OptionsText);

/// <summary>
/// Shared local-account policy for sister-app Backstage panes. Hosts provide live identity/storage values;
/// renderers decide how to show the returned rows and where the options command routes.
/// </summary>
public static class SisterBackstageAccountPanePlanner
{
    public static SisterBackstageAccountPanePlan Build(SisterBackstageAccountPaneContext context) =>
        Build(context, SisterBackstageAccountPaneTextSpec.NeutralEnglish);

    public static SisterBackstageAccountPanePlan Build(
        SisterBackstageAccountPaneContext context,
        SisterBackstageAccountPaneTextSpec text)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(text);

        var productName = ValueOrFallback(context.ProductName, text);
        return new SisterBackstageAccountPanePlan(
            Heading: text.Heading,
            Description: text.FormatDescription(productName),
            Groups:
            [
                new(text.ProductInformationHeading,
                [
                    new(text.ProductLabel, productName),
                    new(text.VersionLabel, ValueOrFallback(context.Version, text)),
                    new(text.DeviceLabel, ValueOrFallback(context.MachineName, text)),
                ]),
                new(text.UserInformationHeading,
                [
                    new(text.WindowsUserLabel, ValueOrFallback(context.UserName, text)),
                    new(text.DataFolderLabel, ValueOrFallback(context.DataFolder, text)),
                    new(text.ConnectedServicesLabel, text.ConnectedServicesValue),
                ]),
            ],
            OptionsText: text.FormatOptionsText(productName));
    }

    private static string ValueOrFallback(string? value, SisterBackstageAccountPaneTextSpec text) =>
        string.IsNullOrWhiteSpace(value) ? text.MissingValueText : value.Trim();
}
