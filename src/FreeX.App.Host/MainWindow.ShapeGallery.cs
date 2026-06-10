using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void InitializeInsertShapeGalleryContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var group in InsertShapeGalleryCatalog.Groups)
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

        ShapesBtn.ContextMenu = menu;
    }

    private void ShapeGalleryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DrawingShapeKind kind })
            return;

        InsertDrawingShape(kind);
    }
}
