using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>The Insert Shapes gallery context menu, built imperatively. Attached to the rendered
    /// declarative "Shapes" button once the ribbon is built (see <see cref="AttachInsertShapeGalleryContextMenu"/>),
    /// which happens after this menu is constructed during window load.</summary>
    private ContextMenu? _insertShapeGalleryMenu;

    private void InitializeInsertShapeGalleryContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var group in DrawingInsertionPlanner.ShapeGroups)
        {
            var groupItem = new MenuItem { Header = group.Label };
            RibbonTooltip.SetKeyTip(groupItem, group.KeyTip);
            RibbonMetadata.SetCommandName(groupItem, group.Label);

            foreach (var item in group.Items)
            {
                var shapeItem = new MenuItem
                {
                    Header = item.Label,
                    Tag = item.Kind
                };
                RibbonTooltip.SetKeyTip(shapeItem, group.KeyTip + item.KeyTip);
                RibbonMetadata.SetCommandName(shapeItem, item.Label);
                shapeItem.Click += ShapeGalleryMenuItem_Click;
                groupItem.Items.Add(shapeItem);
            }

            menu.Items.Add(groupItem);
        }

        _insertShapeGalleryMenu = menu;

        // The rendered declarative "Shapes" button may not exist yet (the ribbon is built later in
        // MainWindow_Loaded); AttachInsertShapeGalleryContextMenu is also called from
        // TryApplyDeclarativeRibbon once the rendered controls are collected.
        AttachInsertShapeGalleryContextMenu();
    }

    /// <summary>Attaches the imperatively-built Insert Shapes gallery menu to the rendered declarative
    /// "Shapes" button. No-op until both the menu and the rendered button exist.</summary>
    private void AttachInsertShapeGalleryContextMenu()
    {
        if (_insertShapeGalleryMenu is { } menu &&
            FindRenderedRibbonControl("Shapes") is ButtonBase shapesBtn)
        {
            shapesBtn.ContextMenu = menu;
        }
    }

    private void ShapeGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DrawingShapeKind kind })
            return;

        InsertDrawingShape(kind);
    }
}
