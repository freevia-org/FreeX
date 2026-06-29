using FreeP.App.Localization;

namespace FreeP.Ribbon.Definitions;

internal static class FreePRibbonText
{
    public static string HomeTabLabel => Get("Ribbon_Tab_Home_Label");
    public static string HomeTabKeyTip => Get("Ribbon_Tab_Home_KeyTip");

    public static string FileGroupLabel => Get("Ribbon_Group_File_Label");
    public static string FileGroupKeyTip => Get("Ribbon_Group_File_KeyTip");
    public static string FileNewLabel => Get("Ribbon_Command_FileNew_Label");
    public static string FileNewKeyTip => Get("Ribbon_Command_FileNew_KeyTip");
    public static string FileOpenLabel => Get("Ribbon_Command_FileOpen_Label");
    public static string FileOpenKeyTip => Get("Ribbon_Command_FileOpen_KeyTip");
    public static string FileSaveLabel => Get("Ribbon_Command_FileSave_Label");
    public static string FileSaveKeyTip => Get("Ribbon_Command_FileSave_KeyTip");
    public static string FileSaveAsLabel => Get("Ribbon_Command_FileSaveAs_Label");
    public static string FileSaveAsKeyTip => Get("Ribbon_Command_FileSaveAs_KeyTip");

    public static string SlidesGroupLabel => Get("Ribbon_Group_Slides_Label");
    public static string SlidesGroupKeyTip => Get("Ribbon_Group_Slides_KeyTip");
    public static string NewSlideLabel => Get("Ribbon_Command_NewSlide_Label");
    public static string NewSlideKeyTip => Get("Ribbon_Command_NewSlide_KeyTip");
    public static string NewSlideAvaloniaKeyTip => Get("Ribbon_Command_NewSlide_AvaloniaKeyTip");
    public static string DuplicateSlideLabel => Get("Ribbon_Command_DuplicateSlide_Label");
    public static string DuplicateSlideKeyTip => Get("Ribbon_Command_DuplicateSlide_KeyTip");
    public static string DeleteSlideLabel => Get("Ribbon_Command_DeleteSlide_Label");
    public static string DeleteSlideKeyTip => Get("Ribbon_Command_DeleteSlide_KeyTip");

    public static string EditGroupLabel => Get("Ribbon_Group_Edit_Label");
    public static string EditGroupKeyTip => Get("Ribbon_Group_Edit_KeyTip");
    public static string UndoLabel => Get("Ribbon_Command_Undo_Label");
    public static string UndoKeyTip => Get("Ribbon_Command_Undo_KeyTip");
    public static string RedoLabel => Get("Ribbon_Command_Redo_Label");
    public static string RedoKeyTip => Get("Ribbon_Command_Redo_KeyTip");

    public static string SlideShowGroupLabel => Get("Ribbon_Group_SlideShow_Label");
    public static string SlideShowGroupWpfKeyTip => Get("Ribbon_Group_SlideShow_WpfKeyTip");
    public static string SlideShowGroupAvaloniaKeyTip => Get("Ribbon_Group_SlideShow_AvaloniaKeyTip");
    public static string SlideShowFromBeginningLabel => Get("Ribbon_Command_SlideShowFromBeginning_Label");
    public static string SlideShowFromBeginningKeyTip => Get("Ribbon_Command_SlideShowFromBeginning_KeyTip");
    public static string SlideShowFromCurrentSlideLabel => Get("Ribbon_Command_SlideShowFromCurrentSlide_Label");
    public static string SlideShowFromCurrentSlideKeyTip => Get("Ribbon_Command_SlideShowFromCurrentSlide_KeyTip");

    public static string InsertTabLabel => Get("Ribbon_Tab_Insert_Label");
    public static string InsertTabKeyTip => Get("Ribbon_Tab_Insert_KeyTip");

    public static string TextGroupLabel => Get("Ribbon_Group_Text_Label");
    public static string TextGroupKeyTip => Get("Ribbon_Group_Text_KeyTip");
    public static string TextBoxLabel => Get("Ribbon_Command_TextBox_Label");
    public static string TextBoxKeyTip => Get("Ribbon_Command_TextBox_KeyTip");

    public static string TablesGroupLabel => Get("Ribbon_Group_Tables_Label");
    public static string TablesGroupKeyTip => Get("Ribbon_Group_Tables_KeyTip");
    public static string InsertTable3x3Label => Get("Ribbon_Command_InsertTable3x3_Label");
    public static string InsertTable3x3KeyTip => Get("Ribbon_Command_InsertTable3x3_KeyTip");
    public static string InsertTable2x2Label => Get("Ribbon_Command_InsertTable2x2_Label");
    public static string InsertTable2x2KeyTip => Get("Ribbon_Command_InsertTable2x2_KeyTip");
    public static string InsertTable4x4Label => Get("Ribbon_Command_InsertTable4x4_Label");
    public static string InsertTable4x4KeyTip => Get("Ribbon_Command_InsertTable4x4_KeyTip");

    public static string ChartsGroupLabel => Get("Ribbon_Group_Charts_Label");
    public static string ChartsGroupKeyTip => Get("Ribbon_Group_Charts_KeyTip");
    public static string InsertChartColumnLabel => Get("Ribbon_Command_InsertChartColumn_Label");
    public static string InsertChartColumnKeyTip => Get("Ribbon_Command_InsertChartColumn_KeyTip");
    public static string InsertChartBarLabel => Get("Ribbon_Command_InsertChartBar_Label");
    public static string InsertChartBarKeyTip => Get("Ribbon_Command_InsertChartBar_KeyTip");
    public static string InsertChartLineLabel => Get("Ribbon_Command_InsertChartLine_Label");
    public static string InsertChartLineKeyTip => Get("Ribbon_Command_InsertChartLine_KeyTip");
    public static string InsertChartPieLabel => Get("Ribbon_Command_InsertChartPie_Label");
    public static string InsertChartPieKeyTip => Get("Ribbon_Command_InsertChartPie_KeyTip");
    public static string ChartEditDataLabel => Get("Ribbon_Command_ChartEditData_Label");
    public static string ChartEditDataKeyTip => Get("Ribbon_Command_ChartEditData_KeyTip");

    public static string LinksGroupLabel => Get("Ribbon_Group_Links_Label");
    public static string LinksGroupKeyTip => Get("Ribbon_Group_Links_KeyTip");
    public static string InsertLinkLabel => Get("Ribbon_Command_InsertLink_Label");
    public static string InsertLinkKeyTip => Get("Ribbon_Command_InsertLink_KeyTip");
    public static string RemoveLinkLabel => Get("Ribbon_Command_RemoveLink_Label");
    public static string RemoveLinkKeyTip => Get("Ribbon_Command_RemoveLink_KeyTip");

    public static string IllustrationsGroupLabel => Get("Ribbon_Group_Illustrations_Label");
    public static string IllustrationsGroupKeyTip => Get("Ribbon_Group_Illustrations_KeyTip");
    public static string PictureLabel => Get("Ribbon_Command_Picture_Label");
    public static string PictureKeyTip => Get("Ribbon_Command_Picture_KeyTip");
    public static string ShapeRectangleLabel => Get("Ribbon_Command_ShapeRectangle_Label");
    public static string ShapeRectangleKeyTip => Get("Ribbon_Command_ShapeRectangle_KeyTip");
    public static string ShapeEllipseLabel => Get("Ribbon_Command_ShapeEllipse_Label");
    public static string ShapeEllipseKeyTip => Get("Ribbon_Command_ShapeEllipse_KeyTip");

    private static string Get(string key) => Loc.Get(key);
}
