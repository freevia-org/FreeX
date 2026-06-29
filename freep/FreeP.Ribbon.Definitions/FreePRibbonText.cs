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

    private static string Get(string key) => Loc.Get(key);
}
