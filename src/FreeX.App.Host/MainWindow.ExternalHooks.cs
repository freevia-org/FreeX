using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    partial void AdjustExternalInitialWorkbookCreation(ref bool shouldCreate);

    partial void StartExternalLoadedWorkflows();

    partial void RefreshExternalReviewWindows(Sheet sheet);
}
