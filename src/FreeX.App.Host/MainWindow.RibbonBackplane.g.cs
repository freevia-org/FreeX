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
    private readonly System.Windows.Controls.Button OrientationPickerButton = new();
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
    private readonly System.Windows.Controls.Primitives.ToggleButton SplitViewBtn = new();
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

    /// <summary>Original x:Name of each backplane control keyed by its CommandName, so the
    /// declarative wiring can re-point that name to the visible rendered control (FindName).</summary>
    private readonly Dictionary<string, string> RibbonBackplaneControlNames = new(System.StringComparer.Ordinal);

    private void InitializeRibbonControlBackplane()
    {
        try { RegisterName("HomeRibbonPanel", HomeRibbonPanel); } catch (System.ArgumentException) { }
        try { RegisterName("FontNameBox", FontNameBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(FontNameBox, "Font");
        RibbonBackplaneControls["Font"] = FontNameBox;
        RibbonBackplaneControlNames["Font"] = "FontNameBox";
        try { RegisterName("FontSizeBox", FontSizeBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(FontSizeBox, "Font Size");
        RibbonBackplaneControls["Font Size"] = FontSizeBox;
        RibbonBackplaneControlNames["Font Size"] = "FontSizeBox";
        try { RegisterName("BoldButton", BoldButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(BoldButton, "Bold");
        RibbonBackplaneControls["Bold"] = BoldButton;
        RibbonBackplaneControlNames["Bold"] = "BoldButton";
        try { RegisterName("ItalicButton", ItalicButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ItalicButton, "Italic");
        RibbonBackplaneControls["Italic"] = ItalicButton;
        RibbonBackplaneControlNames["Italic"] = "ItalicButton";
        try { RegisterName("UnderlineButton", UnderlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(UnderlineButton, "Underline");
        RibbonBackplaneControls["Underline"] = UnderlineButton;
        RibbonBackplaneControlNames["Underline"] = "UnderlineButton";
        try { RegisterName("StrikeButton", StrikeButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(StrikeButton, "Strikethrough");
        RibbonBackplaneControls["Strikethrough"] = StrikeButton;
        RibbonBackplaneControlNames["Strikethrough"] = "StrikeButton";
        try { RegisterName("BordersMenuButton", BordersMenuButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(BordersMenuButton, "Borders");
        RibbonBackplaneControls["Borders"] = BordersMenuButton;
        RibbonBackplaneControlNames["Borders"] = "BordersMenuButton";
        RibbonBackplaneControlNames["Top Align"] = "AlignTopBtn";
        RibbonBackplaneControlNames["Middle Align"] = "AlignMiddleBtn";
        RibbonBackplaneControlNames["Bottom Align"] = "AlignBottomBtn";
        try { RegisterName("OrientationPickerButton", OrientationPickerButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(OrientationPickerButton, "Orientation");
        RibbonBackplaneControls["Orientation"] = OrientationPickerButton;
        RibbonBackplaneControlNames["Orientation"] = "OrientationPickerButton";
        RibbonBackplaneControlNames["Wrap Text"] = "WrapTextBtn";
        RibbonBackplaneControlNames["Align Left"] = "AlignLeftBtn";
        RibbonBackplaneControlNames["Center"] = "AlignCenterBtn";
        RibbonBackplaneControlNames["Align Right"] = "AlignRightBtn";
        try { RegisterName("NumberFormatBox", NumberFormatBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(NumberFormatBox, "Number Format");
        RibbonBackplaneControls["Number Format"] = NumberFormatBox;
        RibbonBackplaneControlNames["Number Format"] = "NumberFormatBox";
        try { RegisterName("FormatTableGalleryMenu", FormatTableGalleryMenu); } catch (System.ArgumentException) { }
        try { RegisterName("ShapesBtn", ShapesBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShapesBtn, "Shapes");
        RibbonBackplaneControls["Shapes"] = ShapesBtn;
        RibbonBackplaneControlNames["Shapes"] = "ShapesBtn";
        try { RegisterName("PageLayoutScaleWidthBox", PageLayoutScaleWidthBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScaleWidthBox, "Scale Width");
        RibbonBackplaneControls["Scale Width"] = PageLayoutScaleWidthBox;
        RibbonBackplaneControlNames["Scale Width"] = "PageLayoutScaleWidthBox";
        try { RegisterName("PageLayoutScaleHeightBox", PageLayoutScaleHeightBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScaleHeightBox, "Scale Height");
        RibbonBackplaneControls["Scale Height"] = PageLayoutScaleHeightBox;
        RibbonBackplaneControlNames["Scale Height"] = "PageLayoutScaleHeightBox";
        try { RegisterName("PageLayoutScalePercentBox", PageLayoutScalePercentBox); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutScalePercentBox, "Scale Percent");
        RibbonBackplaneControls["Scale Percent"] = PageLayoutScalePercentBox;
        RibbonBackplaneControlNames["Scale Percent"] = "PageLayoutScalePercentBox";
        try { RegisterName("PageLayoutViewGridlinesChk", PageLayoutViewGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutViewGridlinesChk, "View Gridlines");
        RibbonBackplaneControls["View Gridlines"] = PageLayoutViewGridlinesChk;
        RibbonBackplaneControlNames["View Gridlines"] = "PageLayoutViewGridlinesChk";
        try { RegisterName("PageLayoutPrintGridlinesChk", PageLayoutPrintGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutPrintGridlinesChk, "Print Gridlines");
        RibbonBackplaneControls["Print Gridlines"] = PageLayoutPrintGridlinesChk;
        RibbonBackplaneControlNames["Print Gridlines"] = "PageLayoutPrintGridlinesChk";
        try { RegisterName("PageLayoutViewHeadingsChk", PageLayoutViewHeadingsChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutViewHeadingsChk, "View Headings");
        RibbonBackplaneControls["View Headings"] = PageLayoutViewHeadingsChk;
        RibbonBackplaneControlNames["View Headings"] = "PageLayoutViewHeadingsChk";
        try { RegisterName("PageLayoutPrintHeadingsChk", PageLayoutPrintHeadingsChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PageLayoutPrintHeadingsChk, "Print Headings");
        RibbonBackplaneControls["Print Headings"] = PageLayoutPrintHeadingsChk;
        RibbonBackplaneControlNames["Print Headings"] = "PageLayoutPrintHeadingsChk";
        try { RegisterName("ShowFormulasButton", ShowFormulasButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ShowFormulasButton, "Show Formulas");
        RibbonBackplaneControls["Show Formulas"] = ShowFormulasButton;
        RibbonBackplaneControlNames["Show Formulas"] = "ShowFormulasButton";
        try { RegisterName("ReviewNewThreadedCommentButton", ReviewNewThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNewThreadedCommentButton, "New Comment");
        RibbonBackplaneControls["New Comment"] = ReviewNewThreadedCommentButton;
        RibbonBackplaneControlNames["New Comment"] = "ReviewNewThreadedCommentButton";
        try { RegisterName("ReviewDeleteThreadedCommentButton", ReviewDeleteThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewDeleteThreadedCommentButton, "Delete Comment");
        RibbonBackplaneControls["Delete Comment"] = ReviewDeleteThreadedCommentButton;
        RibbonBackplaneControlNames["Delete Comment"] = "ReviewDeleteThreadedCommentButton";
        try { RegisterName("ReviewPreviousThreadedCommentButton", ReviewPreviousThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewPreviousThreadedCommentButton, "Previous Comment");
        RibbonBackplaneControls["Previous Comment"] = ReviewPreviousThreadedCommentButton;
        RibbonBackplaneControlNames["Previous Comment"] = "ReviewPreviousThreadedCommentButton";
        try { RegisterName("ReviewNextThreadedCommentButton", ReviewNextThreadedCommentButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNextThreadedCommentButton, "Next Comment");
        RibbonBackplaneControls["Next Comment"] = ReviewNextThreadedCommentButton;
        RibbonBackplaneControlNames["Next Comment"] = "ReviewNextThreadedCommentButton";
        try { RegisterName("ReviewNewNoteButton", ReviewNewNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNewNoteButton, "New Note");
        RibbonBackplaneControls["New Note"] = ReviewNewNoteButton;
        RibbonBackplaneControlNames["New Note"] = "ReviewNewNoteButton";
        try { RegisterName("ReviewEditNoteButton", ReviewEditNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewEditNoteButton, "Edit Note");
        RibbonBackplaneControls["Edit Note"] = ReviewEditNoteButton;
        RibbonBackplaneControlNames["Edit Note"] = "ReviewEditNoteButton";
        try { RegisterName("ReviewDeleteNoteButton", ReviewDeleteNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewDeleteNoteButton, "Delete Note");
        RibbonBackplaneControls["Delete Note"] = ReviewDeleteNoteButton;
        RibbonBackplaneControlNames["Delete Note"] = "ReviewDeleteNoteButton";
        try { RegisterName("ReviewPreviousNoteButton", ReviewPreviousNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewPreviousNoteButton, "Previous Note");
        RibbonBackplaneControls["Previous Note"] = ReviewPreviousNoteButton;
        RibbonBackplaneControlNames["Previous Note"] = "ReviewPreviousNoteButton";
        try { RegisterName("ReviewNextNoteButton", ReviewNextNoteButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ReviewNextNoteButton, "Next Note");
        RibbonBackplaneControls["Next Note"] = ReviewNextNoteButton;
        RibbonBackplaneControlNames["Next Note"] = "ReviewNextNoteButton";
        try { RegisterName("ProtectSheetButton", ProtectSheetButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ProtectSheetButton, "Protect Sheet");
        RibbonBackplaneControls["Protect Sheet"] = ProtectSheetButton;
        RibbonBackplaneControlNames["Protect Sheet"] = "ProtectSheetButton";
        try { RegisterName("ProtectWorkbookButton", ProtectWorkbookButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ProtectWorkbookButton, "Protect Workbook");
        RibbonBackplaneControls["Protect Workbook"] = ProtectWorkbookButton;
        RibbonBackplaneControlNames["Protect Workbook"] = "ProtectWorkbookButton";
        try { RegisterName("AllowEditRangesButton", AllowEditRangesButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(AllowEditRangesButton, "Allow Users to Edit Ranges");
        RibbonBackplaneControls["Allow Users to Edit Ranges"] = AllowEditRangesButton;
        RibbonBackplaneControlNames["Allow Users to Edit Ranges"] = "AllowEditRangesButton";
        try { RegisterName("ViewNormalButton", ViewNormalButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewNormalButton, "Normal");
        RibbonBackplaneControls["Normal"] = ViewNormalButton;
        RibbonBackplaneControlNames["Normal"] = "ViewNormalButton";
        try { RegisterName("ViewPageBreakPreviewButton", ViewPageBreakPreviewButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewPageBreakPreviewButton, "Page Break Preview");
        RibbonBackplaneControls["Page Break Preview"] = ViewPageBreakPreviewButton;
        RibbonBackplaneControlNames["Page Break Preview"] = "ViewPageBreakPreviewButton";
        try { RegisterName("ViewPageLayoutButton", ViewPageLayoutButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewPageLayoutButton, "Page Layout");
        RibbonBackplaneControls["Page Layout"] = ViewPageLayoutButton;
        RibbonBackplaneControlNames["Page Layout"] = "ViewPageLayoutButton";
        try { RegisterName("ViewGridlinesChk", ViewGridlinesChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewGridlinesChk, "Gridlines");
        RibbonBackplaneControls["Gridlines"] = ViewGridlinesChk;
        RibbonBackplaneControlNames["Gridlines"] = "ViewGridlinesChk";
        try { RegisterName("ViewHeadersChk", ViewHeadersChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewHeadersChk, "Headings");
        RibbonBackplaneControls["Headings"] = ViewHeadersChk;
        RibbonBackplaneControlNames["Headings"] = "ViewHeadersChk";
        try { RegisterName("ViewRulerChk", ViewRulerChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewRulerChk, "Ruler");
        RibbonBackplaneControls["Ruler"] = ViewRulerChk;
        RibbonBackplaneControlNames["Ruler"] = "ViewRulerChk";
        try { RegisterName("ViewFormulaBarChk", ViewFormulaBarChk); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(ViewFormulaBarChk, "Formula Bar");
        RibbonBackplaneControls["Formula Bar"] = ViewFormulaBarChk;
        RibbonBackplaneControlNames["Formula Bar"] = "ViewFormulaBarChk";
        RibbonBackplaneControlNames["New Window"] = "ViewNewWindowBtn";
        try { RegisterName("SplitViewBtn", SplitViewBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(SplitViewBtn, "Split");
        RibbonBackplaneControls["Split"] = SplitViewBtn;
        RibbonBackplaneControlNames["Split"] = "SplitViewBtn";
        RibbonBackplaneControlNames["View Side by Side"] = "ViewSideBySideBtn";
        RibbonBackplaneControlNames["Synchronous Scrolling"] = "ViewSynchronousScrollingBtn";
        RibbonBackplaneControlNames["Switch Windows"] = "ViewSwitchWindowsBtn";
        RibbonBackplaneControlNames["Hide"] = "ViewHideWindowBtn";
        RibbonBackplaneControlNames["Unhide"] = "ViewUnhideWindowBtn";
        RibbonBackplaneControlNames["Reset Window Position"] = "ViewResetWindowPositionBtn";
        RibbonBackplaneControlNames["Shape Gradient"] = "ShapeFormatGradientButton";
        RibbonBackplaneControlNames["Shape Effects"] = "ShapeFormatEffectsButton";
        RibbonBackplaneControlNames["Crop Picture"] = "PictureFormatCropButton";
        try { RegisterName("PictureFormatCropMenuItem", PictureFormatCropMenuItem); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PictureFormatCropMenuItem, "Crop");
        RibbonBackplaneControls["Crop"] = PictureFormatCropMenuItem;
        RibbonBackplaneControlNames["Crop"] = "PictureFormatCropMenuItem";
        try { RegisterName("PictureFormatResetCropMenuItem", PictureFormatResetCropMenuItem); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(PictureFormatResetCropMenuItem, "Reset Crop");
        RibbonBackplaneControls["Reset Crop"] = PictureFormatResetCropMenuItem;
        RibbonBackplaneControlNames["Reset Crop"] = "PictureFormatResetCropMenuItem";
        try { RegisterName("TableDesignTotalRowBtn", TableDesignTotalRowBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignTotalRowBtn, "Total Row");
        RibbonBackplaneControls["Total Row"] = TableDesignTotalRowBtn;
        RibbonBackplaneControlNames["Total Row"] = "TableDesignTotalRowBtn";
        try { RegisterName("TableDesignFirstColumnBtn", TableDesignFirstColumnBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignFirstColumnBtn, "First Column");
        RibbonBackplaneControls["First Column"] = TableDesignFirstColumnBtn;
        RibbonBackplaneControlNames["First Column"] = "TableDesignFirstColumnBtn";
        try { RegisterName("TableDesignLastColumnBtn", TableDesignLastColumnBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignLastColumnBtn, "Last Column");
        RibbonBackplaneControls["Last Column"] = TableDesignLastColumnBtn;
        RibbonBackplaneControlNames["Last Column"] = "TableDesignLastColumnBtn";
        try { RegisterName("TableDesignBandedRowsBtn", TableDesignBandedRowsBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignBandedRowsBtn, "Banded Rows");
        RibbonBackplaneControls["Banded Rows"] = TableDesignBandedRowsBtn;
        RibbonBackplaneControlNames["Banded Rows"] = "TableDesignBandedRowsBtn";
        try { RegisterName("TableDesignBandedColumnsBtn", TableDesignBandedColumnsBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignBandedColumnsBtn, "Banded Columns");
        RibbonBackplaneControls["Banded Columns"] = TableDesignBandedColumnsBtn;
        RibbonBackplaneControlNames["Banded Columns"] = "TableDesignBandedColumnsBtn";
        try { RegisterName("TableDesignFilterButtonBtn", TableDesignFilterButtonBtn); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(TableDesignFilterButtonBtn, "Filter Button");
        RibbonBackplaneControls["Filter Button"] = TableDesignFilterButtonBtn;
        RibbonBackplaneControlNames["Filter Button"] = "TableDesignFilterButtonBtn";
        try { RegisterName("TableDesignStyleGalleryMenu", TableDesignStyleGalleryMenu); } catch (System.ArgumentException) { }
        try { RegisterName("HelpOnlineButton", HelpOnlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(HelpOnlineButton, "Help Online");
        RibbonBackplaneControls["Help Online"] = HelpOnlineButton;
        RibbonBackplaneControlNames["Help Online"] = "HelpOnlineButton";
    }
}
