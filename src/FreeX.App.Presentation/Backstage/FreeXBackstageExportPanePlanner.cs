namespace FreeX.App.Presentation.Backstage;

public sealed record FreeXBackstageExportScopeOptionSource<TScope>(
    TScope Scope,
    bool IsAvailable,
    bool IsDefault)
    where TScope : struct, Enum;

public sealed record FreeXBackstageExportScopeOptionRequest(
    FreeXBackstageExportScopeId Scope,
    bool IsAvailable,
    bool IsDefault);

public sealed record FreeXBackstageExportOutputKindOptionRequest(
    FreeXBackstageExportOutputKindId OutputKind,
    bool IsDefault);

public sealed record FreeXBackstageExportPaneRequest(
    IReadOnlyList<FreeXBackstageExportScopeOptionRequest> Scopes,
    IReadOnlyList<FreeXBackstageExportOutputKindOptionRequest> OutputKinds,
    bool CanExport);

public sealed record FreeXBackstageExportScopeOptionPlan(
    FreeXBackstageExportScopeId Scope,
    string LabelKey,
    string AutomationId,
    bool IsEnabled,
    bool IsDefault);

public sealed record FreeXBackstageExportOutputKindOptionPlan(
    FreeXBackstageExportOutputKindId OutputKind,
    string LabelKey,
    string AutomationId,
    bool IsDefault);

public sealed record FreeXBackstageExportPanePlan(
    bool CanExport,
    bool ShowUnavailableNote,
    string UnavailableNoteKey,
    string UnavailableAutomationId,
    string ScopeHeaderKey,
    string ScopeGroupAutomationId,
    IReadOnlyList<FreeXBackstageExportScopeOptionPlan> ScopeOptions,
    string FormatHeaderKey,
    string FormatGroupAutomationId,
    IReadOnlyList<FreeXBackstageExportOutputKindOptionPlan> OutputKindOptions);

/// <summary>
/// Builds the renderer-neutral model for FreeX's Backstage Export pane. Export engines still decide
/// capability and perform the output; this planner owns the pane sections, option labels, and stable ids.
/// </summary>
public static class FreeXBackstageExportPanePlanner
{
    public static FreeXBackstageExportPaneRequest CreateRequest<TScope, TOutputKind>(
        IReadOnlyList<FreeXBackstageExportScopeOptionSource<TScope>> scopes,
        IReadOnlyList<TOutputKind> outputKinds,
        TOutputKind defaultOutputKind,
        bool canExport)
        where TScope : struct, Enum
        where TOutputKind : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(outputKinds);

        return new FreeXBackstageExportPaneRequest(
            scopes
                .Select(scope => new FreeXBackstageExportScopeOptionRequest(
                    ToBackstageScopeId(scope.Scope),
                    scope.IsAvailable,
                    scope.IsDefault))
                .ToArray(),
            outputKinds
                .Select(outputKind => new FreeXBackstageExportOutputKindOptionRequest(
                    ToBackstageOutputKindId(outputKind),
                    EqualityComparer<TOutputKind>.Default.Equals(outputKind, defaultOutputKind)))
                .ToArray(),
            canExport);
    }

    public static FreeXBackstageExportScopeId ToBackstageScopeId<TScope>(TScope scope)
        where TScope : struct, Enum =>
        ConvertEnum<TScope, FreeXBackstageExportScopeId>(scope);

    public static TScope ToExternalScope<TScope>(FreeXBackstageExportScopeId scope)
        where TScope : struct, Enum =>
        ConvertEnum<FreeXBackstageExportScopeId, TScope>(scope);

    public static FreeXBackstageExportOutputKindId ToBackstageOutputKindId<TOutputKind>(TOutputKind outputKind)
        where TOutputKind : struct, Enum =>
        ConvertEnum<TOutputKind, FreeXBackstageExportOutputKindId>(outputKind);

    public static TOutputKind ToExternalOutputKind<TOutputKind>(FreeXBackstageExportOutputKindId outputKind)
        where TOutputKind : struct, Enum =>
        ConvertEnum<FreeXBackstageExportOutputKindId, TOutputKind>(outputKind);

    public static FreeXBackstageExportPanePlan Build(FreeXBackstageExportPaneRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new FreeXBackstageExportPanePlan(
            request.CanExport,
            ShowUnavailableNote: !request.CanExport,
            UnavailableNoteKey: "Backstage_Export_Unavailable",
            UnavailableAutomationId: "BackstageExportUnavailable",
            ScopeHeaderKey: "Backstage_Export_ScopeHeader",
            ScopeGroupAutomationId: "BackstageExportScope",
            BuildScopeOptions(request.Scopes),
            FormatHeaderKey: "Backstage_Export_FormatHeader",
            FormatGroupAutomationId: "BackstageExportFormat",
            BuildOutputKindOptions(request.OutputKinds));
    }

    private static IReadOnlyList<FreeXBackstageExportScopeOptionPlan> BuildScopeOptions(
        IReadOnlyList<FreeXBackstageExportScopeOptionRequest> requests)
    {
        var options = new List<FreeXBackstageExportScopeOptionPlan>(requests.Count);
        foreach (var request in requests)
        {
            options.Add(new FreeXBackstageExportScopeOptionPlan(
                request.Scope,
                FreeXBackstagePaneCatalog.GetExportScopeLabelKey(request.Scope, request.IsAvailable),
                FreeXBackstagePaneCatalog.GetExportScopeAutomationId(request.Scope),
                request.IsAvailable,
                request.IsDefault));
        }

        return options;
    }

    private static IReadOnlyList<FreeXBackstageExportOutputKindOptionPlan> BuildOutputKindOptions(
        IReadOnlyList<FreeXBackstageExportOutputKindOptionRequest> requests)
    {
        var options = new List<FreeXBackstageExportOutputKindOptionPlan>(requests.Count);
        foreach (var request in requests)
        {
            options.Add(new FreeXBackstageExportOutputKindOptionPlan(
                request.OutputKind,
                FreeXBackstagePaneCatalog.GetExportOutputKindLabelKey(request.OutputKind),
                FreeXBackstagePaneCatalog.GetExportOutputKindAutomationId(request.OutputKind),
                request.IsDefault));
        }

        return options;
    }

    private static TDestination ConvertEnum<TSource, TDestination>(TSource source)
        where TSource : struct, Enum
        where TDestination : struct, Enum
    {
        var name = Enum.GetName(typeof(TSource), source)
            ?? throw new InvalidOperationException(
                $"Backstage export mapping cannot resolve unnamed {typeof(TSource).Name} value '{source}'.");

        if (Enum.TryParse<TDestination>(name, ignoreCase: false, out var destination))
            return destination;

        throw new InvalidOperationException(
            $"Backstage export mapping does not define {typeof(TDestination).Name} value '{name}' for {typeof(TSource).Name}.");
    }
}
