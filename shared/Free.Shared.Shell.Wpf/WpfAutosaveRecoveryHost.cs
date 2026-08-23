using System.Globalization;
using System.Windows;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Wpf;

public sealed record WpfAutosaveRecoveryMessages(
    string Title,
    string NoCandidatesMessage,
    string FailureMessageFormat);

/// <summary>
/// WPF prompt and host policy for startup and user-invoked autosave recovery.
/// Product hosts retain their recovery plans, offer text, dirty gates, and restore callbacks.
/// </summary>
public static class WpfAutosaveRecoveryHost
{
    public static bool OfferStartup<TPlan>(
        Window? owner,
        WpfAutosaveRecoveryMessages messages,
        Func<bool> currentWindowHasExplicitDocument,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery)
        where TPlan : IAutosaveRecoveryPlan =>
        OfferStartup(
            owner,
            messages,
            currentWindowHasExplicitDocument,
            planRecoveries,
            createPrompt,
            completeRecovery,
            WpfAutosaveRecoveryDialogs.Instance);

    internal static bool OfferStartup<TPlan>(
        Window? owner,
        WpfAutosaveRecoveryMessages messages,
        Func<bool> currentWindowHasExplicitDocument,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery,
        IWpfAutosaveRecoveryDialogs dialogs)
        where TPlan : IAutosaveRecoveryPlan
    {
        Validate(messages, currentWindowHasExplicitDocument, planRecoveries, createPrompt, completeRecovery, dialogs);

        try
        {
            var hasExplicitDocument = currentWindowHasExplicitDocument();
            var result = AutosaveRecoveryWorkflow.RunAsync(
                    planRecoveries(),
                    AutosaveRecoveryPromptMode.Startup,
                    (recovery, remainingCount) => createPrompt(recovery, remainingCount),
                    prompt => new ValueTask<bool>(dialogs.AskStartup(owner, prompt, messages.Title)),
                    (recovery, useCurrentWindow) => new ValueTask<bool>(completeRecovery(
                        recovery,
                        useCurrentWindow && !hasExplicitDocument)))
                .GetAwaiter()
                .GetResult();

            return result.AnyAccepted;
        }
        catch
        {
            // Startup recovery is best-effort and must never block opening the application.
            return false;
        }
    }

    public static bool RecoverManually<TPlan>(
        Window? owner,
        WpfAutosaveRecoveryMessages messages,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery)
        where TPlan : IAutosaveRecoveryPlan =>
        RecoverManually(
            owner,
            messages,
            planRecoveries,
            createPrompt,
            completeRecovery,
            WpfAutosaveRecoveryDialogs.Instance);

    internal static bool RecoverManually<TPlan>(
        Window? owner,
        WpfAutosaveRecoveryMessages messages,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery,
        IWpfAutosaveRecoveryDialogs dialogs)
        where TPlan : IAutosaveRecoveryPlan
    {
        Validate(messages, planRecoveries, createPrompt, completeRecovery, dialogs);

        try
        {
            var recoveries = planRecoveries();
            if (recoveries.Count == 0)
            {
                dialogs.ShowNoCandidates(owner, messages.NoCandidatesMessage, messages.Title);
                return false;
            }

            var result = AutosaveRecoveryWorkflow.RunAsync(
                    recoveries,
                    AutosaveRecoveryPromptMode.Manual,
                    (recovery, remainingCount) => createPrompt(recovery, remainingCount),
                    prompt => new ValueTask<bool>(dialogs.AskManual(owner, prompt, messages.Title)),
                    (recovery, useCurrentWindow) =>
                        new ValueTask<bool>(completeRecovery(recovery, useCurrentWindow)))
                .GetAwaiter()
                .GetResult();

            return result.AnyRecovered;
        }
        catch (Exception ex)
        {
            dialogs.ShowFailure(
                owner,
                string.Format(CultureInfo.CurrentCulture, messages.FailureMessageFormat, ex.Message),
                messages.Title);
            return false;
        }
    }

    private static void Validate<TPlan>(
        WpfAutosaveRecoveryMessages messages,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery,
        IWpfAutosaveRecoveryDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(planRecoveries);
        ArgumentNullException.ThrowIfNull(createPrompt);
        ArgumentNullException.ThrowIfNull(completeRecovery);
        ArgumentNullException.ThrowIfNull(dialogs);
    }

    private static void Validate<TPlan>(
        WpfAutosaveRecoveryMessages messages,
        Func<bool> currentWindowHasExplicitDocument,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, string> createPrompt,
        Func<TPlan, bool, bool> completeRecovery,
        IWpfAutosaveRecoveryDialogs dialogs)
    {
        ArgumentNullException.ThrowIfNull(currentWindowHasExplicitDocument);
        Validate(messages, planRecoveries, createPrompt, completeRecovery, dialogs);
    }
}

internal interface IWpfAutosaveRecoveryDialogs
{
    bool AskStartup(Window? owner, string prompt, string title);

    bool AskManual(Window? owner, string prompt, string title);

    void ShowNoCandidates(Window? owner, string message, string title);

    void ShowFailure(Window? owner, string message, string title);
}

internal sealed class WpfAutosaveRecoveryDialogs : IWpfAutosaveRecoveryDialogs
{
    public static WpfAutosaveRecoveryDialogs Instance { get; } = new();

    private WpfAutosaveRecoveryDialogs()
    {
    }

    public bool AskStartup(Window? owner, string prompt, string title) =>
        DialogMessageHelper.AskYesNo(owner, prompt, title);

    public bool AskManual(Window? owner, string prompt, string title) =>
        DialogMessageHelper.ShowMessage(
            owner,
            prompt,
            title,
            UserMessageButtons.OkCancel,
            UserMessageIcon.Question) == UserMessageResult.Ok;

    public void ShowNoCandidates(Window? owner, string message, string title) =>
        DialogMessageHelper.ShowInfo(owner, message, title);

    public void ShowFailure(Window? owner, string message, string title) =>
        DialogMessageHelper.ShowError(owner, message, title);
}
