using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Typed renderer queries used by the portable FreeP ribbon workflow.</summary>
public sealed class FreePRibbonHostQueryEndpoints
{
    public Func<bool?>? BeginFormatPainter { get; init; }
    public Func<bool?>? EditPointsEnabled { get; init; }
    public Func<bool?>? AnimationPaneVisible { get; init; }
    public Func<PresentationViewShowState?>? ViewShowState { get; init; }
    public Func<PresentationViewZoomState?>? ViewZoomState { get; init; }

    internal object? Query(FreePRibbonHostQuery query) => query.Kind switch
    {
        FreePRibbonHostQueryKind.BeginFormatPainter => BeginFormatPainter?.Invoke(),
        FreePRibbonHostQueryKind.EditPointsEnabled => EditPointsEnabled?.Invoke(),
        FreePRibbonHostQueryKind.AnimationPaneVisible => AnimationPaneVisible?.Invoke(),
        FreePRibbonHostQueryKind.ViewShowState => ViewShowState?.Invoke(),
        FreePRibbonHostQueryKind.ViewZoomState => ViewZoomState?.Invoke(),
        _ => null,
    };
}

/// <summary>Native file and export command endpoints supplied by a renderer host.</summary>
public sealed class FreePRibbonFileCommandEndpoints
{
    public Action? New { get; init; }
    public Action? Open { get; init; }
    public Action? Save { get; init; }
    public Action? SaveAs { get; init; }
    public Action? ExportPdf { get; init; }
    public Action? ExportNotesPagePdf { get; init; }
    public Action? ExportImages { get; init; }
    public Action? Print { get; init; }
    public Action? ExportVideo { get; init; }
}

/// <summary>Native OLE insertion and activation endpoints supplied by a renderer host.</summary>
public sealed class FreePRibbonOleCommandEndpoints
{
    public Action? InsertEmbeddedObject { get; init; }
    public Func<bool>? TryOpenInlineEmbeddedObject { get; init; }
    public Func<OleObjectInfo, bool>? TryOpenSelectedEmbeddedObject { get; init; }
}

/// <summary>Renderer-owned native ports consumed by the Presentation profile factory.</summary>
public sealed class FreePRibbonHostPorts
{
    public FreePRibbonHostActionEndpoints ActionEndpoints { get; init; } = new();
    public FreePRibbonHostQueryEndpoints QueryEndpoints { get; init; } = new();
    public FreePRibbonTextActionTargets TextActionTargets { get; init; } = new();
    public FreePRibbonDesignCommandEndpoints DesignCommands { get; init; } = new();
    public FreePRibbonFileCommandEndpoints? FileCommands { get; init; }
    public FreePRibbonOleCommandEndpoints? OleCommands { get; init; }
}

/// <summary>
/// Portable composition profile for FreeP's ribbon registry. Renderers provide native endpoints;
/// command inventory, dispatch, query routing, and OLE selection policy remain Presentation-owned.
/// </summary>
public sealed class FreePRibbonHostProfile
{
    internal FreePRibbonHostProfile(FreePRibbonHostPorts ports)
    {
        ActionEndpoints = ports.ActionEndpoints;
        QueryEndpoints = ports.QueryEndpoints;
        TextActionTargets = ports.TextActionTargets;
        DesignCommands = ports.DesignCommands;
        FileCommands = ports.FileCommands;
        OleCommands = ports.OleCommands;
    }

    internal FreePRibbonHostActionEndpoints ActionEndpoints { get; }
    internal FreePRibbonHostQueryEndpoints QueryEndpoints { get; }
    internal FreePRibbonTextActionTargets TextActionTargets { get; }
    internal FreePRibbonDesignCommandEndpoints DesignCommands { get; }
    internal FreePRibbonFileCommandEndpoints? FileCommands { get; }
    internal FreePRibbonOleCommandEndpoints? OleCommands { get; }

    internal FreePRibbonCommandHostAdapter CreateCommandHostAdapter(EditingSession editor) => new()
    {
        ExecuteAction = action => FreePRibbonHostActionRouter.Dispatch(
            editor,
            action,
            ActionEndpoints,
            DesignCommands),
        QueryState = QueryEndpoints.Query,
        TryHandleTextAction = action =>
            FreePRibbonTextActionTargetRouter.Dispatch(action, TextActionTargets),
    };
}

/// <summary>Creates the canonical typed profile from renderer-owned native ports.</summary>
public static class FreePRibbonHostProfileFactory
{
    public static FreePRibbonHostProfile Create(FreePRibbonHostPorts ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return new FreePRibbonHostProfile(ports);
    }
}

