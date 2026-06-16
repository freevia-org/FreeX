using System.Collections.Generic;
using System.Windows.Controls;

namespace FreeX.App.Host;

// Auto-generated hidden control backplane. These named controls used to live in the
// hand-authored ribbon XAML; the visible ribbon is now declarative (RibbonWpfRenderer).
// They remain as invisible state/handler holders so existing code compiles and runs;
// their state is mirrored onto the rendered ribbon by WireDeclarativeStateSync, and they
// serve as the 'sender' for handlers invoked through the native command registry.
public partial class MainWindow
{
    private readonly System.Windows.Controls.StackPanel HomeRibbonPanel = new();
    private readonly System.Windows.Controls.ComboBox FontNameBox = new();
    private readonly System.Windows.Controls.ComboBox FontSizeBox = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton BoldButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ItalicButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton UnderlineButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton StrikeButton = new();
    private readonly System.Windows.Controls.Button BordersMenuButton = new();
    private readonly System.Windows.Shapes.Rectangle FillColorBar = new();
    private readonly System.Windows.Shapes.Rectangle FontColorBar = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignTopBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignMiddleBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignBottomBtn = new();
    private readonly System.Windows.Controls.Button OrientationPickerButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton WrapTextBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignLeftBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignCenterBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton AlignRightBtn = new();
    private readonly System.Windows.Controls.ComboBox NumberFormatBox = new();
    private readonly System.Windows.Controls.ContextMenu FormatTableGalleryMenu = new();
    private readonly System.Windows.Controls.Button ShapesBtn = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScaleWidthBox = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScaleHeightBox = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScalePercentBox = new();
    private readonly System.Windows.Controls.CheckBox PageLayoutViewGridlinesChk = new();
    private readonly System.Windows.Controls.CheckBox PageLayoutPrintGridlinesChk = new();
    private readonly System.Windows.Controls.CheckBox PageLayoutViewHeadingsChk = new();
    private readonly System.Windows.Controls.CheckBox PageLayoutPrintHeadingsChk = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ShowFormulasButton = new();
    private readonly System.Windows.Controls.Button ReviewNewThreadedCommentButton = new();
    private readonly System.Windows.Controls.Button ReviewDeleteThreadedCommentButton = new();
    private readonly System.Windows.Controls.Button ReviewPreviousThreadedCommentButton = new();
    private readonly System.Windows.Controls.Button ReviewNextThreadedCommentButton = new();
    private readonly System.Windows.Controls.Button ReviewNewNoteButton = new();
    private readonly System.Windows.Controls.Button ReviewEditNoteButton = new();
    private readonly System.Windows.Controls.Button ReviewDeleteNoteButton = new();
    private readonly System.Windows.Controls.Button ReviewPreviousNoteButton = new();
    private readonly System.Windows.Controls.Button ReviewNextNoteButton = new();
    private readonly System.Windows.Controls.Button ProtectSheetButton = new();
    private readonly System.Windows.Controls.Button ProtectWorkbookButton = new();
    private readonly System.Windows.Controls.Button AllowEditRangesButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ViewNormalButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ViewPageBreakPreviewButton = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ViewPageLayoutButton = new();
    private readonly System.Windows.Controls.CheckBox ViewGridlinesChk = new();
    private readonly System.Windows.Controls.CheckBox ViewHeadersChk = new();
    private readonly System.Windows.Controls.CheckBox ViewRulerChk = new();
    private readonly System.Windows.Controls.CheckBox ViewFormulaBarChk = new();
    private readonly System.Windows.Controls.Button ViewNewWindowBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton SplitViewBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ViewSideBySideBtn = new();
    private readonly System.Windows.Controls.Primitives.ToggleButton ViewSynchronousScrollingBtn = new();
    private readonly System.Windows.Controls.Button ViewSwitchWindowsBtn = new();
    private readonly System.Windows.Controls.Button ViewHideWindowBtn = new();
    private readonly System.Windows.Controls.Button ViewUnhideWindowBtn = new();
    private readonly System.Windows.Controls.Button ViewResetWindowPositionBtn = new();
    private readonly System.Windows.Controls.Button ShapeFormatGradientButton = new();
    private readonly System.Windows.Controls.Button ShapeFormatEffectsButton = new();
    private readonly System.Windows.Controls.Button PictureFormatCropButton = new();
    private readonly System.Windows.Controls.MenuItem PictureFormatCropMenuItem = new();
    private readonly System.Windows.Controls.MenuItem PictureFormatResetCropMenuItem = new();
    private readonly System.Windows.Controls.CheckBox TableDesignTotalRowBtn = new();
    private readonly System.Windows.Controls.CheckBox TableDesignFirstColumnBtn = new();
    private readonly System.Windows.Controls.CheckBox TableDesignLastColumnBtn = new();
    private readonly System.Windows.Controls.CheckBox TableDesignBandedRowsBtn = new();
    private readonly System.Windows.Controls.CheckBox TableDesignBandedColumnsBtn = new();
    private readonly System.Windows.Controls.CheckBox TableDesignFilterButtonBtn = new();
    private readonly System.Windows.Controls.ContextMenu TableDesignStyleGalleryMenu = new();
    private readonly AutomationInvokeButton HelpOnlineButton = new();

