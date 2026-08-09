namespace FreeP.App.Compositor;

/// <summary>Native pane visibility queries supplied by a renderer workarea.</summary>
public sealed class PresentationWorkareaPaneEndpoints
{
    public Func<bool>? AltTextVisible { get; init; }
    public Func<bool>? SmartArtTextVisible { get; init; }
}

/// <summary>Native control and service operations supplied by a renderer workarea.</summary>
public sealed class PresentationWorkareaOperationEndpoints
{
    public Action? BeforePresentationReplaced { get; init; }
    public Action<EditingSession>? BindEditor { get; init; }
    public Action? ResetAnimationSession { get; init; }
    public Action? HideTransientPickers { get; init; }
    public Action? BeforeEditorChanged { get; init; }
    public Action? MarkDirty { get; init; }
    public Action? AfterEditorMarkedDirty { get; init; }
    public Action? RefreshCommandStates { get; init; }
    public Action? RefreshSlidePane { get; init; }
    public Action? RefreshCanvas { get; init; }
    public Action? RefreshNotesPane { get; init; }
    public Action<PresentationWorkareaTransition>? RefreshDocumentStatusBeforeReview { get; init; }
    public Action? RefreshReviewWorkflowPlans { get; init; }
    public Action? RefreshSmartArtPane { get; init; }
    public Action? RefreshAnimationPaneAfterEditorChanged { get; init; }
    public Action? RefreshAnimationPaneAfterNavigation { get; init; }
    public Action? RefreshAnimationPaneAfterSelection { get; init; }
    public Action? RefreshAnimationPaneAfterPresentationChanged { get; init; }
    public Action? RefreshSelectionPane { get; init; }
    public Action? RefreshAccessibilityMetadata { get; init; }
    public Action<PresentationWorkareaTransition>? RefreshDocumentStatusAfterReview { get; init; }
    public Action? BeforeCurrentSlideChanged { get; init; }
    public Action? ClearReviewSelection { get; init; }
    public Action? ResetAnimationSelection { get; init; }
    public Action? ClearMediaSelection { get; init; }
    public Action? SyncSlidePaneSelection { get; init; }
    public Action? RefreshSlidePaneChrome { get; init; }
    public Action? RefreshReviewPaneBeforePlans { get; init; }
    public Action? RefreshReviewPaneAfterPlans { get; init; }
    public Action? RefreshVisibleMediaPane { get; init; }
    public Action? RefreshCurrentSlideStatus { get; init; }
    public Action? RefreshAltTextRequest { get; init; }
    public Action? RefreshReadingOrder { get; init; }
    public Action? RefreshAltTextPane { get; init; }
}

/// <summary>Native file, clipboard, dialog, and slide-show commands supplied by a renderer.</summary>
public sealed class PresentationWorkareaNativeCommandEndpoints
{
    public Action? NewPresentation { get; init; }
    public Action? OpenPresentation { get; init; }
    public Action? SavePresentation { get; init; }
    public Action? SavePresentationAs { get; init; }
    public Action? PrintPresentation { get; init; }
    public Action? StartSlideShowFromBeginning { get; init; }
    public Action? StartSlideShowFromCurrentSlide { get; init; }
    public Action? Copy { get; init; }
    public Action? Cut { get; init; }
    public Action? Paste { get; init; }
    public Action? Find { get; init; }
    public Action? Replace { get; init; }
}

/// <summary>
/// Complete native endpoint profile for the portable workarea. Renderers provide delegates only;
/// Presentation owns endpoint classification and argument normalization.
/// </summary>
public sealed class PresentationWorkareaEndpointProfile
{
    public PresentationWorkareaPaneEndpoints Panes { get; init; } = new();
    public PresentationWorkareaOperationEndpoints Operations { get; init; } = new();
    public PresentationWorkareaNativeCommandEndpoints NativeCommands { get; init; } = new();
}

public sealed class PresentationWorkareaEndpoint : IPresentationWorkareaEndpoint
{
    private readonly PresentationWorkareaEndpointProfile _profile;

    public PresentationWorkareaEndpoint(PresentationWorkareaEndpointProfile profile) =>
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));

    public bool IsPaneVisible(PresentationWorkareaPane pane) =>
        PresentationWorkareaEndpointDispatcher.IsPaneVisible(pane, _profile.Panes);

    public void Apply(
        PresentationWorkareaOperation operation,
        PresentationWorkareaContext context) =>
        PresentationWorkareaEndpointDispatcher.Dispatch(operation, context, _profile.Operations);

    public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
    {
        if (!PresentationWorkareaEndpointDispatcher.Dispatch(command, _profile.NativeCommands))
            throw new ArgumentOutOfRangeException(nameof(command), command, null);
    }
}

/// <summary>Exhaustive UI-free routing from portable workarea requests to native delegates.</summary>
public static class PresentationWorkareaEndpointDispatcher
{
    public static bool IsPaneVisible(
        PresentationWorkareaPane pane,
        PresentationWorkareaPaneEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return pane switch
        {
            PresentationWorkareaPane.AltText => endpoints.AltTextVisible?.Invoke() == true,
            PresentationWorkareaPane.SmartArtText => endpoints.SmartArtTextVisible?.Invoke() == true,
            _ => false,
        };
    }

