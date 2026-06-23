using Free.Shared.Shell;

namespace FreeW.App.Presentation.Backstage;

public sealed record BackstageAccountFieldGroup(
    string Heading,
    IReadOnlyList<BackstageFieldRow> Fields);

public sealed record BackstageAccountPanePlan(
    string Description,
    IReadOnlyList<BackstageAccountFieldGroup> Groups,
    string OptionsText);

public static class BackstageAccountPanePlanner
{
    public static BackstageAccountPanePlan Build(
        string productName,
        string version,
        string userName,
        string machineName,
        string dataFolder)
    {
        return new BackstageAccountPanePlan(
            Description: "View local product and user information for this FreeW installation.",
            Groups:
            [
                new("Product Information",
                [
                    new("Product", ValueOrFallback(productName)),
                    new("Version", ValueOrFallback(version)),
                    new("Device", ValueOrFallback(machineName)),
                ]),
                new("User Information",
                [
                    new("Windows user", ValueOrFallback(userName)),
                    new("Data folder", ValueOrFallback(dataFolder)),
                    new("Connected services", "Local desktop app"),
                ]),
            ],
            OptionsText: "FreeW Options...");
    }

    private static string ValueOrFallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not available" : value.Trim();
}