    /// <summary>Backplane controls that carry a ribbon CommandName, keyed by it.</summary>
    private readonly Dictionary<string, Control> RibbonBackplaneControls = new(System.StringComparer.Ordinal);

    private void InitializeRibbonControlBackplane()
    {
        try { RegisterName("HomeRibbonPanel", HomeRibbonPanel); } catch (System.ArgumentException) { }
        try { RegisterName("FontNameBox", FontNameBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(FontNameBox, "Font");
        RibbonBackplaneControls["Font"] = FontNameBox;
        try { RegisterName("FontSizeBox", FontSizeBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(FontSizeBox, "Font Size");
        RibbonBackplaneControls["Font Size"] = FontSizeBox;
        try { RegisterName("BoldButton", BoldButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(BoldButton, "Bold");
        RibbonBackplaneControls["Bold"] = BoldButton;
        try { RegisterName("ItalicButton", ItalicButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ItalicButton, "Italic");
        RibbonBackplaneControls["Italic"] = ItalicButton;
        try { RegisterName("UnderlineButton", UnderlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(UnderlineButton, "Underline");
        RibbonBackplaneControls["Underline"] = UnderlineButton;
        try { RegisterName("StrikeButton", StrikeButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(StrikeButton, "Strikethrough");
        RibbonBackplaneControls["Strikethrough"] = StrikeButton;
        try { RegisterName("BordersMenuButton", BordersMenuButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(BordersMenuButton, "Borders");
        RibbonBackplaneControls["Borders"] = BordersMenuButton;
        try { RegisterName("FillColorBar", FillColorBar); } catch (System.ArgumentException) { }
        try { RegisterName("FontColorBar", FontColorBar); } catch (System.ArgumentException) { }
        try { RegisterName("AlignTopBtn", AlignTopBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignTopBtn, "Top Align");
        RibbonBackplaneControls["Top Align"] = AlignTopBtn;
        try { RegisterName("AlignMiddleBtn", AlignMiddleBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignMiddleBtn, "Middle Align");
        RibbonBackplaneControls["Middle Align"] = AlignMiddleBtn;
        try { RegisterName("AlignBottomBtn", AlignBottomBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignBottomBtn, "Bottom Align");
        RibbonBackplaneControls["Bottom Align"] = AlignBottomBtn;
        try { RegisterName("OrientationPickerButton", OrientationPickerButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(OrientationPickerButton, "Orientation");
        RibbonBackplaneControls["Orientation"] = OrientationPickerButton;
        try { RegisterName("WrapTextBtn", WrapTextBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(WrapTextBtn, "Wrap Text");
        RibbonBackplaneControls["Wrap Text"] = WrapTextBtn;
        try { RegisterName("AlignLeftBtn", AlignLeftBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignLeftBtn, "Align Left");
        RibbonBackplaneControls["Align Left"] = AlignLeftBtn;
        try { RegisterName("AlignCenterBtn", AlignCenterBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignCenterBtn, "Center");
        RibbonBackplaneControls["Center"] = AlignCenterBtn;
        try { RegisterName("AlignRightBtn", AlignRightBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AlignRightBtn, "Align Right");
        RibbonBackplaneControls["Align Right"] = AlignRightBtn;
        try { RegisterName("NumberFormatBox", NumberFormatBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(NumberFormatBox, "Number Format");
        RibbonBackplaneControls["Number Format"] = NumberFormatBox;
        try { RegisterName("FormatTableGalleryMenu", FormatTableGalleryMenu); } catch (System.ArgumentException) { }
        try { RegisterName("ShapesBtn", ShapesBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShapesBtn, "Shapes");
        RibbonBackplaneControls["Shapes"] = ShapesBtn;
        try { RegisterName("PageLayoutScaleWidthBox", PageLayoutScaleWidthBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScaleWidthBox, "Scale Width");
        RibbonBackplaneControls["Scale Width"] = PageLayoutScaleWidthBox;
        try { RegisterName("PageLayoutScaleHeightBox", PageLayoutScaleHeightBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScaleHeightBox, "Scale Height");
        RibbonBackplaneControls["Scale Height"] = PageLayoutScaleHeightBox;
        try { RegisterName("PageLayoutScalePercentBox", PageLayoutScalePercentBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScalePercentBox, "Scale Percent");
        RibbonBackplaneControls["Scale Percent"] = PageLayoutScalePercentBox;
        try { RegisterName("PageLayoutViewGridlinesChk", PageLayoutViewGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutViewGridlinesChk, "View Gridlines");
        RibbonBackplaneControls["View Gridlines"] = PageLayoutViewGridlinesChk;
        try { RegisterName("PageLayoutPrintGridlinesChk", PageLayoutPrintGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutPrintGridlinesChk, "Print Gridlines");
        RibbonBackplaneControls["Print Gridlines"] = PageLayoutPrintGridlinesChk;
        try { RegisterName("PageLayoutViewHeadingsChk", PageLayoutViewHeadingsChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutViewHeadingsChk, "View Headings");
        RibbonBackplaneControls["View Headings"] = PageLayoutViewHeadingsChk;
        try { RegisterName("PageLayoutPrintHeadingsChk", PageLayoutPrintHeadingsChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutPrintHeadingsChk, "Print Headings");
        RibbonBackplaneControls["Print Headings"] = PageLayoutPrintHeadingsChk;
        try { RegisterName("ShowFormulasButton", ShowFormulasButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShowFormulasButton, "Show Formulas");
        RibbonBackplaneControls["Show Formulas"] = ShowFormulasButton;
        try { RegisterName("ReviewNewThreadedCommentButton", ReviewNewThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNewThreadedCommentButton, "New Comment");
        RibbonBackplaneControls["New Comment"] = ReviewNewThreadedCommentButton;
        try { RegisterName("ReviewDeleteThreadedCommentButton", ReviewDeleteThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewDeleteThreadedCommentButton, "Delete Comment");
        RibbonBackplaneControls["Delete Comment"] = ReviewDeleteThreadedCommentButton;
        try { RegisterName("ReviewPreviousThreadedCommentButton", ReviewPreviousThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewPreviousThreadedCommentButton, "Previous Comment");
        RibbonBackplaneControls["Previous Comment"] = ReviewPreviousThreadedCommentButton;
        try { RegisterName("ReviewNextThreadedCommentButton", ReviewNextThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNextThreadedCommentButton, "Next Comment");
        RibbonBackplaneControls["Next Comment"] = ReviewNextThreadedCommentButton;
        try { RegisterName("ReviewNewNoteButton", ReviewNewNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNewNoteButton, "New Note");
        RibbonBackplaneControls["New Note"] = ReviewNewNoteButton;
        try { RegisterName("ReviewEditNoteButton", ReviewEditNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewEditNoteButton, "Edit Note");
        RibbonBackplaneControls["Edit Note"] = ReviewEditNoteButton;
        try { RegisterName("ReviewDeleteNoteButton", ReviewDeleteNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewDeleteNoteButton, "Delete Note");
        RibbonBackplaneControls["Delete Note"] = ReviewDeleteNoteButton;
        try { RegisterName("ReviewPreviousNoteButton", ReviewPreviousNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewPreviousNoteButton, "Previous Note");
        RibbonBackplaneControls["Previous Note"] = ReviewPreviousNoteButton;
        try { RegisterName("ReviewNextNoteButton", ReviewNextNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNextNoteButton, "Next Note");
        RibbonBackplaneControls["Next Note"] = ReviewNextNoteButton;
        try { RegisterName("ProtectSheetButton", ProtectSheetButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ProtectSheetButton, "Protect Sheet");
        RibbonBackplaneControls["Protect Sheet"] = ProtectSheetButton;
        try { RegisterName("ProtectWorkbookButton", ProtectWorkbookButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ProtectWorkbookButton, "Protect Workbook");
        RibbonBackplaneControls["Protect Workbook"] = ProtectWorkbookButton;
        try { RegisterName("AllowEditRangesButton", AllowEditRangesButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AllowEditRangesButton, "Allow Users to Edit Ranges");
        RibbonBackplaneControls["Allow Users to Edit Ranges"] = AllowEditRangesButton;
        try { RegisterName("ViewNormalButton", ViewNormalButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewNormalButton, "Normal");
        RibbonBackplaneControls["Normal"] = ViewNormalButton;
        try { RegisterName("ViewPageBreakPreviewButton", ViewPageBreakPreviewButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewPageBreakPreviewButton, "Page Break Preview");
        RibbonBackplaneControls["Page Break Preview"] = ViewPageBreakPreviewButton;
        try { RegisterName("ViewPageLayoutButton", ViewPageLayoutButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewPageLayoutButton, "Page Layout");
        RibbonBackplaneControls["Page Layout"] = ViewPageLayoutButton;
        try { RegisterName("ViewGridlinesChk", ViewGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewGridlinesChk, "Gridlines");
        RibbonBackplaneControls["Gridlines"] = ViewGridlinesChk;
        try { RegisterName("ViewHeadersChk", ViewHeadersChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewHeadersChk, "Headings");
        RibbonBackplaneControls["Headings"] = ViewHeadersChk;
        try { RegisterName("ViewRulerChk", ViewRulerChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewRulerChk, "Ruler");
        RibbonBackplaneControls["Ruler"] = ViewRulerChk;
        try { RegisterName("ViewFormulaBarChk", ViewFormulaBarChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewFormulaBarChk, "Formula Bar");
        RibbonBackplaneControls["Formula Bar"] = ViewFormulaBarChk;
        try { RegisterName("ViewNewWindowBtn", ViewNewWindowBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewNewWindowBtn, "New Window");
        RibbonBackplaneControls["New Window"] = ViewNewWindowBtn;
        try { RegisterName("SplitViewBtn", SplitViewBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(SplitViewBtn, "Split");
        RibbonBackplaneControls["Split"] = SplitViewBtn;
        try { RegisterName("ViewSideBySideBtn", ViewSideBySideBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewSideBySideBtn, "View Side by Side");
        RibbonBackplaneControls["View Side by Side"] = ViewSideBySideBtn;
        try { RegisterName("ViewSynchronousScrollingBtn", ViewSynchronousScrollingBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewSynchronousScrollingBtn, "Synchronous Scrolling");
        RibbonBackplaneControls["Synchronous Scrolling"] = ViewSynchronousScrollingBtn;
        try { RegisterName("ViewSwitchWindowsBtn", ViewSwitchWindowsBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewSwitchWindowsBtn, "Switch Windows");
        RibbonBackplaneControls["Switch Windows"] = ViewSwitchWindowsBtn;
        try { RegisterName("ViewHideWindowBtn", ViewHideWindowBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewHideWindowBtn, "Hide");
        RibbonBackplaneControls["Hide"] = ViewHideWindowBtn;
        try { RegisterName("ViewUnhideWindowBtn", ViewUnhideWindowBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewUnhideWindowBtn, "Unhide");
        RibbonBackplaneControls["Unhide"] = ViewUnhideWindowBtn;
        try { RegisterName("ViewResetWindowPositionBtn", ViewResetWindowPositionBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewResetWindowPositionBtn, "Reset Window Position");
        RibbonBackplaneControls["Reset Window Position"] = ViewResetWindowPositionBtn;
        try { RegisterName("ShapeFormatGradientButton", ShapeFormatGradientButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShapeFormatGradientButton, "Shape Gradient");
        RibbonBackplaneControls["Shape Gradient"] = ShapeFormatGradientButton;
        try { RegisterName("ShapeFormatEffectsButton", ShapeFormatEffectsButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShapeFormatEffectsButton, "Shape Effects");
        RibbonBackplaneControls["Shape Effects"] = ShapeFormatEffectsButton;
        try { RegisterName("PictureFormatCropButton", PictureFormatCropButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PictureFormatCropButton, "Crop Picture");
        RibbonBackplaneControls["Crop Picture"] = PictureFormatCropButton;
        try { RegisterName("PictureFormatCropMenuItem", PictureFormatCropMenuItem); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PictureFormatCropMenuItem, "Crop");
        RibbonBackplaneControls["Crop"] = PictureFormatCropMenuItem;
        try { RegisterName("PictureFormatResetCropMenuItem", PictureFormatResetCropMenuItem); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PictureFormatResetCropMenuItem, "Reset Crop");
        RibbonBackplaneControls["Reset Crop"] = PictureFormatResetCropMenuItem;
        try { RegisterName("TableDesignTotalRowBtn", TableDesignTotalRowBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignTotalRowBtn, "Total Row");
        RibbonBackplaneControls["Total Row"] = TableDesignTotalRowBtn;
        try { RegisterName("TableDesignFirstColumnBtn", TableDesignFirstColumnBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignFirstColumnBtn, "First Column");
        RibbonBackplaneControls["First Column"] = TableDesignFirstColumnBtn;
        try { RegisterName("TableDesignLastColumnBtn", TableDesignLastColumnBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignLastColumnBtn, "Last Column");
        RibbonBackplaneControls["Last Column"] = TableDesignLastColumnBtn;
        try { RegisterName("TableDesignBandedRowsBtn", TableDesignBandedRowsBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignBandedRowsBtn, "Banded Rows");
        RibbonBackplaneControls["Banded Rows"] = TableDesignBandedRowsBtn;
        try { RegisterName("TableDesignBandedColumnsBtn", TableDesignBandedColumnsBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignBandedColumnsBtn, "Banded Columns");
        RibbonBackplaneControls["Banded Columns"] = TableDesignBandedColumnsBtn;
        try { RegisterName("TableDesignFilterButtonBtn", TableDesignFilterButtonBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignFilterButtonBtn, "Filter Button");
        RibbonBackplaneControls["Filter Button"] = TableDesignFilterButtonBtn;
        try { RegisterName("TableDesignStyleGalleryMenu", TableDesignStyleGalleryMenu); } catch (System.ArgumentException) { }
        try { RegisterName("HelpOnlineButton", HelpOnlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(HelpOnlineButton, "Help Online");
        RibbonBackplaneControls["Help Online"] = HelpOnlineButton;
    }
}
