using FreeX.App.Services;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private FreeXOptionsPersistenceResult MutateRuntimeOptions(Action<AppOptions> mutation)
    {
        var result = _optionsRuntimeSession.MutateFresh(mutation);
        _options = result.Options;
        return result;
    }
}