    public static bool Dispatch(
        PresentationWorkareaOperation operation,
        PresentationWorkareaContext context,
        PresentationWorkareaOperationEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(endpoints);

        return operation switch
        {
            PresentationWorkareaOperation.BeforePresentationReplaced => Invoke(endpoints.BeforePresentationReplaced),
            PresentationWorkareaOperation.BindEditor => Invoke(endpoints.BindEditor, context.Snapshot.Editor),
            PresentationWorkareaOperation.ResetAnimationSession => Invoke(endpoints.ResetAnimationSession),
            PresentationWorkareaOperation.HideTransientPickers => Invoke(endpoints.HideTransientPickers),
            PresentationWorkareaOperation.BeforeEditorChanged => Invoke(endpoints.BeforeEditorChanged),
            PresentationWorkareaOperation.MarkDirty => Invoke(endpoints.MarkDirty),
            PresentationWorkareaOperation.AfterEditorMarkedDirty => Invoke(endpoints.AfterEditorMarkedDirty),
            PresentationWorkareaOperation.RefreshCommandStates => Invoke(endpoints.RefreshCommandStates),
            PresentationWorkareaOperation.RefreshSlidePane => Invoke(endpoints.RefreshSlidePane),
            PresentationWorkareaOperation.RefreshCanvas => Invoke(endpoints.RefreshCanvas),
            PresentationWorkareaOperation.RefreshNotesPane => Invoke(endpoints.RefreshNotesPane),
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview =>
                Invoke(endpoints.RefreshDocumentStatusBeforeReview, context.Transition),
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans =>
                Invoke(endpoints.RefreshReviewWorkflowPlans),
            PresentationWorkareaOperation.RefreshSmartArtPane => Invoke(endpoints.RefreshSmartArtPane),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterEditorChanged =>
                Invoke(endpoints.RefreshAnimationPaneAfterEditorChanged),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterNavigation =>
                Invoke(endpoints.RefreshAnimationPaneAfterNavigation),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterSelection =>
                Invoke(endpoints.RefreshAnimationPaneAfterSelection),
            PresentationWorkareaOperation.RefreshAnimationPaneAfterPresentationChanged =>
                Invoke(endpoints.RefreshAnimationPaneAfterPresentationChanged),
            PresentationWorkareaOperation.RefreshSelectionPane => Invoke(endpoints.RefreshSelectionPane),
            PresentationWorkareaOperation.RefreshAccessibilityMetadata =>
                Invoke(endpoints.RefreshAccessibilityMetadata),
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview =>
                Invoke(endpoints.RefreshDocumentStatusAfterReview, context.Transition),
            PresentationWorkareaOperation.BeforeCurrentSlideChanged =>
                Invoke(endpoints.BeforeCurrentSlideChanged),
            PresentationWorkareaOperation.ClearReviewSelection => Invoke(endpoints.ClearReviewSelection),
            PresentationWorkareaOperation.ResetAnimationSelection => Invoke(endpoints.ResetAnimationSelection),
            PresentationWorkareaOperation.ClearMediaSelection => Invoke(endpoints.ClearMediaSelection),
            PresentationWorkareaOperation.SyncSlidePaneSelection => Invoke(endpoints.SyncSlidePaneSelection),
            PresentationWorkareaOperation.RefreshSlidePaneChrome => Invoke(endpoints.RefreshSlidePaneChrome),
            PresentationWorkareaOperation.RefreshReviewPaneBeforePlans =>
                Invoke(endpoints.RefreshReviewPaneBeforePlans),
            PresentationWorkareaOperation.RefreshReviewPaneAfterPlans =>
                Invoke(endpoints.RefreshReviewPaneAfterPlans),
            PresentationWorkareaOperation.RefreshVisibleMediaPane => Invoke(endpoints.RefreshVisibleMediaPane),
            PresentationWorkareaOperation.RefreshCurrentSlideStatus =>
                Invoke(endpoints.RefreshCurrentSlideStatus),
            PresentationWorkareaOperation.RefreshAltTextRequest => Invoke(endpoints.RefreshAltTextRequest),
            PresentationWorkareaOperation.RefreshReadingOrder => Invoke(endpoints.RefreshReadingOrder),
            PresentationWorkareaOperation.RefreshAltTextPane => Invoke(endpoints.RefreshAltTextPane),
            _ => false,
        };
    }

    public static bool Dispatch(
        PresentationWorkareaNativeCommand command,
        PresentationWorkareaNativeCommandEndpoints endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return command switch
        {
            PresentationWorkareaNativeCommand.NewPresentation => Invoke(endpoints.NewPresentation),
            PresentationWorkareaNativeCommand.OpenPresentation => Invoke(endpoints.OpenPresentation),
            PresentationWorkareaNativeCommand.SavePresentation => Invoke(endpoints.SavePresentation),
            PresentationWorkareaNativeCommand.SavePresentationAs => Invoke(endpoints.SavePresentationAs),
            PresentationWorkareaNativeCommand.PrintPresentation => Invoke(endpoints.PrintPresentation),
            PresentationWorkareaNativeCommand.StartSlideShowFromBeginning =>
                Invoke(endpoints.StartSlideShowFromBeginning),
            PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide =>
                Invoke(endpoints.StartSlideShowFromCurrentSlide),
            PresentationWorkareaNativeCommand.Copy => Invoke(endpoints.Copy),
            PresentationWorkareaNativeCommand.Cut => Invoke(endpoints.Cut),
            PresentationWorkareaNativeCommand.Paste => Invoke(endpoints.Paste),
            PresentationWorkareaNativeCommand.Find => Invoke(endpoints.Find),
            PresentationWorkareaNativeCommand.Replace => Invoke(endpoints.Replace),
            _ => false,
        };
    }

    private static bool Invoke(Action? endpoint)
    {
        if (endpoint is null)
            return false;
        endpoint();
        return true;
    }

    private static bool Invoke<T>(Action<T>? endpoint, T argument)
    {
        if (endpoint is null)
            return false;
        endpoint(argument);
        return true;
    }
}