public sealed record FreePRibbonHostRegistryBuildResult(
    RibbonCommandRegistry Registry,
    IReadOnlyDictionary<FreePRibbonCommandGroup, IReadOnlyList<RibbonCommandId>> CommandGroups,
    IReadOnlyList<RibbonCommandId> NativeCommandIds)
{
    public IReadOnlyList<RibbonCommandId> CommonCommandIds =>
        CommandGroups.Values.SelectMany(static commands => commands).ToArray();

    public IReadOnlyList<RibbonCommandId> AllCommandIds =>
        CommonCommandIds.Concat(NativeCommandIds).ToArray();
}

/// <summary>Builds the complete renderer registry from a portable host profile.</summary>
public static class FreePRibbonHostRegistryComposer
{
    private static readonly RibbonCommandId[] FileIds =
    [
        "freep.file.new",
        "freep.file.open",
        "freep.file.save",
        "freep.file.save-as",
        PresentationExportPlanner.PdfExportCommandId,
        PresentationExportPlanner.NotesPagePdfExportCommandId,
        PresentationExportPlanner.ImageExportCommandId,
        PresentationExportPlanner.PrintCommandId,
        PresentationExportPlanner.VideoExportCommandId,
    ];

    private static readonly RibbonCommandId[] OleIds =
    [
        OleInsertionPlanner.InsertEmbeddedObjectCommandId,
        OleActivationPlanner.OpenEmbeddedObjectCommandId,
    ];

    public static IReadOnlyList<RibbonCommandId> FileCommandIds => FileIds;
    public static IReadOnlyList<RibbonCommandId> OleCommandIds => OleIds;

    public static FreePRibbonHostRegistryBuildResult Build(
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(profile);

        var common = FreePRibbonCommandWorkflow.Build(
            editor,
            stateStore,
            profile.CreateCommandHostAdapter(editor));
        var nativeCommandIds = RegisterNativeCommands(common.Registry, editor, profile);
        return new FreePRibbonHostRegistryBuildResult(
            common.Registry,
            common.CommandGroups,
            nativeCommandIds);
    }

    public static FreePRibbonHostRegistryBuildResult BindInto(
        RibbonCommandRegistry target,
        EditingSession editor,
        RibbonStateStore stateStore,
        FreePRibbonHostProfile profile)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(profile);

        var common = FreePRibbonCommandWorkflow.BindInto(
            target,
            editor,
            stateStore,
            profile.CreateCommandHostAdapter(editor));
        var nativeCommandIds = RegisterNativeCommands(target, editor, profile);
        return new FreePRibbonHostRegistryBuildResult(
            target,
            common.CommandGroups,
            nativeCommandIds);
    }

    private static IReadOnlyList<RibbonCommandId> RegisterNativeCommands(
        RibbonCommandRegistry registry,
        EditingSession editor,
        FreePRibbonHostProfile profile)
    {
        var registered = new List<RibbonCommandId>(FileIds.Length + OleIds.Length);
        if (profile.FileCommands is { } file)
        {
            Register(registry, registered, FileIds[0], file.New);
            Register(registry, registered, FileIds[1], file.Open);
            Register(registry, registered, FileIds[2], file.Save);
            Register(registry, registered, FileIds[3], file.SaveAs);
            Register(registry, registered, FileIds[4], file.ExportPdf);
            Register(registry, registered, FileIds[5], file.ExportNotesPagePdf);
            Register(registry, registered, FileIds[6], file.ExportImages);
            Register(registry, registered, FileIds[7], file.Print);
            Register(registry, registered, FileIds[8], file.ExportVideo);
        }

        if (profile.OleCommands is { } ole)
        {
            Register(registry, registered, OleIds[0], ole.InsertEmbeddedObject);
            if (ole.TryOpenInlineEmbeddedObject is not null
                || ole.TryOpenSelectedEmbeddedObject is not null)
            {
                Register(
                    registry,
                    registered,
                    OleIds[1],
                    () => OleActivationPlanner.TryOpenInlineFirst(
                        ole.TryOpenInlineEmbeddedObject,
                        () => TryOpenSelectedEmbeddedObject(editor, ole)));
            }
        }

        return registered.ToArray();
    }

    private static bool TryOpenSelectedEmbeddedObject(
        EditingSession editor,
        FreePRibbonOleCommandEndpoints endpoints)
    {
        if (editor.SelectedOleObject is not { } ole)
            return false;

        return endpoints.TryOpenSelectedEmbeddedObject?.Invoke(ole)
            ?? OleActivationService.TryActivate(ole);
    }

    private static void Register(
        RibbonCommandRegistry registry,
        ICollection<RibbonCommandId> registered,
        RibbonCommandId commandId,
        Action? execute)
    {
        if (execute is null)
            return;

        registry.Register(commandId, new ActionRibbonCommand(execute));
        registered.Add(commandId);
    }
}
