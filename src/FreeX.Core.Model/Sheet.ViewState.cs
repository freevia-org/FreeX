namespace FreeX.Core.Model;

public sealed partial class Sheet
{
    /// <summary>Resets worksheet activation and viewport state for a newly created sheet.</summary>
    public void ResetViewStateToA1()
    {
        ActiveRow = 1;
        ActiveCol = 1;
        ViewTopRow = 1;
        ViewLeftCol = 1;
    }
}
