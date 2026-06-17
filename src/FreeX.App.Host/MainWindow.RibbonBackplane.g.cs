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
    private readonly System.Windows.Controls.Primitives.ToggleButton UnderlineButton = new();
    private readonly System.Windows.Controls.Button BordersMenuButton = new();
    private readonly System.Windows.Controls.Button OrientationPickerButton = new();
    private readonly System.Windows.Controls.ContextMenu FormatTableGalleryMenu = new();
    private readonly System.Windows.Controls.Button ShapesBtn = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScaleWidthBox = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScaleHeightBox = new();
    private readonly System.Windows.Controls.ComboBox PageLayoutScalePercentBox = new();
    private readonly System.Windows.Controls.MenuItem PictureFormatCropMenuItem = new();
    private readonly System.Windows.Controls.MenuItem PictureFormatResetCropMenuItem = new();
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
        // Font / Font Size combos are now driven entirely through the rendered declarative combos
        // (populated + wired by PopulateAndWireRenderedHomeCombos); only the x:Name mapping survives
        // so RepointBackplaneNamesToRenderedControls can resolve FindName to the on-screen control.
        RibbonBackplaneControlNames["Font"] = "FontNameBox";
        RibbonBackplaneControlNames["Font Size"] = "FontSizeBox";
        RibbonBackplaneControlNames["Bold"] = "BoldButton";
        RibbonBackplaneControlNames["Italic"] = "ItalicButton";
        try { RegisterName("UnderlineButton", UnderlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(UnderlineButton, "Underline");
        RibbonBackplaneControls["Underline"] = UnderlineButton;
        RibbonBackplaneControlNames["Underline"] = "UnderlineButton";
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
        // Number Format combo is driven through the rendered declarative combo as well; keep only the
        // x:Name mapping for FindName resolution.
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
        RibbonBackplaneControlNames["View Gridlines"] = "PageLayoutViewGridlinesChk";
        RibbonBackplaneControlNames["Print Gridlines"] = "PageLayoutPrintGridlinesChk";
        RibbonBackplaneControlNames["View Headings"] = "PageLayoutViewHeadingsChk";
        RibbonBackplaneControlNames["Print Headings"] = "PageLayoutPrintHeadingsChk";
        RibbonBackplaneControlNames["Show Formulas"] = "ShowFormulasButton";
        RibbonBackplaneControlNames["New Comment"] = "ReviewNewThreadedCommentButton";
        RibbonBackplaneControlNames["Delete Comment"] = "ReviewDeleteThreadedCommentButton";
        RibbonBackplaneControlNames["Previous Comment"] = "ReviewPreviousThreadedCommentButton";
        RibbonBackplaneControlNames["Next Comment"] = "ReviewNextThreadedCommentButton";
        RibbonBackplaneControlNames["New Note"] = "ReviewNewNoteButton";
        RibbonBackplaneControlNames["Edit Note"] = "ReviewEditNoteButton";
        RibbonBackplaneControlNames["Delete Note"] = "ReviewDeleteNoteButton";
        RibbonBackplaneControlNames["Previous Note"] = "ReviewPreviousNoteButton";
        RibbonBackplaneControlNames["Next Note"] = "ReviewNextNoteButton";
        RibbonBackplaneControlNames["Protect Sheet"] = "ProtectSheetButton";
        RibbonBackplaneControlNames["Protect Workbook"] = "ProtectWorkbookButton";
        RibbonBackplaneControlNames["Allow Users to Edit Ranges"] = "AllowEditRangesButton";
        RibbonBackplaneControlNames["Normal"] = "ViewNormalButton";
        RibbonBackplaneControlNames["Page Break Preview"] = "ViewPageBreakPreviewButton";
        RibbonBackplaneControlNames["Page Layout"] = "ViewPageLayoutButton";
        RibbonBackplaneControlNames["Gridlines"] = "ViewGridlinesChk";
        RibbonBackplaneControlNames["Headings"] = "ViewHeadersChk";
        RibbonBackplaneControlNames["Ruler"] = "ViewRulerChk";
        RibbonBackplaneControlNames["Formula Bar"] = "ViewFormulaBarChk";
        RibbonBackplaneControlNames["New Window"] = "ViewNewWindowBtn";
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
        RibbonBackplaneControlNames["Total Row"] = "TableDesignTotalRowBtn";
        RibbonBackplaneControlNames["First Column"] = "TableDesignFirstColumnBtn";
        RibbonBackplaneControlNames["Last Column"] = "TableDesignLastColumnBtn";
        RibbonBackplaneControlNames["Banded Rows"] = "TableDesignBandedRowsBtn";
        RibbonBackplaneControlNames["Banded Columns"] = "TableDesignBandedColumnsBtn";
        RibbonBackplaneControlNames["Filter Button"] = "TableDesignFilterButtonBtn";
        try { RegisterName("TableDesignStyleGalleryMenu", TableDesignStyleGalleryMenu); } catch (System.ArgumentException) { }
        try { RegisterName("HelpOnlineButton", HelpOnlineButton); } catch (System.ArgumentException) { }
        RibbonMetadata.SetCommandName(HelpOnlineButton, "Help Online");
        RibbonBackplaneControls["Help Online"] = HelpOnlineButton;
        RibbonBackplaneControlNames["Help Online"] = "HelpOnlineButton";
    }
}
