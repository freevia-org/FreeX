using System.Globalization;
using Avalonia.Controls;
using Free.Shared.AppServices;

namespace Free.Shared.Shell.Avalonia;

public sealed record AvaloniaAutosaveRecoveryMessages(
    string Title,
    string NoCandidatesMessage,
    string FailureMessageFormat);

/// <summary>
/// Avalonia host policy for startup and user-invoked autosave recovery.
/// Product adapters retain their offer text, typed restore callbacks, and recovery sessions.
/// </summary>
public static class AvaloniaAutosaveRecoveryHost
{
    public static async Task<bool> OfferStartupAsync<TPlan, TOffer>(
        Window owner,
        Func<bool> currentWindowHasExplicitDocument,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, TOffer> createOffer,
        Func<TOffer, ValueTask<bool>> promptAsync,
        Func<TPlan, bool> recoverInCurrentWindow,
        Func<TPlan, Task<bool>> recoverInNewWindowAsync,
        Action<TPlan, bool, bool> completeRecoveryResult)
        where TPlan : IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(currentWindowHasExplicitDocument);
        Validate(
            owner,
            planRecoveries,
            createOffer,
            promptAsync,
            recoverInCurrentWindow,
            recoverInNewWindowAsync,
            completeRecoveryResult);

        try
        {
            var hasExplicitDocument = currentWindowHasExplicitDocument();
            var result = await AutosaveRecoveryWorkflow.RunAsync(
                planRecoveries(),
                AutosaveRecoveryPromptMode.StartupQuotedDisplayName,
                createOffer,
                promptAsync,
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow && !hasExplicitDocument)
                        return recoverInCurrentWindow(recovery);

                    var recovered = await recoverInNewWindowAsync(recovery);
                    completeRecoveryResult(recovery, true, recovered);
                    return recovered;
                });

            return result.AnyAccepted;
        }
        catch
        {
            // Startup recovery is best-effort and must never block opening the application.
            return false;
        }
    }

    public static Task<bool> RecoverManuallyAsync<TPlan, TOffer>(
        Window owner,
        AvaloniaAutosaveRecoveryMessages messages,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, TOffer> createOffer,
        Func<TOffer, ValueTask<bool>> promptAsync,
        Func<Task<bool>>? confirmDiscardOrSaveAsync,
        Func<TPlan, bool> recoverInCurrentWindow,
        Func<TPlan, Task<bool>> recoverInNewWindowAsync,
        Action<TPlan, bool, bool> completeRecoveryResult)
        where TPlan : IAutosaveRecoveryPlan =>
        RecoverManuallyAsync(
            owner,
            messages,
            planRecoveries,
            createOffer,
            promptAsync,
            confirmDiscardOrSaveAsync,
            recoverInCurrentWindow,
            recoverInNewWindowAsync,
            completeRecoveryResult,
            AvaloniaAutosaveRecoveryDialogs.Instance);

    internal static async Task<bool> RecoverManuallyAsync<TPlan, TOffer>(
        Window owner,
        AvaloniaAutosaveRecoveryMessages messages,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, TOffer> createOffer,
        Func<TOffer, ValueTask<bool>> promptAsync,
        Func<Task<bool>>? confirmDiscardOrSaveAsync,
        Func<TPlan, bool> recoverInCurrentWindow,
        Func<TPlan, Task<bool>> recoverInNewWindowAsync,
        Action<TPlan, bool, bool> completeRecoveryResult,
        IAvaloniaAutosaveRecoveryDialogs dialogs)
        where TPlan : IAutosaveRecoveryPlan
    {
        ArgumentNullException.ThrowIfNull(messages);
        Validate(
            owner,
            planRecoveries,
            createOffer,
            promptAsync,
            recoverInCurrentWindow,
            recoverInNewWindowAsync,
            completeRecoveryResult);
        ArgumentNullException.ThrowIfNull(dialogs);

        try
        {
            var recoveries = planRecoveries();
            if (recoveries.Count == 0)
            {
                await dialogs.ShowNoCandidatesAsync(owner, messages.NoCandidatesMessage, messages.Title);
                return false;
            }

            var result = await AutosaveRecoveryWorkflow.RunAsync(
                recoveries,
                AutosaveRecoveryPromptMode.Manual,
                createOffer,
                promptAsync,
                async (recovery, useCurrentWindow) =>
                {
                    if (useCurrentWindow)
                    {
                        if (confirmDiscardOrSaveAsync is not null &&
                            !await confirmDiscardOrSaveAsync())
                        {
                            completeRecoveryResult(recovery, false, false);
                            return false;
                        }

                        return recoverInCurrentWindow(recovery);
                    }

                    var recovered = await recoverInNewWindowAsync(recovery);
                    completeRecoveryResult(recovery, true, recovered);
                    return recovered;
                });

            return result.AnyRecovered;
        }
        catch (Exception ex)
        {
            await dialogs.ShowFailureAsync(
                owner,
                string.Format(CultureInfo.CurrentCulture, messages.FailureMessageFormat, ex.Message),
                messages.Title);
            return false;
        }
    }

    private static void Validate<TPlan, TOffer>(
        Window owner,
        Func<IReadOnlyList<TPlan>> planRecoveries,
        Func<TPlan, int, TOffer> createOffer,
        Func<TOffer, ValueTask<bool>> promptAsync,
        Func<TPlan, bool> recoverInCurrentWindow,
        Func<TPlan, Task<bool>> recoverInNewWindowAsync,
        Action<TPlan, bool, bool> completeRecoveryResult)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(planRecoveries);
        ArgumentNullException.ThrowIfNull(createOffer);
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(recoverInCurrentWindow);
        ArgumentNullException.ThrowIfNull(recoverInNewWindowAsync);
        ArgumentNullException.ThrowIfNull(completeRecoveryResult);
    }
}

internal interface IAvaloniaAutosaveRecoveryDialogs
{
    Task ShowNoCandidatesAsync(Window owner, string message, string title);

    Task ShowFailureAsync(Window owner, string message, string title);
}

internal sealed class AvaloniaAutosaveRecoveryDialogs : IAvaloniaAutosaveRecoveryDialogs
{
    public static AvaloniaAutosaveRecoveryDialogs Instance { get; } = new();

    private AvaloniaAutosaveRecoveryDialogs()
    {
    }

    public Task ShowNoCandidatesAsync(Window owner, string message, string title) =>
        AvaloniaUserMessageDialog.ShowAsync(
            owner,
            message,
            title,
            UserMessageIcon.Information);

    public Task ShowFailureAsync(Window owner, string message, string title) =>
        AvaloniaUserMessageDialog.ShowErrorAsync(owner, message, title);
}
