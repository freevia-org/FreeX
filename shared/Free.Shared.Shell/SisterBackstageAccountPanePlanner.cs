namespace Free.Shared.Shell;

public sealed record SisterBackstageAccountPaneContext(
    string ProductName,
    string Version,
    string UserName,
    string MachineName,
    string DataFolder);

public sealed record SisterBackstageAccountFieldGroup(
    string Heading,
    IReadOnlyList<BackstageFieldRow> Fields);

public sealed record SisterBackstageAccountPanePlan(
    string Description,
    IReadOnlyList<SisterBackstageAccountFieldGroup> Groups,
    string OptionsText);

/// <summary>
/// Shared local-account policy for sister-app Backstage panes. Hosts provide live identity/storage values;
/// renderers decide how to show the returned rows and where the options command routes.
/// </summary>
public static class SisterBackstageAccountPanePlanner
{
    public static SisterBackstageAccountPanePlan Build(SisterBackstageAccountPaneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var productName = ValueOrFallback(context.ProductName);
        return new SisterBackstageAccountPanePlan(
            Description: $"View local product and user information for this {productName} installation.",
            Groups:
            [
                new("Product Information",
                [
                    new("Product", productName),
                    new("Version", ValueOrFallback(context.Version)),
                    new("Device", ValueOrFallback(context.MachineName)),
                ]),
                new("User Information",
                [
                    new("Windows user", ValueOrFallback(context.UserName)),
                    new("Data folder", ValueOrFallback(context.DataFolder)),
                    new("Connected services", "Local desktop app"),
                ]),
            ],
            OptionsText: productName + " Options...");
    }

    private static string ValueOrFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not available" : value.Trim();
}
