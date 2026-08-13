namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Identifies the document presentation selected by the application work area.
/// Rendering and editable-page implementation remain platform responsibilities.
/// </summary>
public enum DocumentViewMode
{
    PrintLayout,
    WebLayout,
    Draft,
    PagedEdit,
}
