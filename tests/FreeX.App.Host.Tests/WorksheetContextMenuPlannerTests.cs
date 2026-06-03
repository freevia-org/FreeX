namespace FreeX.App.Host.Tests;

public sealed partial class WorksheetContextMenuPlannerTests
{
    public static TheoryData<WorksheetContextMenuTargetKind, string[], string[]> TargetSpecificCommandEnvelopeCases => new()
    {
        {
            WorksheetContextMenuTargetKind.Worksheet,
            [
                "Insert...",
                "Custom Sort...",
                "Quick Analysis",
                "Data Validation...",
                "New Comment",
                "Format Cells..."
            ],
            [
                "Format Picture...",
                "Format Shape...",
                "Format Text Box...",
                "Group",
                "Ungroup"
            ]
        },
        {
            WorksheetContextMenuTargetKind.RowSelection,
            [
                "Insert Row Above",
                "Delete Row(s)",
                "Row Height...",
                "AutoFit Row Height",
                "Group",
                "Ungroup"
            ],
            [
                "Insert...",
                "Data Validation...",
                "Column Width...",
                "Format Picture..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.ColumnSelection,
            [
                "Insert Column Left",
                "Delete Column(s)",
                "Column Width...",
                "AutoFit Column Width",
                "Group",
                "Ungroup"
            ],
            [
                "Insert...",
                "Data Validation...",
                "Row Height...",
                "Format Picture..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.Picture,
            [
                "Format Picture...",
                "Crop...",
                "Reset Crop",
                "Edit Alt Text...",
                "Selection Pane..."
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Shape...",
                "Group"
            ]
        },
        {
            WorksheetContextMenuTargetKind.Shape,
            [
                "Format Shape...",
                "Size and Properties...",
                "Rotate...",
                "Bring Forward",
                "Send Backward"
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Picture...",
                "Format Text Box..."
            ]
        },
        {
            WorksheetContextMenuTargetKind.TextBox,
            [
                "Format Text Box...",
                "Size and Properties...",
                "Rotate...",
                "Shape Fill...",
                "Shape Outline..."
            ],
            [
                "Insert...",
                "Format Cells...",
                "Format Picture...",
                "Bring Forward",
                "Send Backward"
            ]
        }
    };

    public static TheoryData<WorksheetContextMenuTargetKind, WorksheetContextMenuCommand[]> RowColumnSizingVisibilityCases => new()
    {
        {
            WorksheetContextMenuTargetKind.Worksheet,
            [
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws"),
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns"),
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth")
            ]
        },
        {
            WorksheetContextMenuTargetKind.RowSelection,
            [
                new("Row Height...", WorksheetContextMenuAction.RowHeight, AccessHeader: "Row _Height..."),
                new("AutoFit Row Height", WorksheetContextMenuAction.AutoFitRowHeight, AccessHeader: "AutoFit Row He_ight"),
                new("Hide Rows", WorksheetContextMenuAction.HideRows, AccessHeader: "_Hide Rows"),
                new("Unhide Rows", WorksheetContextMenuAction.UnhideRows, AccessHeader: "Unhide Ro_ws")
            ]
        },
        {
            WorksheetContextMenuTargetKind.ColumnSelection,
            [
                new("Column Width...", WorksheetContextMenuAction.ColumnWidth, AccessHeader: "Column _Width..."),
                new("AutoFit Column Width", WorksheetContextMenuAction.AutoFitColumnWidth, AccessHeader: "AutoFit Column Wi_dth"),
                new("Hide Columns", WorksheetContextMenuAction.HideColumns, AccessHeader: "Hide Col_umns"),
                new("Unhide Columns", WorksheetContextMenuAction.UnhideColumns, AccessHeader: "Unhide Co_lumns")
            ]
        }
    };
}
