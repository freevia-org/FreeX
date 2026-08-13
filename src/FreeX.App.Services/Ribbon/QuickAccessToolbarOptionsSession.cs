namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Mutable, renderer-neutral Options-dialog state for Quick Access Toolbar editing. File pickers
/// and messages remain native; reset/import/export and command-list mutations are shared.
/// </summary>
public sealed class QuickAccessToolbarOptionsSession
{
    private List<string> _commandIds;

    public QuickAccessToolbarOptionsSession(
        IEnumerable<string>? commandIds,
        bool quickAccessToolbarBelowRibbon)
    {
        _commandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(commandIds).ToList();
        QuickAccessToolbarBelowRibbon = quickAccessToolbarBelowRibbon;
    }

    public IReadOnlyList<string> CommandIds => _commandIds;

    public bool QuickAccessToolbarBelowRibbon { get; private set; }

    public void SetPlacement(bool belowRibbon) =>
        QuickAccessToolbarBelowRibbon = belowRibbon;

    public void Apply(string commandId, QuickAccessToolbarCustomizationAction action) =>
        Replace(QuickAccessToolbarCustomizationPlanner.Apply(_commandIds, commandId, action));

    public void Move(string commandId, int delta) =>
        Replace(QuickAccessToolbarCustomizationPlanner.Move(_commandIds, commandId, delta));

    public void Reset() => Replace(QuickAccessToolbarCustomizationPlanner.Reset());

    public int IndexOf(string commandId) =>
        _commandIds.FindIndex(id => string.Equals(id, commandId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<QuickAccessToolbarCommandDefinition> FilterAvailable(
        string? searchText,
        Func<QuickAccessToolbarCommandDefinition, IEnumerable<string>>? localizedSearchText = null) =>
        QuickAccessToolbarCustomizationPlanner.FilterAvailable(
            _commandIds,
            searchText,
            localizedSearchText);

    public QuickAccessToolbarCustomizationFileResult TryImport(string path)
    {
        var result = QuickAccessToolbarCustomizationFile.TryLoad(path);
        if (result.Success && result.Customization is { } customization)
            Adopt(customization);

        return result;
    }

    public bool TryExport(string path, out string? errorMessage) =>
        QuickAccessToolbarCustomizationFile.TrySave(
            path,
            _commandIds,
            QuickAccessToolbarBelowRibbon,
            out errorMessage);

    public void Adopt(QuickAccessToolbarCustomization customization)
    {
        ArgumentNullException.ThrowIfNull(customization);
        Replace(customization.CommandIds);
        QuickAccessToolbarBelowRibbon = customization.QuickAccessToolbarBelowRibbon;
    }

    private void Replace(IEnumerable<string>? commandIds) =>
        _commandIds = QuickAccessToolbarCatalog.NormalizeCommandIds(commandIds).ToList();
}
