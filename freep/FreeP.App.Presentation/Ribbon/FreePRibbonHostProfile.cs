using Free.Shared.Ribbon;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>Typed renderer queries used by the portable FreeP ribbon workflow.</summary>
public sealed class FreePRibbonHostQueryEndpoints
{
    public Func<bool?>? BeginFormatPainter { get; init; }
    public Func<bool?>? CanMergeTableCells { get; init; }
    public Func<bool?>? CanSplitTableCell { get; init; }
    public Func<bool?>? EditPointsEnabled { get; init; }
    public Func<bool?>? AnimationPaneVisible { get; init; }
    public Func<PresentationViewShowState?>? ViewShowState { get; init; }
    public Func<PresentationViewZoomState?>? ViewZoomState { get; init; }

    internal object? Query(FreePRibbonHostQuery query) => query.Kind switch
    {
        FreePRibbonHostQueryKind.BeginFormatPainter => BeginFormatPainter?.Invoke(),
        FreePRibbonHostQueryKind.CanMergeTableCells => CanMergeTableCells?.Invoke(),
        FreePRibbonHostQueryKind.CanSplitTableCell => CanSplitTableCell?.Invoke(),
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

/// <summary>
/// Portable composition profile for FreeP's ribbon registry. Renderers provide native endpoints;
/// command inventory, dispatch, query routing, and OLE selection policy remain Presentation-owned.
/// </summary>
public sealed class FreePRibbonHostProfile
{
    public FreePRibbonHostActionEndpoints ActionEndpoints { get; init; } = new();
    public FreePRibbonHostQueryEndpoints QueryEndpoints { get; init; } = new();
    public FreePRibbonTextActionEndpoints TextActionEndpoints { get; init; } = new();
    public FreePRibbonFileCommandEndpoints? FileCommands { get; init; }
    public FreePRibbonOleCommandEndpoints? OleCommands { get; init; }

    internal FreePRibbonCommandHostAdapter CreateCommandHostAdapter() => new()
    {
        ExecuteAction = action => FreePRibbonHostActionDispatcher.Dispatch(action, ActionEndpoints),
        QueryState = QueryEndpoints.Query,
        TryHandleTextAction = action =>
            FreePRibbonTextActionDispatcher.Dispatch(action, TextActionEndpoints),
    };
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
            profile.CreateCommandHostAdapter());
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
            profile.CreateCommandHostAdapter());
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
            Register(
                registry,
                registered,
                OleIds[1],
                () => OleActivationPlanner.TryOpenInlineFirst(
                    ole.TryOpenInlineEmbeddedObject,
                    () => TryOpenSelectedEmbeddedObject(editor, ole)));
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
        registry.Register(commandId, new ActionRibbonCommand(() => execute?.Invoke()));
        registered.Add(commandId);
    }
}
