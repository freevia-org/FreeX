using Free.Shared.Ribbon;
using FreeP.App.Compositor;

namespace FreeP.Ribbon.Definitions;

/// <summary>
/// The single profile-driven FreeP ribbon catalog. The catalog owns canonical
/// tab, group, command, label, and keytip definitions; profiles contain only
/// host shell placement and presentation overrides.
/// </summary>
public static class FreePRibbon
{
    public static RibbonDefinition Build(FreePRibbonCapabilities? capabilities = null)
    {
        var profile = (capabilities ?? FreePRibbonCapabilities.Wpf).Profile;

        var definition = new RibbonDefinitionBuilder()
            .Tab("home", FreePRibbonText.HomeTabLabel, FreePRibbonText.HomeTabKeyTip,
                tab => AddHomeGroups(tab, profile))
            .Tab("insert", FreePRibbonText.InsertTabLabel, FreePRibbonText.InsertTabKeyTip,
                AddInsertGroups)
            .Tab("design", FreePRibbonText.DesignTab.Label, FreePRibbonText.DesignTab.KeyTip,
                AddDesignGroups)
            .Tab("transitions", FreePRibbonText.TransitionsTab.Label, FreePRibbonText.TransitionsTab.KeyTip,
                tab => AddTransitionGroups(tab, profile))
            .Tab("animations", FreePRibbonText.AnimationsTab.Label, FreePRibbonText.AnimationsTab.KeyTip,
                tab => AddAnimationGroups(tab, profile))
            .Tab("view", FreePRibbonText.ViewTab.Label, FreePRibbonText.ViewTab.KeyTip,
                AddViewGroups)
            .Build();

        return EnsureUnambiguousKeyTips(definition);
    }

    private static RibbonDefinition EnsureUnambiguousKeyTips(RibbonDefinition definition)
    {
        var tabKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tabs = definition.Tabs.Select(tab =>
        {
            var tabKeyTip = MakeUniqueKeyTip(tab.KeyTip, tabKeyTips);
            var groupKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groups = tab.Groups.Select(group =>
            {
                var groupKeyTip = MakeUniqueKeyTip(group.KeyTip, groupKeyTips);
                var controlKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var controls = group.Controls.Select(control =>
                {
                    var normalized = control switch
                    {
                        RibbonSplitButton split => split with { Menu = NormalizeMenuKeyTips(split.Menu) },
                        RibbonDropdown dropdown => dropdown with { Menu = NormalizeMenuKeyTips(dropdown.Menu) },
                        _ => control,
                    };
                    return normalized with
                    {
                        KeyTip = MakeUniqueKeyTip(normalized.KeyTip, controlKeyTips),
                    };
                }).ToArray();

                return group with { KeyTip = groupKeyTip, Controls = controls };
            }).ToArray();

            return tab with { KeyTip = tabKeyTip, Groups = groups };
        }).ToArray();

        return definition with { Tabs = tabs };
    }

    private static RibbonMenu NormalizeMenuKeyTips(RibbonMenu menu)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = menu.Items.Select(item => item with
        {
            KeyTip = MakeUniqueKeyTip(item.KeyTip, used),
            Children = NormalizeMenuItems(item.Children),
        }).ToArray();
        return menu with { Items = items };
    }

    private static IReadOnlyList<RibbonMenuItem> NormalizeMenuItems(
        IReadOnlyList<RibbonMenuItem> source)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return source.Select(item => item with
        {
            KeyTip = MakeUniqueKeyTip(item.KeyTip, used),
            Children = NormalizeMenuItems(item.Children),
        }).ToArray();
    }

    private static string? MakeUniqueKeyTip(string? keyTip, HashSet<string> used)
    {
        if (string.IsNullOrWhiteSpace(keyTip))
            return keyTip;

        var normalized = keyTip.Trim().ToUpperInvariant();
        if (used.Add(normalized))
            return normalized;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{normalized}{suffix}";
            if (normalized.StartsWith("[[", StringComparison.Ordinal) &&
                normalized.EndsWith("]]", StringComparison.Ordinal))
            {
                candidate = $"{normalized[..^2]}{suffix}]]";
            }
            if (used.Add(candidate))
                return candidate;
        }
    }

    private static void AddHomeGroups(RibbonTabBuilder tab, FreePRibbonProfile profile)
    {
        foreach (var groupId in profile.HomeGroups)
        {
            var priority = profile.HomeGroupPriorities[groupId];
            switch (groupId)
            {
                case FreePRibbonHomeGroupId.Slides:
                    tab.Group("slides", FreePRibbonText.SlidesGroupLabel, FreePRibbonText.SlidesGroupKeyTip, priority,
                        group => AddSlidesControls(group, profile));
                    break;
                case FreePRibbonHomeGroupId.Clipboard:
                    tab.Group("clipboard", FreePRibbonText.ClipboardGroupLabel, FreePRibbonText.ClipboardGroupKeyTip,
                        priority, AddClipboardControls);
                    break;
                case FreePRibbonHomeGroupId.Font:
                    tab.Group("font", FreePRibbonText.FontGroupLabel, FreePRibbonText.FontGroupKeyTip, priority,
                        AddFontControls);
                    break;
                case FreePRibbonHomeGroupId.Paragraph:
                    tab.Group("paragraph", FreePRibbonText.ParagraphGroup.Label, FreePRibbonText.ParagraphGroup.KeyTip,
                        priority, AddParagraphControls);
                    break;
                case FreePRibbonHomeGroupId.Arrange:
                    tab.Group("arrange", FreePRibbonText.ArrangeGroup.Label, FreePRibbonText.ArrangeGroup.KeyTip,
                        priority, AddArrangeControls);
                    break;
                case FreePRibbonHomeGroupId.Edit:
                    tab.Group("edit", FreePRibbonText.EditGroupLabel, FreePRibbonText.EditGroupKeyTip, priority,
                        AddEditControls);
                    break;
                case FreePRibbonHomeGroupId.Editing:
                    tab.Group("editing", FreePRibbonText.EditingGroupLabel,
                        profile.HomeGroups.Contains(FreePRibbonHomeGroupId.Edit)
                            ? FreePRibbonText.EditingGroupAvaloniaKeyTip
                            : FreePRibbonText.EditingGroupKeyTip,
                        priority, AddEditingControls);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Unknown FreeP ribbon group.");
            }
        }
    }

    private static void AddSlidesControls(RibbonGroupBuilder group, FreePRibbonProfile profile)
    {
        group.Large("freep.new-slide", FreePRibbonText.NewSlideLabel, RibbonCommandIconKind.Insert,
            profile.NewSlideKeyTip());
        group.Medium("freep.duplicate-slide", FreePRibbonText.DuplicateSlideLabel, RibbonCommandIconKind.Copy,
            FreePRibbonText.DuplicateSlideKeyTip);
        group.Medium("freep.delete-slide", FreePRibbonText.DeleteSlideLabel, RibbonCommandIconKind.Delete,
            FreePRibbonText.DeleteSlideKeyTip);
        group.Medium("freep.layout", FreePRibbonText.LayoutLabel, RibbonCommandIconKind.Grid,
            FreePRibbonText.LayoutKeyTip);
    }

    private static void AddClipboardControls(RibbonGroupBuilder group)
    {
        group.Large("freep.paste", FreePRibbonText.PasteLabel, RibbonCommandIconKind.Paste, FreePRibbonText.PasteKeyTip);
        group.Medium("freep.cut", FreePRibbonText.CutLabel, RibbonCommandIconKind.Cut, FreePRibbonText.CutKeyTip);
        group.Medium("freep.copy", FreePRibbonText.CopyLabel, RibbonCommandIconKind.Copy, FreePRibbonText.CopyKeyTip);
        group.Medium("freep.format-painter", FreePRibbonText.FormatPainterLabel, RibbonCommandIconKind.FormatPainter,
            FreePRibbonText.FormatPainterKeyTip);
    }

    private static void AddFontControls(RibbonGroupBuilder group)
    {
        group.ComboBox("freep.font-family", FreePRibbonText.FontFamilyLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.FontFamilies,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Font),
            KeyTip = FreePRibbonText.FontFamilyKeyTip,
            Width = 140
        });
        group.ComboBox("freep.font-size", FreePRibbonText.FontSizeLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.FontSizes,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size),
            KeyTip = FreePRibbonText.FontSizeKeyTip,
            Width = 64
        });
        group.ComboBox("freep.font-color", FreePRibbonText.FontColorLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.FontColors,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.FontColor, RibbonCommandIconAccent.Color),
            KeyTip = FreePRibbonText.FontColorKeyTip,
            Width = 96
        });
        group.ComboBox("freep.text-autofit", FreePRibbonText.TextAutoFitLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TextAutoFitOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            KeyTip = FreePRibbonText.TextAutoFitKeyTip,
            Width = 160
        });
        group.ComboBox("freep.text-direction", FreePRibbonText.TextDirectionLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TextVerticalTypeOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextBox),
            KeyTip = FreePRibbonText.TextDirectionKeyTip,
            Width = 160
        });
        group.ComboBox("freep.text-columns", FreePRibbonText.TextColumnsLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TextColumnCountOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextColumns),
            KeyTip = FreePRibbonText.TextColumnsKeyTip,
            Width = 96
        });
        group.ComboBox("freep.text-column-spacing", FreePRibbonText.TextColumnSpacingLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TextColumnSpacingOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.TextColumns),
            KeyTip = FreePRibbonText.TextColumnSpacingKeyTip,
            Width = 112
        });
        group.ComboBox("freep.table-cell-fill", FreePRibbonText.TableCellFillLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TableCellFillColors,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Fill, RibbonCommandIconAccent.Color),
            KeyTip = FreePRibbonText.TableCellFillKeyTip,
            Width = 96
        });
        group.ComboBox("freep.table-cell-anchor", FreePRibbonText.TableCellAnchorLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TableCellAnchorOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Align),
            KeyTip = FreePRibbonText.TableCellAnchorKeyTip,
            Width = 100
        });
        group.ComboBox("freep.table-cell-border", FreePRibbonText.TableCellBorderLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TableCellBorderOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Border, RibbonCommandIconAccent.Border),
            KeyTip = FreePRibbonText.TableCellBorderKeyTip,
            Width = 132
        });
        group.ComboBox("freep.table-cell-inset", FreePRibbonText.TableCellInsetLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TableCellInsetOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Margins),
            KeyTip = FreePRibbonText.TableCellInsetKeyTip,
            Width = 132
        });
        group.ComboBox("freep.table-row-height", FreePRibbonText.TableRowHeightLabel, control => control with
        {
            Items = FreePRibbonDefinitionData.TableRowHeightOptions,
            Icon = new RibbonCommandIcon(RibbonCommandIconKind.Size),
            KeyTip = FreePRibbonText.TableRowHeightKeyTip,
            Width = 100
        });
        group.Medium(TableCellEditPlanner.MergeCellsCommandId,
            FreePRibbonText.TableMergeCellsLabel,
            RibbonCommandIconKind.Merge,
            FreePRibbonText.TableMergeCellsKeyTip);
        group.Medium(TableCellEditPlanner.SplitCellCommandId,
            FreePRibbonText.TableSplitCellLabel,
            RibbonCommandIconKind.Table,
            FreePRibbonText.TableSplitCellKeyTip);
        group.IconToggle(TableCellEditPlanner.TableFirstRowCommandId,
            FreePRibbonText.TableFirstRowLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableFirstRowKeyTip);
        group.IconToggle(TableCellEditPlanner.TableLastRowCommandId,
            FreePRibbonText.TableLastRowLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableLastRowKeyTip);
        group.IconToggle(TableCellEditPlanner.TableFirstColCommandId,
            FreePRibbonText.TableFirstColLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableFirstColKeyTip);
        group.IconToggle(TableCellEditPlanner.TableLastColCommandId,
            FreePRibbonText.TableLastColLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableLastColKeyTip);
        group.IconToggle(TableCellEditPlanner.TableBandRowCommandId,
            FreePRibbonText.TableBandRowLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableBandRowKeyTip);
        group.IconToggle(TableCellEditPlanner.TableBandColCommandId,
            FreePRibbonText.TableBandColLabel, RibbonCommandIconKind.Table,
            FreePRibbonText.TableBandColKeyTip);
        group.IconToggle("freep.bold", FreePRibbonText.BoldLabel, RibbonCommandIconKind.Bold, FreePRibbonText.BoldKeyTip);
        group.IconToggle("freep.italic", FreePRibbonText.ItalicLabel, RibbonCommandIconKind.Italic, FreePRibbonText.ItalicKeyTip);
        group.IconToggle("freep.underline", FreePRibbonText.UnderlineLabel, RibbonCommandIconKind.Underline,
            FreePRibbonText.UnderlineKeyTip);
        group.IconToggle("freep.superscript", FreePRibbonText.SuperscriptLabel, RibbonCommandIconKind.Superscript,
            FreePRibbonText.SuperscriptKeyTip);
        group.IconToggle("freep.subscript", FreePRibbonText.SubscriptLabel, RibbonCommandIconKind.Subscript,
            FreePRibbonText.SubscriptKeyTip);
    }

    private static void AddEditControls(RibbonGroupBuilder group)
    {
        group.Large("freep.undo", FreePRibbonText.UndoLabel, RibbonCommandIconKind.Undo, FreePRibbonText.UndoKeyTip);
        group.Large("freep.redo", FreePRibbonText.RedoLabel, RibbonCommandIconKind.Redo, FreePRibbonText.RedoKeyTip);
    }

    private static void AddEditingControls(RibbonGroupBuilder group)
    {
        group.Large("freep.find", FreePRibbonText.FindLabel, RibbonCommandIconKind.Search, FreePRibbonText.FindKeyTip);
        group.Medium("freep.replace", FreePRibbonText.ReplaceLabel, RibbonCommandIconKind.Refresh, FreePRibbonText.ReplaceKeyTip);
    }

    private static void AddSlideShowControls(RibbonGroupBuilder group, FreePRibbonProfile profile)
    {
        group.Large("freep.slideshow.from-beginning", FreePRibbonText.SlideShowFromBeginningLabel,
            RibbonCommandIconKind.Next, FreePRibbonText.SlideShowFromBeginningKeyTip);
        group.Large("freep.slideshow.from-current-slide", FreePRibbonText.SlideShowFromCurrentSlideLabel,
            profile.SlideShowFromCurrentSlideIcon, FreePRibbonText.SlideShowFromCurrentSlideKeyTip);
        group.Medium("freep.slideshow.rehearse-timings", FreePRibbonText.SlideShowRehearseTimingsLabel,
            RibbonCommandIconKind.Watch, FreePRibbonText.SlideShowRehearseTimingsKeyTip);
        group.Medium("freep.slideshow.record-timings", FreePRibbonText.SlideShowRecordTimingsLabel,
            RibbonCommandIconKind.Watch, FreePRibbonText.SlideShowRecordTimingsKeyTip);
        group.Medium("freep.slideshow.custom-shows", FreePRibbonText.SlideShowCustomShowsLabel,
            RibbonCommandIconKind.List, FreePRibbonText.SlideShowCustomShowsKeyTip);
    }

    private static void AddArrangeControls(RibbonGroupBuilder group)
    {
        group.Large("freep.arrange.group", FreePRibbonText.ArrangeGroupCommand.Label, RibbonCommandIconKind.Group,
            FreePRibbonText.ArrangeGroupCommand.KeyTip);
        group.Medium("freep.arrange.ungroup", FreePRibbonText.ArrangeUngroupCommand.Label, RibbonCommandIconKind.Ungroup,
            FreePRibbonText.ArrangeUngroupCommand.KeyTip);
        group.Medium(ShapeChangePlanner.MenuCommandId, FreePRibbonText.ArrangeChangeShapeCommand.Label,
            RibbonCommandIconKind.RibbonShape, FreePRibbonText.ArrangeChangeShapeCommand.KeyTip,
            dropdown: true, menu: BuildChangeShapeMenu);
        group.Medium(OleActivationPlanner.OpenEmbeddedObjectCommandId,
            FreePRibbonText.OpenEmbeddedObjectCommand.Label, RibbonCommandIconKind.RibbonShape,
            FreePRibbonText.OpenEmbeddedObjectCommand.KeyTip);
        group.IconToggle("freep.arrange.edit-points", FreePRibbonText.ArrangeEditPointsCommand.Label,
            RibbonCommandIconKind.Diamond, FreePRibbonText.ArrangeEditPointsCommand.KeyTip);
        group.Separator();
        group.Medium("freep.arrange.bring-to-front", FreePRibbonText.ArrangeBringToFrontCommand.Label,
            RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringToFrontCommand.KeyTip);
        group.Medium("freep.arrange.bring-forward", FreePRibbonText.ArrangeBringForwardCommand.Label,
            RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeBringForwardCommand.KeyTip);
        group.Medium("freep.arrange.send-backward", FreePRibbonText.ArrangeSendBackwardCommand.Label,
            RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendBackwardCommand.KeyTip);
        group.Medium("freep.arrange.send-to-back", FreePRibbonText.ArrangeSendToBackCommand.Label,
            RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeSendToBackCommand.KeyTip);
        group.Medium("freep.arrange.flip-horizontal", FreePRibbonText.ArrangeFlipHorizontalCommand.Label,
            RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.ArrangeFlipHorizontalCommand.KeyTip);
        group.Medium("freep.arrange.flip-vertical", FreePRibbonText.ArrangeFlipVerticalCommand.Label,
            RibbonCommandIconKind.Align, FreePRibbonText.ArrangeFlipVerticalCommand.KeyTip);
        group.Medium("freep.arrange.rotate-left-90", FreePRibbonText.ArrangeRotateLeft90Command.Label,
            RibbonCommandIconKind.Rotate, FreePRibbonText.ArrangeRotateLeft90Command.KeyTip);
        group.Medium("freep.arrange.rotate-right-90", FreePRibbonText.ArrangeRotateRight90Command.Label,
            RibbonCommandIconKind.Rotate, FreePRibbonText.ArrangeRotateRight90Command.KeyTip);
        group.Medium(RotationOptionsPlanner.CommandId, FreePRibbonText.ArrangeRotationOptionsCommand.Label,
            RibbonCommandIconKind.Rotate, FreePRibbonText.ArrangeRotationOptionsCommand.KeyTip);
        group.Separator();
        group.Medium("freep.arrange.align-left", FreePRibbonText.ArrangeAlignLeftCommand.Label,
            RibbonCommandIconKind.AlignLeft, FreePRibbonText.ArrangeAlignLeftCommand.KeyTip);
        group.Medium("freep.arrange.align-center-h", FreePRibbonText.ArrangeAlignCenterHorizontalCommand.Label,
            RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeAlignCenterHorizontalCommand.KeyTip);
        group.Medium("freep.arrange.align-right", FreePRibbonText.ArrangeAlignRightCommand.Label,
            RibbonCommandIconKind.AlignRight, FreePRibbonText.ArrangeAlignRightCommand.KeyTip);
        group.Medium("freep.arrange.align-top", FreePRibbonText.ArrangeAlignTopCommand.Label,
            RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeAlignTopCommand.KeyTip);
        group.Medium("freep.arrange.align-middle", FreePRibbonText.ArrangeAlignMiddleCommand.Label,
            RibbonCommandIconKind.Align, FreePRibbonText.ArrangeAlignMiddleCommand.KeyTip);
        group.Medium("freep.arrange.align-bottom", FreePRibbonText.ArrangeAlignBottomCommand.Label,
            RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeAlignBottomCommand.KeyTip);
        group.Medium("freep.arrange.align-left-to-slide", FreePRibbonText.ArrangeAlignLeftToSlideCommand.Label,
            RibbonCommandIconKind.AlignLeft, FreePRibbonText.ArrangeAlignLeftToSlideCommand.KeyTip);
        group.Medium("freep.arrange.align-center-h-to-slide", FreePRibbonText.ArrangeAlignCenterHorizontalToSlideCommand.Label,
            RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeAlignCenterHorizontalToSlideCommand.KeyTip);
        group.Medium("freep.arrange.align-right-to-slide", FreePRibbonText.ArrangeAlignRightToSlideCommand.Label,
            RibbonCommandIconKind.AlignRight, FreePRibbonText.ArrangeAlignRightToSlideCommand.KeyTip);
        group.Medium("freep.arrange.align-top-to-slide", FreePRibbonText.ArrangeAlignTopToSlideCommand.Label,
            RibbonCommandIconKind.ArrowUp, FreePRibbonText.ArrangeAlignTopToSlideCommand.KeyTip);
        group.Medium("freep.arrange.align-middle-to-slide", FreePRibbonText.ArrangeAlignMiddleToSlideCommand.Label,
            RibbonCommandIconKind.Align, FreePRibbonText.ArrangeAlignMiddleToSlideCommand.KeyTip);
        group.Medium("freep.arrange.align-bottom-to-slide", FreePRibbonText.ArrangeAlignBottomToSlideCommand.Label,
            RibbonCommandIconKind.ArrowDown, FreePRibbonText.ArrangeAlignBottomToSlideCommand.KeyTip);
        group.Separator();
        group.Medium("freep.arrange.distribute-h", FreePRibbonText.ArrangeDistributeHorizontalCommand.Label,
            RibbonCommandIconKind.AlignCenter, FreePRibbonText.ArrangeDistributeHorizontalCommand.KeyTip);
        group.Medium("freep.arrange.distribute-v", FreePRibbonText.ArrangeDistributeVerticalCommand.Label,
            RibbonCommandIconKind.Align, FreePRibbonText.ArrangeDistributeVerticalCommand.KeyTip);
    }

    private static void BuildChangeShapeMenu(RibbonMenuBuilder menu)
    {
        menu.Item(ShapeChangePlanner.RectangleCommandId,
            FreePRibbonText.ArrangeChangeShapeRectangleCommand.Label,
            FreePRibbonText.ArrangeChangeShapeRectangleCommand.KeyTip);
        menu.Item(ShapeChangePlanner.RoundedRectangleCommandId,
            FreePRibbonText.ShapeRoundedRectangleLabel,
            FreePRibbonText.ShapeRoundedRectangleKeyTip);
        menu.Item(ShapeChangePlanner.EllipseCommandId,
            FreePRibbonText.ArrangeChangeShapeEllipseCommand.Label,
            FreePRibbonText.ArrangeChangeShapeEllipseCommand.KeyTip);
        menu.Item(ShapeChangePlanner.TriangleCommandId,
            FreePRibbonText.ArrangeChangeShapeTriangleCommand.Label,
            FreePRibbonText.ArrangeChangeShapeTriangleCommand.KeyTip);
        menu.Item(ShapeChangePlanner.DiamondCommandId,
            FreePRibbonText.ArrangeChangeShapeDiamondCommand.Label,
            FreePRibbonText.ArrangeChangeShapeDiamondCommand.KeyTip);
        menu.Item(ShapeChangePlanner.RightArrowCommandId,
            FreePRibbonText.ArrangeChangeShapeRightArrowCommand.Label,
            FreePRibbonText.ArrangeChangeShapeRightArrowCommand.KeyTip);
        menu.Item(ShapeChangePlanner.HexagonCommandId,
            FreePRibbonText.ShapeHexagonLabel,
            FreePRibbonText.ShapeHexagonKeyTip);
        menu.Item(ShapeChangePlanner.ParallelogramCommandId,
            FreePRibbonText.ShapeParallelogramLabel,
            FreePRibbonText.ShapeParallelogramKeyTip);
        menu.Item(ShapeChangePlanner.TrapezoidCommandId,
            FreePRibbonText.ShapeTrapezoidLabel,
            FreePRibbonText.ShapeTrapezoidKeyTip);
        menu.Item(ShapeChangePlanner.LeftArrowCommandId,
            FreePRibbonText.ShapeLeftArrowLabel,
            FreePRibbonText.ShapeLeftArrowKeyTip);
        menu.Item(ShapeChangePlanner.Star5CommandId,
            FreePRibbonText.ShapeStar5Label,
            FreePRibbonText.ShapeStar5KeyTip);
        menu.Item(ShapeChangePlanner.UpArrowCommandId,
            FreePRibbonText.ShapeUpArrowLabel,
            FreePRibbonText.ShapeUpArrowKeyTip);
        menu.Item(ShapeChangePlanner.DownArrowCommandId,
            FreePRibbonText.ShapeDownArrowLabel,
            FreePRibbonText.ShapeDownArrowKeyTip);
        menu.Item(ShapeChangePlanner.CrossCommandId,
            FreePRibbonText.ArrangeChangeShapeCrossCommand.Label,
            FreePRibbonText.ArrangeChangeShapeCrossCommand.KeyTip);
        menu.Item(ShapeChangePlanner.PlusSignCommandId,
            FreePRibbonText.ArrangeChangeShapePlusSignCommand.Label,
            FreePRibbonText.ArrangeChangeShapePlusSignCommand.KeyTip);
        menu.Item(ShapeChangePlanner.PentagonCommandId, FreePRibbonText.ShapePentagonLabel, FreePRibbonText.ShapePentagonKeyTip);
        menu.Item(ShapeChangePlanner.OctagonCommandId, FreePRibbonText.ShapeOctagonLabel, FreePRibbonText.ShapeOctagonKeyTip);
        menu.Item(ShapeChangePlanner.LeftRightArrowCommandId, FreePRibbonText.ShapeLeftRightArrowLabel, FreePRibbonText.ShapeLeftRightArrowKeyTip);
        menu.Item(ShapeChangePlanner.UpDownArrowCommandId, FreePRibbonText.ShapeUpDownArrowLabel, FreePRibbonText.ShapeUpDownArrowKeyTip);
        menu.Item(ShapeChangePlanner.Star8CommandId, FreePRibbonText.ShapeStar8Label, FreePRibbonText.ShapeStar8KeyTip);
        menu.Item(ShapeChangePlanner.ChevronCommandId, FreePRibbonText.ShapeChevronLabel, FreePRibbonText.ShapeChevronKeyTip);
        menu.Item(ShapeChangePlanner.HomePlateCommandId, FreePRibbonText.ShapeHomePlateLabel, FreePRibbonText.ShapeHomePlateKeyTip);
        menu.Item(ShapeChangePlanner.RightTriangleCommandId, FreePRibbonText.ShapeRightTriangleLabel, FreePRibbonText.ShapeRightTriangleKeyTip);
        menu.Item(ShapeChangePlanner.MinusSignCommandId, FreePRibbonText.ShapeMinusSignLabel, FreePRibbonText.ShapeMinusSignKeyTip);
        menu.Item(ShapeChangePlanner.MultiplySignCommandId, FreePRibbonText.ShapeMultiplySignLabel, FreePRibbonText.ShapeMultiplySignKeyTip);
        menu.Item(ShapeChangePlanner.DivideSignCommandId, FreePRibbonText.ShapeDivideSignLabel, FreePRibbonText.ShapeDivideSignKeyTip);
        menu.Item(ShapeChangePlanner.EqualSignCommandId, FreePRibbonText.ShapeEqualSignLabel, FreePRibbonText.ShapeEqualSignKeyTip);
        menu.Item(ShapeChangePlanner.NotEqualSignCommandId, FreePRibbonText.ShapeNotEqualSignLabel, FreePRibbonText.ShapeNotEqualSignKeyTip);
        menu.Item(ShapeChangePlanner.WaveCommandId, FreePRibbonText.ShapeWaveLabel, FreePRibbonText.ShapeWaveKeyTip);
        menu.Item(ShapeChangePlanner.RectangularCalloutCommandId, FreePRibbonText.ShapeRectangularCalloutLabel, FreePRibbonText.ShapeRectangularCalloutKeyTip);
        menu.Item(ShapeChangePlanner.RoundedRectangularCalloutCommandId, FreePRibbonText.ShapeRoundedRectangularCalloutLabel, FreePRibbonText.ShapeRoundedRectangularCalloutKeyTip);
        menu.Item(ShapeChangePlanner.OvalCalloutCommandId, FreePRibbonText.ShapeOvalCalloutLabel, FreePRibbonText.ShapeOvalCalloutKeyTip);
        menu.Item(ShapeChangePlanner.ExplosionCommandId, FreePRibbonText.ShapeExplosionLabel, FreePRibbonText.ShapeExplosionKeyTip);
        menu.Item(ShapeChangePlanner.RibbonCommandId, FreePRibbonText.ShapeRibbonLabel, FreePRibbonText.ShapeRibbonKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartProcessCommandId, FreePRibbonText.ShapeFlowchartProcessLabel, FreePRibbonText.ShapeFlowchartProcessKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartDecisionCommandId, FreePRibbonText.ShapeFlowchartDecisionLabel, FreePRibbonText.ShapeFlowchartDecisionKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartDataCommandId, FreePRibbonText.ShapeFlowchartDataLabel, FreePRibbonText.ShapeFlowchartDataKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartPredefinedProcessCommandId, FreePRibbonText.ShapeFlowchartPredefinedProcessLabel, FreePRibbonText.ShapeFlowchartPredefinedProcessKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartDocumentCommandId, FreePRibbonText.ShapeFlowchartDocumentLabel, FreePRibbonText.ShapeFlowchartDocumentKeyTip);
        menu.Item(ShapeChangePlanner.FlowchartTerminatorCommandId, FreePRibbonText.ShapeFlowchartTerminatorLabel, FreePRibbonText.ShapeFlowchartTerminatorKeyTip);
        menu.Item(ShapeChangePlanner.LineCalloutCommandId, FreePRibbonText.ShapeLineCalloutLabel, FreePRibbonText.ShapeLineCalloutKeyTip);
        menu.Item(ShapeChangePlanner.CylinderCommandId, FreePRibbonText.ShapeCylinderLabel, FreePRibbonText.ShapeCylinderKeyTip);
        menu.Item(ShapeChangePlanner.ChordCommandId, FreePRibbonText.ShapeChordLabel, FreePRibbonText.ShapeChordKeyTip);
        menu.Item(ShapeChangePlanner.HeartCommandId, FreePRibbonText.ShapeHeartLabel, FreePRibbonText.ShapeHeartKeyTip);
    }

    private static void AddInsertGroups(RibbonTabBuilder tab)
    {
        tab.Group("text", FreePRibbonText.TextGroupLabel, FreePRibbonText.TextGroupKeyTip, 100, group =>
        {
            group.Large("freep.text-box", FreePRibbonText.TextBoxLabel, RibbonCommandIconKind.TextBox, FreePRibbonText.TextBoxKeyTip);
            group.Medium("freep.header-footer", FreePRibbonText.HeaderFooterLabel, RibbonCommandIconKind.HeaderFooter, FreePRibbonText.HeaderFooterKeyTip);
            group.Medium("freep.date-time", FreePRibbonText.DateTimeLabel, RibbonCommandIconKind.Date, FreePRibbonText.DateTimeKeyTip);
            group.Medium("freep.slide-number", FreePRibbonText.SlideNumberLabel, RibbonCommandIconKind.PageNumber, FreePRibbonText.SlideNumberKeyTip);
        });
        tab.Group("tables", FreePRibbonText.TablesGroupLabel, FreePRibbonText.TablesGroupKeyTip, 95, group =>
        {
            group.Large("freep.insert-table-3x3", FreePRibbonText.InsertTable3x3Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable3x3KeyTip);
            group.Medium("freep.insert-table-2x2", FreePRibbonText.InsertTable2x2Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable2x2KeyTip);
            group.Medium("freep.insert-table-4x4", FreePRibbonText.InsertTable4x4Label, RibbonCommandIconKind.Table, FreePRibbonText.InsertTable4x4KeyTip);
        });
        tab.Group("charts", FreePRibbonText.ChartsGroupLabel, FreePRibbonText.ChartsGroupKeyTip, 93, group =>
        {
            group.Medium("freep.insert-chart-column", FreePRibbonText.InsertChartColumnLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartColumnKeyTip);
            group.Medium("freep.insert-chart-bar", FreePRibbonText.InsertChartBarLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.InsertChartBarKeyTip);
            group.Medium("freep.insert-chart-line", FreePRibbonText.InsertChartLineLabel, RibbonCommandIconKind.ChartLine, FreePRibbonText.InsertChartLineKeyTip);
            group.Medium("freep.insert-chart-pie", FreePRibbonText.InsertChartPieLabel, RibbonCommandIconKind.ChartPie, FreePRibbonText.InsertChartPieKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartColumnStackedCommandId,
                FreePRibbonText.InsertChartColumnStackedLabel, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartColumnStackedKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartColumnStacked100CommandId,
                FreePRibbonText.InsertChartColumnStacked100Label, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartColumnStacked100KeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartBarStackedCommandId,
                FreePRibbonText.InsertChartBarStackedLabel, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartBarStackedKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartBarStacked100CommandId,
                FreePRibbonText.InsertChartBarStacked100Label, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartBarStacked100KeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartLineMarkersCommandId,
                FreePRibbonText.InsertChartLineMarkersLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartLineMarkersKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartAreaCommandId,
                FreePRibbonText.InsertChartAreaLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartAreaKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartAreaStackedCommandId,
                FreePRibbonText.InsertChartAreaStackedLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartAreaStackedKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartScatterCommandId,
                FreePRibbonText.InsertChartScatterLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartScatterKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartDoughnutCommandId,
                FreePRibbonText.InsertChartDoughnutLabel, RibbonCommandIconKind.ChartPie,
                FreePRibbonText.InsertChartDoughnutKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartRadarCommandId,
                FreePRibbonText.InsertChartRadarLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartRadarKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartBubbleCommandId,
                FreePRibbonText.InsertChartBubbleLabel, RibbonCommandIconKind.ChartPie,
                FreePRibbonText.InsertChartBubbleKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartStockCommandId,
                FreePRibbonText.InsertChartStockLabel, RibbonCommandIconKind.ChartLine,
                FreePRibbonText.InsertChartStockKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartSurfaceCommandId,
                FreePRibbonText.InsertChartSurfaceLabel, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartSurfaceKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChartSurface3DCommandId,
                FreePRibbonText.InsertChartSurface3DLabel, RibbonCommandIconKind.ChartColumn,
                FreePRibbonText.InsertChartSurface3DKeyTip);
            group.Dropdown(
                ChartDataDialogPlanner.ChangeChartTypeCommandId,
                FreePRibbonText.ChartChangeTypeLabel,
                BuildChartTypeMenu(),
                dropdown => dropdown with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.ChartColumn),
                    KeyTip = FreePRibbonText.ChartChangeTypeKeyTip
                });
            group.Medium("freep.chart.edit-data", FreePRibbonText.ChartEditDataLabel, RibbonCommandIconKind.ChartTitle, FreePRibbonText.ChartEditDataKeyTip);
            group.Medium(ChartDisplayOptionsPlanner.CommandId, FreePRibbonText.ChartDisplayOptionsLabel, RibbonCommandIconKind.Effects, FreePRibbonText.ChartDisplayOptionsKeyTip);
            group.Medium(ChartAxisOptionsPlanner.CommandId, FreePRibbonText.ChartAxisOptionsLabel, RibbonCommandIconKind.ChartTitle, FreePRibbonText.ChartAxisOptionsKeyTip);
            group.Medium(ChartSeriesOptionsPlanner.CommandId, FreePRibbonText.ChartSeriesOptionsLabel, RibbonCommandIconKind.Effects, FreePRibbonText.ChartSeriesOptionsKeyTip);
            group.Medium(ChartPointOptionsPlanner.CommandId, FreePRibbonText.ChartPointOptionsLabel, RibbonCommandIconKind.Effects, FreePRibbonText.ChartPointOptionsKeyTip);
            group.Medium(ChartLayoutOptionsPlanner.CommandId, FreePRibbonText.ChartLayoutOptionsLabel, RibbonCommandIconKind.ChartTitle, FreePRibbonText.ChartLayoutOptionsKeyTip);
            group.Medium(ChartDataTableOptionsPlanner.CommandId, FreePRibbonText.ChartDataTableOptionsLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.ChartDataTableOptionsKeyTip);
            group.Medium(ChartBubbleOptionsPlanner.CommandId, FreePRibbonText.ChartBubbleOptionsLabel, RibbonCommandIconKind.ChartPie, FreePRibbonText.ChartBubbleOptionsKeyTip);
            group.Medium(ChartPieOptionsPlanner.CommandId, FreePRibbonText.ChartPieOptionsLabel, RibbonCommandIconKind.ChartPie, FreePRibbonText.ChartPieOptionsKeyTip);
            group.Medium(ChartPlotStyleOptionsPlanner.CommandId, FreePRibbonText.ChartPlotStyleOptionsLabel, RibbonCommandIconKind.Effects, FreePRibbonText.ChartPlotStyleOptionsKeyTip);
            group.Medium(Chart3DViewOptionsPlanner.CommandId, FreePRibbonText.Chart3DViewOptionsLabel, RibbonCommandIconKind.ChartColumn, FreePRibbonText.Chart3DViewOptionsKeyTip);
            group.Medium(ChartTextOptionsPlanner.CommandId, FreePRibbonText.ChartTextOptionsLabel, RibbonCommandIconKind.Font, FreePRibbonText.ChartTextOptionsKeyTip);
            group.Medium(ChartAreaOptionsPlanner.CommandId, FreePRibbonText.ChartAreaOptionsLabel, RibbonCommandIconKind.Color, FreePRibbonText.ChartAreaOptionsKeyTip);
            group.Medium(ChartProtectionOptionsPlanner.CommandId, FreePRibbonText.ChartProtectionOptionsLabel, RibbonCommandIconKind.Protect, FreePRibbonText.ChartProtectionOptionsKeyTip);
        });
        tab.Group("smartart-insert", FreePRibbonText.SmartArtLayoutsGroup.Label, FreePRibbonText.SmartArtLayoutsGroup.KeyTip, 91, group =>
        {
            group.Large(SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId,
                FreePRibbonText.SmartArtBasicProcessCommand.Label,
                RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtBasicProcessCommand.KeyTip,
                dropdown: true,
                menu: BuildSmartArtInsertMenu);
        });
        tab.Group("links", FreePRibbonText.LinksGroupLabel, FreePRibbonText.LinksGroupKeyTip, 92, group =>
        {
            group.Large("freep.insert-link", FreePRibbonText.InsertLinkLabel, RibbonCommandIconKind.Link, FreePRibbonText.InsertLinkKeyTip);
            group.Medium("freep.remove-link", FreePRibbonText.RemoveLinkLabel, RibbonCommandIconKind.Delete, FreePRibbonText.RemoveLinkKeyTip);
        });
        tab.Group("illustrations", FreePRibbonText.IllustrationsGroupLabel, FreePRibbonText.IllustrationsGroupKeyTip, 90, group =>
        {
            group.Large("freep.picture", FreePRibbonText.PictureLabel, RibbonCommandIconKind.Picture, FreePRibbonText.PictureKeyTip);
            group.Medium("freep.video", FreePRibbonText.VideoLabel, RibbonCommandIconKind.Picture, FreePRibbonText.VideoKeyTip);
            group.Medium("freep.audio", FreePRibbonText.AudioLabel, RibbonCommandIconKind.Picture, FreePRibbonText.AudioKeyTip);
            group.Medium(PictureCropAuthoringPlanner.InsetCommandId, FreePRibbonText.PictureCropInsetCommand.Label,
                RibbonCommandIconKind.Picture, FreePRibbonText.PictureCropInsetCommand.KeyTip);
            group.Medium(PictureCropAuthoringPlanner.ResetCommandId, FreePRibbonText.PictureCropResetCommand.Label,
                RibbonCommandIconKind.Picture, FreePRibbonText.PictureCropResetCommand.KeyTip);
            group.Medium(PictureColorEffectAuthoringPlanner.GrayscaleCommandId,
                FreePRibbonText.PictureGrayscaleCommand.Label, RibbonCommandIconKind.Color,
                FreePRibbonText.PictureGrayscaleCommand.KeyTip);
            group.Medium(PictureColorEffectAuthoringPlanner.ResetCommandId,
                FreePRibbonText.PictureEffectsResetCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.PictureEffectsResetCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.NoneCommandId,
                FreePRibbonText.ShapeShadowNoneCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.ShapeShadowNoneCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.SubtleCommandId,
                FreePRibbonText.ShapeShadowSubtleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeShadowSubtleCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.OffsetCommandId,
                FreePRibbonText.ShapeShadowOffsetCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeShadowOffsetCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.GlowNoneCommandId,
                FreePRibbonText.ShapeGlowNoneCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.ShapeGlowNoneCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.GlowSubtleCommandId,
                FreePRibbonText.ShapeGlowSubtleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeGlowSubtleCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.GlowStrongCommandId,
                FreePRibbonText.ShapeGlowStrongCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeGlowStrongCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.SoftEdgeNoneCommandId,
                FreePRibbonText.ShapeSoftEdgeNoneCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.ShapeSoftEdgeNoneCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.SoftEdgeSubtleCommandId,
                FreePRibbonText.ShapeSoftEdgeSubtleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeSoftEdgeSubtleCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.SoftEdgeStrongCommandId,
                FreePRibbonText.ShapeSoftEdgeStrongCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeSoftEdgeStrongCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.BevelNoneCommandId,
                FreePRibbonText.ShapeBevelNoneCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.ShapeBevelNoneCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.BevelSubtleCommandId,
                FreePRibbonText.ShapeBevelSubtleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeBevelSubtleCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.BevelStrongCommandId,
                FreePRibbonText.ShapeBevelStrongCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.ShapeBevelStrongCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.Shape3dNoneCommandId,
                FreePRibbonText.Shape3dNoneCommand.Label, RibbonCommandIconKind.Delete,
                FreePRibbonText.Shape3dNoneCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.Shape3dSubtleCommandId,
                FreePRibbonText.Shape3dSubtleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.Shape3dSubtleCommand.KeyTip);
            group.Medium(ShapeEffectAuthoringPlanner.Shape3dStrongCommandId,
                FreePRibbonText.Shape3dStrongCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.Shape3dStrongCommand.KeyTip);
            group.Medium(OleInsertionPlanner.InsertEmbeddedObjectCommandId,
                FreePRibbonText.InsertEmbeddedObjectCommand.Label, RibbonCommandIconKind.RibbonShape,
                FreePRibbonText.InsertEmbeddedObjectCommand.KeyTip);
            group.Medium("freep.shape-rectangle", FreePRibbonText.ShapeRectangleLabel, RibbonCommandIconKind.Rectangle, FreePRibbonText.ShapeRectangleKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RoundedRectangleCommandId, FreePRibbonText.ShapeRoundedRectangleLabel,
                RibbonCommandIconKind.Rectangle, FreePRibbonText.ShapeRoundedRectangleKeyTip);
            group.Medium("freep.shape-ellipse", FreePRibbonText.ShapeEllipseLabel, RibbonCommandIconKind.Ellipse, FreePRibbonText.ShapeEllipseKeyTip);
            group.Medium(SlideObjectInsertionPlanner.TriangleCommandId, FreePRibbonText.ShapeTriangleLabel,
                RibbonCommandIconKind.Triangle, FreePRibbonText.ShapeTriangleKeyTip);
            group.Medium(SlideObjectInsertionPlanner.DiamondCommandId, FreePRibbonText.ShapeDiamondLabel,
                RibbonCommandIconKind.Diamond, FreePRibbonText.ShapeDiamondKeyTip);
            group.Medium(SlideObjectInsertionPlanner.HexagonCommandId, FreePRibbonText.ShapeHexagonLabel,
                RibbonCommandIconKind.Pentagon, FreePRibbonText.ShapeHexagonKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ParallelogramCommandId, FreePRibbonText.ShapeParallelogramLabel,
                RibbonCommandIconKind.Diamond, FreePRibbonText.ShapeParallelogramKeyTip);
            group.Medium(SlideObjectInsertionPlanner.TrapezoidCommandId, FreePRibbonText.ShapeTrapezoidLabel,
                RibbonCommandIconKind.Pentagon, FreePRibbonText.ShapeTrapezoidKeyTip);
            group.Medium(SlideObjectInsertionPlanner.LeftArrowCommandId, FreePRibbonText.ShapeLeftArrowLabel,
                RibbonCommandIconKind.ArrowLeft, FreePRibbonText.ShapeLeftArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RightArrowCommandId, FreePRibbonText.ShapeRightArrowLabel,
                RibbonCommandIconKind.ArrowRight, FreePRibbonText.ShapeRightArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.UpArrowCommandId, FreePRibbonText.ShapeUpArrowLabel,
                RibbonCommandIconKind.ArrowUp, FreePRibbonText.ShapeUpArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.DownArrowCommandId, FreePRibbonText.ShapeDownArrowLabel,
                RibbonCommandIconKind.ArrowDown, FreePRibbonText.ShapeDownArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.Star5CommandId, FreePRibbonText.ShapeStar5Label,
                RibbonCommandIconKind.Star, FreePRibbonText.ShapeStar5KeyTip);
            group.Medium(SlideObjectInsertionPlanner.CrossCommandId, FreePRibbonText.ShapeCrossLabel,
                RibbonCommandIconKind.Cross, FreePRibbonText.ShapeCrossKeyTip);
            group.Medium(SlideObjectInsertionPlanner.PlusSignCommandId, FreePRibbonText.ShapePlusSignLabel,
                RibbonCommandIconKind.PlusSign, FreePRibbonText.ShapePlusSignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.PentagonCommandId, FreePRibbonText.ShapePentagonLabel,
                RibbonCommandIconKind.Pentagon, FreePRibbonText.ShapePentagonKeyTip);
            group.Medium(SlideObjectInsertionPlanner.OctagonCommandId, FreePRibbonText.ShapeOctagonLabel,
                RibbonCommandIconKind.Octagon, FreePRibbonText.ShapeOctagonKeyTip);
            group.Medium(SlideObjectInsertionPlanner.LeftRightArrowCommandId, FreePRibbonText.ShapeLeftRightArrowLabel,
                RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.ShapeLeftRightArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.UpDownArrowCommandId, FreePRibbonText.ShapeUpDownArrowLabel,
                RibbonCommandIconKind.ArrowUpDown, FreePRibbonText.ShapeUpDownArrowKeyTip);
            group.Medium(SlideObjectInsertionPlanner.Star8CommandId, FreePRibbonText.ShapeStar8Label,
                RibbonCommandIconKind.Star, FreePRibbonText.ShapeStar8KeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChevronCommandId, FreePRibbonText.ShapeChevronLabel,
                RibbonCommandIconKind.Pentagon, FreePRibbonText.ShapeChevronKeyTip);
            group.Medium(SlideObjectInsertionPlanner.HomePlateCommandId, FreePRibbonText.ShapeHomePlateLabel,
                RibbonCommandIconKind.Pentagon, FreePRibbonText.ShapeHomePlateKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RightTriangleCommandId, FreePRibbonText.ShapeRightTriangleLabel,
                RibbonCommandIconKind.Triangle, FreePRibbonText.ShapeRightTriangleKeyTip);
            group.Medium(SlideObjectInsertionPlanner.MinusSignCommandId, FreePRibbonText.ShapeMinusSignLabel,
                RibbonCommandIconKind.MinusSign, FreePRibbonText.ShapeMinusSignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.MultiplySignCommandId, FreePRibbonText.ShapeMultiplySignLabel,
                RibbonCommandIconKind.MultiplySign, FreePRibbonText.ShapeMultiplySignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.DivideSignCommandId, FreePRibbonText.ShapeDivideSignLabel,
                RibbonCommandIconKind.DivideSign, FreePRibbonText.ShapeDivideSignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.EqualSignCommandId, FreePRibbonText.ShapeEqualSignLabel,
                RibbonCommandIconKind.EqualSign, FreePRibbonText.ShapeEqualSignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.NotEqualSignCommandId, FreePRibbonText.ShapeNotEqualSignLabel,
                RibbonCommandIconKind.NotEqualSign, FreePRibbonText.ShapeNotEqualSignKeyTip);
            group.Medium(SlideObjectInsertionPlanner.WaveCommandId, FreePRibbonText.ShapeWaveLabel,
                RibbonCommandIconKind.Wave, FreePRibbonText.ShapeWaveKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RectangularCalloutCommandId, FreePRibbonText.ShapeRectangularCalloutLabel,
                RibbonCommandIconKind.Callout, FreePRibbonText.ShapeRectangularCalloutKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RoundedRectangularCalloutCommandId, FreePRibbonText.ShapeRoundedRectangularCalloutLabel,
                RibbonCommandIconKind.Callout, FreePRibbonText.ShapeRoundedRectangularCalloutKeyTip);
            group.Medium(SlideObjectInsertionPlanner.OvalCalloutCommandId, FreePRibbonText.ShapeOvalCalloutLabel,
                RibbonCommandIconKind.Callout, FreePRibbonText.ShapeOvalCalloutKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ExplosionCommandId, FreePRibbonText.ShapeExplosionLabel,
                RibbonCommandIconKind.Explosion, FreePRibbonText.ShapeExplosionKeyTip);
            group.Medium(SlideObjectInsertionPlanner.RibbonCommandId, FreePRibbonText.ShapeRibbonLabel,
                RibbonCommandIconKind.RibbonShape, FreePRibbonText.ShapeRibbonKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartProcessCommandId, FreePRibbonText.ShapeFlowchartProcessLabel,
                RibbonCommandIconKind.FlowchartProcess, FreePRibbonText.ShapeFlowchartProcessKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartDecisionCommandId, FreePRibbonText.ShapeFlowchartDecisionLabel,
                RibbonCommandIconKind.FlowchartDecision, FreePRibbonText.ShapeFlowchartDecisionKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartDataCommandId, FreePRibbonText.ShapeFlowchartDataLabel,
                RibbonCommandIconKind.FlowchartData, FreePRibbonText.ShapeFlowchartDataKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartPredefinedProcessCommandId, FreePRibbonText.ShapeFlowchartPredefinedProcessLabel,
                RibbonCommandIconKind.FlowchartProcess, FreePRibbonText.ShapeFlowchartPredefinedProcessKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartDocumentCommandId, FreePRibbonText.ShapeFlowchartDocumentLabel,
                RibbonCommandIconKind.FlowchartDocument, FreePRibbonText.ShapeFlowchartDocumentKeyTip);
            group.Medium(SlideObjectInsertionPlanner.FlowchartTerminatorCommandId, FreePRibbonText.ShapeFlowchartTerminatorLabel,
                RibbonCommandIconKind.FlowchartTerminator, FreePRibbonText.ShapeFlowchartTerminatorKeyTip);
            group.Medium(SlideObjectInsertionPlanner.LineCalloutCommandId, FreePRibbonText.ShapeLineCalloutLabel,
                RibbonCommandIconKind.LineCallout, FreePRibbonText.ShapeLineCalloutKeyTip);
            group.Medium(SlideObjectInsertionPlanner.CylinderCommandId, FreePRibbonText.ShapeCylinderLabel,
                RibbonCommandIconKind.Rectangle, FreePRibbonText.ShapeCylinderKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ChordCommandId, FreePRibbonText.ShapeChordLabel,
                RibbonCommandIconKind.Diamond, FreePRibbonText.ShapeChordKeyTip);
            group.Medium(SlideObjectInsertionPlanner.HeartCommandId, FreePRibbonText.ShapeHeartLabel,
                RibbonCommandIconKind.RibbonShape, FreePRibbonText.ShapeHeartKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ConnectorCommandId, FreePRibbonText.ConnectorLabel,
                RibbonCommandIconKind.Line, FreePRibbonText.ConnectorKeyTip);
            group.Medium(SlideObjectInsertionPlanner.ElbowConnectorCommandId, FreePRibbonText.ElbowConnectorLabel,
                RibbonCommandIconKind.Line, FreePRibbonText.ElbowConnectorKeyTip);
            group.Medium(SlideObjectInsertionPlanner.CurvedConnectorCommandId, FreePRibbonText.CurvedConnectorLabel,
                RibbonCommandIconKind.Line, FreePRibbonText.CurvedConnectorKeyTip);
        });
    }

    private static void BuildSmartArtInsertMenu(RibbonMenuBuilder menu)
    {
        var index = 0;
        foreach (var preset in SlideObjectInsertionPlanner.InsertableSmartArtLayouts)
        {
            menu.Item(
                preset == SmartArtLayoutPreset.BasicProcess
                    ? SlideObjectInsertionPlanner.SmartArtBasicProcessCommandId
                    : SlideObjectInsertionPlanner.SmartArtLayoutCommandId(preset),
                SlideObjectInsertionPlanner.SmartArtLayoutDisplayName(preset),
                $"S{++index}");
        }
    }

    private static void AddDesignGroups(RibbonTabBuilder tab)
    {
        tab.Group("themes", FreePRibbonText.ThemesGroup.Label, FreePRibbonText.ThemesGroup.KeyTip, 100, group =>
        {
            group.Large("freep.theme.office", FreePRibbonText.ThemeOfficeCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeOfficeCommand.KeyTip);
            group.Medium("freep.theme.berlin", FreePRibbonText.ThemeBerlinCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeBerlinCommand.KeyTip);
            group.Medium("freep.theme.facet", FreePRibbonText.ThemeFacetCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeFacetCommand.KeyTip);
            group.Medium("freep.theme.ion", FreePRibbonText.ThemeIonCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeIonCommand.KeyTip);
            group.Medium("freep.theme.slice", FreePRibbonText.ThemeSliceCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.ThemeSliceCommand.KeyTip);
        });
        tab.Group("customize", FreePRibbonText.CustomizeGroup.Label, FreePRibbonText.CustomizeGroup.KeyTip, 90, group =>
        {
            group.Large("freep.slide-size-16x9", FreePRibbonText.SlideSizeWidescreenCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeWidescreenCommand.KeyTip);
            group.Large("freep.slide-size-4x3", FreePRibbonText.SlideSizeStandardCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeStandardCommand.KeyTip);
            group.Medium("freep.slide-size-custom", FreePRibbonText.SlideSizeCustomCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.SlideSizeCustomCommand.KeyTip);
            group.Medium("freep.background-white", FreePRibbonText.BackgroundWhiteCommand.Label, RibbonCommandIconKind.Fill, FreePRibbonText.BackgroundWhiteCommand.KeyTip);
            group.Medium("freep.background-black", FreePRibbonText.BackgroundBlackCommand.Label, RibbonCommandIconKind.Fill, FreePRibbonText.BackgroundBlackCommand.KeyTip);
            group.Medium("freep.background-blue", FreePRibbonText.BackgroundBlueCommand.Label, RibbonCommandIconKind.Fill, FreePRibbonText.BackgroundBlueCommand.KeyTip);
            group.Medium("freep.background-reset", FreePRibbonText.BackgroundResetCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.BackgroundResetCommand.KeyTip);
        });
        tab.Group("smartart-colors", FreePRibbonText.SmartArtColorsGroup.Label, FreePRibbonText.SmartArtColorsGroup.KeyTip, 80, group =>
        {
            group.Dropdown(
                SmartArtAuthoringPlanner.SmartArtColorsGalleryCommandId,
                "Change Colors",
                BuildSmartArtColorGalleryMenu(),
                dropdown => dropdown with
                {
                    PreferredLayout = RibbonCommandLayoutKind.Medium,
                    Icon = new RibbonCommandIcon(RibbonCommandIconKind.Color),
                    KeyTip = "CC"
                });
            group.Medium(SmartArtAuthoringPlanner.ThemeAccentsCommandId,
                FreePRibbonText.SmartArtThemeAccentsCommand.Label, RibbonCommandIconKind.Color,
                FreePRibbonText.SmartArtThemeAccentsCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.SingleAccentCommandId,
                FreePRibbonText.SmartArtSingleAccentCommand.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtSingleAccentCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MonochromaticAccent2CommandId,
                FreePRibbonText.SmartArtMonochromaticAccent2Command.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtMonochromaticAccent2Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MonochromaticAccent3CommandId,
                FreePRibbonText.SmartArtMonochromaticAccent3Command.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtMonochromaticAccent3Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MonochromaticAccent4CommandId,
                FreePRibbonText.SmartArtMonochromaticAccent4Command.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtMonochromaticAccent4Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MonochromaticAccent5CommandId,
                FreePRibbonText.SmartArtMonochromaticAccent5Command.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtMonochromaticAccent5Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MonochromaticAccent6CommandId,
                FreePRibbonText.SmartArtMonochromaticAccent6Command.Label, RibbonCommandIconKind.Fill,
                FreePRibbonText.SmartArtMonochromaticAccent6Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.GrayscaleCommandId,
                FreePRibbonText.SmartArtGrayscaleCommand.Label, RibbonCommandIconKind.Clear,
                FreePRibbonText.SmartArtGrayscaleCommand.KeyTip);
        });
        tab.Group("smartart-layouts", FreePRibbonText.SmartArtLayoutsGroup.Label, FreePRibbonText.SmartArtLayoutsGroup.KeyTip, 80, group =>
        {
            group.Medium(SmartArtAuthoringPlanner.BasicProcessLayoutCommandId,
                FreePRibbonText.SmartArtBasicProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtBasicProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.AccentProcessLayoutCommandId,
                FreePRibbonText.SmartArtAccentProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtAccentProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.AscendingProcessLayoutCommandId,
                FreePRibbonText.SmartArtAscendingProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtAscendingProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.DescendingProcessLayoutCommandId,
                FreePRibbonText.SmartArtDescendingProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtDescendingProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicTimelineLayoutCommandId,
                FreePRibbonText.SmartArtBasicTimelineCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtBasicTimelineCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.CircleAccentTimelineLayoutCommandId,
                FreePRibbonText.SmartArtCircleAccentTimelineCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtCircleAccentTimelineCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PhasedProcessLayoutCommandId,
                FreePRibbonText.SmartArtPhasedProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtPhasedProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.StepDownProcessLayoutCommandId,
                FreePRibbonText.SmartArtStepDownProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtStepDownProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ContinuousBlockProcessLayoutCommandId,
                FreePRibbonText.SmartArtContinuousBlockProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtContinuousBlockProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.SegmentedProcessLayoutCommandId,
                FreePRibbonText.SmartArtSegmentedProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtSegmentedProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ChevronProcessLayoutCommandId,
                FreePRibbonText.SmartArtChevronProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtChevronProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicChevronProcessLayoutCommandId,
                FreePRibbonText.SmartArtBasicChevronProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtBasicChevronProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ClosedChevronProcessLayoutCommandId,
                FreePRibbonText.SmartArtClosedChevronProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtClosedChevronProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BendingProcessLayoutCommandId,
                FreePRibbonText.SmartArtBendingProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtBendingProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.AlternatingProcessLayoutCommandId,
                FreePRibbonText.SmartArtAlternatingProcessCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtAlternatingProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ArrowRibbonLayoutCommandId,
                FreePRibbonText.SmartArtArrowRibbonCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtArrowRibbonCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.CircleProcessLayoutCommandId,
                FreePRibbonText.SmartArtCircleProcessCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtCircleProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.CircleArrowProcessLayoutCommandId,
                FreePRibbonText.SmartArtCircleArrowProcessCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtCircleArrowProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.FunnelProcessLayoutCommandId,
                FreePRibbonText.SmartArtFunnelProcessCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtFunnelProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalProcessLayoutCommandId,
                FreePRibbonText.SmartArtVerticalProcessCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtVerticalProcessCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalBoxListLayoutCommandId,
                FreePRibbonText.SmartArtVerticalBoxListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtVerticalBoxListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalBlockListLayoutCommandId,
                FreePRibbonText.SmartArtVerticalBlockListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtVerticalBlockListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalChevronListLayoutCommandId,
                FreePRibbonText.SmartArtVerticalChevronListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtVerticalChevronListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalArrowListLayoutCommandId,
                FreePRibbonText.SmartArtVerticalArrowListCommand.Label, RibbonCommandIconKind.ArrowDown,
                FreePRibbonText.SmartArtVerticalArrowListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.VerticalBulletListLayoutCommandId,
                FreePRibbonText.SmartArtVerticalBulletListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtVerticalBulletListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.HorizontalBulletListLayoutCommandId,
                FreePRibbonText.SmartArtHorizontalBulletListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtHorizontalBulletListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.HorizontalBlockListLayoutCommandId,
                FreePRibbonText.SmartArtHorizontalBlockListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtHorizontalBlockListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.TrapezoidListLayoutCommandId,
                FreePRibbonText.SmartArtTrapezoidListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtTrapezoidListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicCycleLayoutCommandId,
                FreePRibbonText.SmartArtBasicCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtBasicCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MultidirectionalCycleLayoutCommandId,
                FreePRibbonText.SmartArtMultidirectionalCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtMultidirectionalCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.Cycle2LayoutCommandId,
                FreePRibbonText.SmartArtCycle2Command.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtCycle2Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ContinuousCycleLayoutCommandId,
                FreePRibbonText.SmartArtContinuousCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtContinuousCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.GearCycleLayoutCommandId,
                FreePRibbonText.SmartArtGearCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtGearCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.TextCycleLayoutCommandId,
                FreePRibbonText.SmartArtTextCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtTextCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BlockCycleLayoutCommandId,
                FreePRibbonText.SmartArtBlockCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtBlockCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.NonDirectionalCycleLayoutCommandId,
                FreePRibbonText.SmartArtNonDirectionalCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtNonDirectionalCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicBlockListLayoutCommandId,
                FreePRibbonText.SmartArtBasicBlockListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtBasicBlockListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicListLayoutCommandId,
                FreePRibbonText.SmartArtBasicListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtBasicListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.List2LayoutCommandId,
                FreePRibbonText.SmartArtList2Command.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtList2Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.StackedListLayoutCommandId,
                FreePRibbonText.SmartArtStackedListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtStackedListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.DescendingBlockListLayoutCommandId,
                FreePRibbonText.SmartArtDescendingBlockListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtDescendingBlockListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicPyramidLayoutCommandId,
                FreePRibbonText.SmartArtBasicPyramidCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtBasicPyramidCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PyramidListLayoutCommandId,
                FreePRibbonText.SmartArtPyramidListCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtPyramidListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.InvertedPyramidLayoutCommandId,
                FreePRibbonText.SmartArtInvertedPyramidCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtInvertedPyramidCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.RadialCycleLayoutCommandId,
                FreePRibbonText.SmartArtRadialCycleCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtRadialCycleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicRadialLayoutCommandId,
                FreePRibbonText.SmartArtBasicRadialCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtBasicRadialCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.RadialClusterLayoutCommandId,
                FreePRibbonText.SmartArtRadialClusterCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtRadialClusterCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.RadialListLayoutCommandId,
                FreePRibbonText.SmartArtRadialListCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtRadialListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicMatrixLayoutCommandId,
                FreePRibbonText.SmartArtBasicMatrixCommand.Label, RibbonCommandIconKind.Grid,
                FreePRibbonText.SmartArtBasicMatrixCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.TitledMatrixLayoutCommandId,
                FreePRibbonText.SmartArtTitledMatrixCommand.Label, RibbonCommandIconKind.Grid,
                FreePRibbonText.SmartArtTitledMatrixCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.GridMatrixLayoutCommandId,
                FreePRibbonText.SmartArtGridMatrixCommand.Label, RibbonCommandIconKind.Grid,
                FreePRibbonText.SmartArtGridMatrixCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicRelationshipLayoutCommandId,
                FreePRibbonText.SmartArtBasicRelationshipCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtBasicRelationshipCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.OpposingIdeasLayoutCommandId,
                FreePRibbonText.SmartArtOpposingIdeasCommand.Label, RibbonCommandIconKind.ArrowRight,
                FreePRibbonText.SmartArtOpposingIdeasCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ConvergingRadialLayoutCommandId,
                FreePRibbonText.SmartArtConvergingRadialCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtConvergingRadialCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.DivergingRadialLayoutCommandId,
                FreePRibbonText.SmartArtDivergingRadialCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtDivergingRadialCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicVennLayoutCommandId,
                FreePRibbonText.SmartArtBasicVennCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtBasicVennCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.RadialVennLayoutCommandId,
                FreePRibbonText.SmartArtRadialVennCommand.Label, RibbonCommandIconKind.Refresh,
                FreePRibbonText.SmartArtRadialVennCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.TargetListLayoutCommandId,
                FreePRibbonText.SmartArtTargetListCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtTargetListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.StackedVennLayoutCommandId,
                FreePRibbonText.SmartArtStackedVennCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtStackedVennCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.InterlockingRingsLayoutCommandId,
                FreePRibbonText.SmartArtInterlockingRingsCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtInterlockingRingsCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BasicHierarchyLayoutCommandId,
                FreePRibbonText.SmartArtBasicHierarchyCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtBasicHierarchyCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.Hierarchy3LayoutCommandId,
                FreePRibbonText.SmartArtHierarchy3Command.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtHierarchy3Command.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.HorizontalHierarchyLayoutCommandId,
                FreePRibbonText.SmartArtHorizontalHierarchyCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtHorizontalHierarchyCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.OrgChartLayoutCommandId,
                FreePRibbonText.SmartArtOrgChartCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtOrgChartCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.NameAndTitleOrgChartLayoutCommandId,
                FreePRibbonText.SmartArtNameAndTitleOrgChartCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtNameAndTitleOrgChartCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureCaptionListLayoutCommandId,
                FreePRibbonText.SmartArtPictureCaptionListCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureCaptionListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureAccentListLayoutCommandId,
                FreePRibbonText.SmartArtPictureAccentListCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureAccentListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureStackLayoutCommandId,
                FreePRibbonText.SmartArtPictureStackCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureStackCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureLineupLayoutCommandId,
                FreePRibbonText.SmartArtPictureLineupCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureLineupCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureStripsLayoutCommandId,
                FreePRibbonText.SmartArtPictureStripsCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureStripsCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ContinuousPictureListLayoutCommandId,
                FreePRibbonText.SmartArtContinuousPictureListCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtContinuousPictureListCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PictureGridLayoutCommandId,
                FreePRibbonText.SmartArtPictureGridCommand.Label, RibbonCommandIconKind.Picture,
                FreePRibbonText.SmartArtPictureGridCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.LabeledHierarchyLayoutCommandId,
                FreePRibbonText.SmartArtLabeledHierarchyCommand.Label, RibbonCommandIconKind.List,
                FreePRibbonText.SmartArtLabeledHierarchyCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.TableHierarchyLayoutCommandId,
                FreePRibbonText.SmartArtTableHierarchyCommand.Label, RibbonCommandIconKind.Grid,
                FreePRibbonText.SmartArtTableHierarchyCommand.KeyTip);
        });
        tab.Group("smartart-styles", FreePRibbonText.SmartArtStylesGroup.Label, FreePRibbonText.SmartArtStylesGroup.KeyTip, 80, group =>
        {
            group.Medium(SmartArtAuthoringPlanner.SimpleQuickStyleCommandId,
                FreePRibbonText.SmartArtSimpleStyleCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtSimpleStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ModerateQuickStyleCommandId,
                FreePRibbonText.SmartArtModerateStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtModerateStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.IntenseQuickStyleCommandId,
                FreePRibbonText.SmartArtIntenseStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtIntenseStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.SubtleQuickStyleCommandId,
                FreePRibbonText.SmartArtSubtleStyleCommand.Label, RibbonCommandIconKind.Rectangle,
                FreePRibbonText.SmartArtSubtleStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.SoftEdgeQuickStyleCommandId,
                FreePRibbonText.SmartArtSoftEdgeStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtSoftEdgeStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.InsertQuickStyleCommandId,
                FreePRibbonText.SmartArtInsertStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtInsertStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.CartoonQuickStyleCommandId,
                FreePRibbonText.SmartArtCartoonStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtCartoonStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PowderQuickStyleCommandId,
                FreePRibbonText.SmartArtPowderStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtPowderStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.PolishedQuickStyleCommandId,
                FreePRibbonText.SmartArtPolishedStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtPolishedStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BrickSceneQuickStyleCommandId,
                FreePRibbonText.SmartArtBrickSceneStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtBrickSceneStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.FlatSceneQuickStyleCommandId,
                FreePRibbonText.SmartArtFlatSceneStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtFlatSceneStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.MetallicSceneQuickStyleCommandId,
                FreePRibbonText.SmartArtMetallicSceneStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtMetallicSceneStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.SunsetSceneQuickStyleCommandId,
                FreePRibbonText.SmartArtSunsetSceneStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtSunsetSceneStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.BirdsEyeSceneQuickStyleCommandId,
                FreePRibbonText.SmartArtBirdsEyeSceneStyleCommand.Label, RibbonCommandIconKind.Effects,
                FreePRibbonText.SmartArtBirdsEyeSceneStyleCommand.KeyTip);
            group.Medium(SmartArtAuthoringPlanner.ConvertToShapesCommandId,
                FreePRibbonText.SmartArtConvertToShapesCommand.Label, RibbonCommandIconKind.Group,
                FreePRibbonText.SmartArtConvertToShapesCommand.KeyTip);
        });
    }

    private static void AddTransitionGroups(RibbonTabBuilder tab, FreePRibbonProfile profile)
    {
        tab.Group("transition-gallery", FreePRibbonText.TransitionGalleryGroup.Label, FreePRibbonText.TransitionGalleryGroup.KeyTip, 100, group =>
        {
            group.Medium("freep.transition.none", FreePRibbonText.TransitionNoneCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.TransitionNoneCommand.KeyTip);
            group.Medium("freep.transition.fade", FreePRibbonText.TransitionFadeCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.TransitionFadeCommand.KeyTip);
            group.Medium("freep.transition.push", FreePRibbonText.TransitionPushCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.TransitionPushCommand.KeyTip);
            group.Medium("freep.transition.wipe", FreePRibbonText.TransitionWipeCommand.Label, RibbonCommandIconKind.ArrowLeft, FreePRibbonText.TransitionWipeCommand.KeyTip);
            group.Medium("freep.transition.split", FreePRibbonText.TransitionSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.TransitionSplitCommand.KeyTip);
            group.Medium("freep.transition.box", FreePRibbonText.TransitionBoxCommand.Label, RibbonCommandIconKind.Rectangle, FreePRibbonText.TransitionBoxCommand.KeyTip);
            group.Medium("freep.transition.doors", FreePRibbonText.TransitionDoorsCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.TransitionDoorsCommand.KeyTip);
            group.Medium("freep.transition.reveal", FreePRibbonText.TransitionRevealCommand.Label, RibbonCommandIconKind.Expand, FreePRibbonText.TransitionRevealCommand.KeyTip);
            group.Medium("freep.transition.flash", FreePRibbonText.TransitionFlashCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.TransitionFlashCommand.KeyTip);
            group.Medium("freep.transition.morph", FreePRibbonText.TransitionMorphCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.TransitionMorphCommand.KeyTip);
            group.Medium("freep.transition.cut", FreePRibbonText.TransitionCutCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.TransitionCutCommand.KeyTip);
            group.Medium("freep.transition.cover", FreePRibbonText.TransitionCoverCommand.Label, RibbonCommandIconKind.Page, FreePRibbonText.TransitionCoverCommand.KeyTip);
            group.Medium("freep.transition.uncover", FreePRibbonText.TransitionUncoverCommand.Label, RibbonCommandIconKind.Expand, FreePRibbonText.TransitionUncoverCommand.KeyTip);
            group.Medium("freep.transition.blinds", FreePRibbonText.TransitionBlindsCommand.Label, RibbonCommandIconKind.View, FreePRibbonText.TransitionBlindsCommand.KeyTip);
            group.Medium("freep.transition.comb", FreePRibbonText.TransitionCombCommand.Label, RibbonCommandIconKind.Grid, FreePRibbonText.TransitionCombCommand.KeyTip);
            group.Medium("freep.transition.random-bars", FreePRibbonText.TransitionRandomBarsCommand.Label, RibbonCommandIconKind.Grid, FreePRibbonText.TransitionRandomBarsCommand.KeyTip);
            group.Medium("freep.transition.strips", FreePRibbonText.TransitionStripsCommand.Label, RibbonCommandIconKind.TextColumns, FreePRibbonText.TransitionStripsCommand.KeyTip);
            group.Medium("freep.transition.wheel-reverse", FreePRibbonText.TransitionWheelReverseCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.TransitionWheelReverseCommand.KeyTip);
            group.Medium("freep.transition.gallery", FreePRibbonText.TransitionGalleryCommand.Label, RibbonCommandIconKind.Grid, FreePRibbonText.TransitionGalleryCommand.KeyTip);
            group.Medium("freep.transition.conveyor", FreePRibbonText.TransitionConveyorCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.TransitionConveyorCommand.KeyTip);
            group.Medium("freep.transition.pan", FreePRibbonText.TransitionPanCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.TransitionPanCommand.KeyTip);
            group.Medium("freep.transition.window", FreePRibbonText.TransitionWindowCommand.Label, RibbonCommandIconKind.Window, FreePRibbonText.TransitionWindowCommand.KeyTip);
            group.Medium("freep.transition.dissolve", FreePRibbonText.TransitionDissolveCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.TransitionDissolveCommand.KeyTip);
            group.Medium("freep.transition.zoom", FreePRibbonText.TransitionZoomCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.TransitionZoomCommand.KeyTip);
            group.Medium("freep.transition.wheel", FreePRibbonText.TransitionWheelCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.TransitionWheelCommand.KeyTip);
        });
        tab.Group("transition-more", FreePRibbonText.TransitionMoreGroup.Label, "M", 95, group =>
        {
            group.Medium(
                "freep.transition.more",
                FreePRibbonText.TransitionMoreCommand.Label,
                RibbonCommandIconKind.Effects,
                "M",
                dropdown: true,
                menu: BuildExtendedTransitionMenu);
        });
        tab.Group("transition-timing", FreePRibbonText.TransitionTimingGroup.Label, FreePRibbonText.TransitionTimingGroup.KeyTip, 90, group =>
        {
            group.ComboBox("freep.transition.duration", FreePRibbonText.TransitionDurationCommand.Label, control => control with
            {
                Items = FreePRibbonDefinitionData.TransitionDurations,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                KeyTip = FreePRibbonText.TransitionDurationCommand.KeyTip,
                Width = 90
            });
            group.MediumToggle("freep.transition.advance-on-click", FreePRibbonText.TransitionAdvanceOnClickCommand.Label,
                RibbonCommandIconKind.Next, FreePRibbonText.TransitionAdvanceOnClickCommand.KeyTip);
            group.ComboBox("freep.transition.advance-after", FreePRibbonText.TransitionAdvanceAfterCommand.Label, control => control with
            {
                Items = FreePRibbonDefinitionData.TransitionAdvanceAfterOptions,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                KeyTip = FreePRibbonText.TransitionAdvanceAfterCommand.KeyTip,
                Width = 90
            });
            group.Medium("freep.transition.apply-all", FreePRibbonText.TransitionApplyAllCommand.Label,
                RibbonCommandIconKind.Refresh, FreePRibbonText.TransitionApplyAllCommand.KeyTip);
            group.Medium("freep.transition.sound", FreePRibbonText.TransitionSoundCommand.Label,
                RibbonCommandIconKind.Picture, FreePRibbonText.TransitionSoundCommand.KeyTip);
            group.Medium("freep.transition.sound-none", FreePRibbonText.TransitionSoundNoneCommand.Label,
                RibbonCommandIconKind.Clear, FreePRibbonText.TransitionSoundNoneCommand.KeyTip);
            group.MediumToggle("freep.transition.sound-loop", FreePRibbonText.TransitionSoundLoopCommand.Label,
                RibbonCommandIconKind.Refresh, FreePRibbonText.TransitionSoundLoopCommand.KeyTip);
        });
        tab.Group("slideshow-from-transitions", FreePRibbonText.SlideShowGroupLabel,
            profile.SlideShowGroupKeyTip(), 80, group => AddSlideShowControls(group, profile));
    }

    private static void AddAnimationGroups(RibbonTabBuilder tab, FreePRibbonProfile profile)
    {
        tab.Group("animation-effects", FreePRibbonText.AnimationEffectsGroup.Label, FreePRibbonText.AnimationEffectsGroup.KeyTip, 100, group =>
        {
            group.Medium("freep.anim.entrance.appear", FreePRibbonText.AnimationEntranceAppearCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEntranceAppearCommand.KeyTip);
            group.Medium("freep.anim.entrance.fade", FreePRibbonText.AnimationEntranceFadeCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationEntranceFadeCommand.KeyTip);
            group.Medium("freep.anim.entrance.fly-in", FreePRibbonText.AnimationEntranceFlyInCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.AnimationEntranceFlyInCommand.KeyTip);
            group.Medium("freep.anim.entrance.wipe", FreePRibbonText.AnimationEntranceWipeCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.AnimationEntranceWipeCommand.KeyTip);
            group.Medium("freep.anim.entrance.zoom", FreePRibbonText.AnimationEntranceZoomCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.AnimationEntranceZoomCommand.KeyTip);
            group.Medium("freep.anim.entrance.split", FreePRibbonText.AnimationEntranceSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.AnimationEntranceSplitCommand.KeyTip);
            group.Medium("freep.anim.entrance.blinds", FreePRibbonText.AnimationEntranceBlindsCommand.Label, RibbonCommandIconKind.Grid, FreePRibbonText.AnimationEntranceBlindsCommand.KeyTip, dropdown: true, menu: BuildAdvancedEntranceAnimationMenu);
            if (profile.IncludeAnimationSeparators) group.Separator();
            group.Medium("freep.anim.emphasis.pulse", FreePRibbonText.AnimationEmphasisPulseCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEmphasisPulseCommand.KeyTip);
            group.Medium("freep.anim.emphasis.spin", FreePRibbonText.AnimationEmphasisSpinCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.AnimationEmphasisSpinCommand.KeyTip);
            group.Medium("freep.anim.emphasis.grow-shrink", FreePRibbonText.AnimationEmphasisGrowShrinkCommand.Label, RibbonCommandIconKind.Scale, FreePRibbonText.AnimationEmphasisGrowShrinkCommand.KeyTip);
            group.Medium("freep.anim.emphasis.teeter", FreePRibbonText.AnimationEmphasisTeeterCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.AnimationEmphasisTeeterCommand.KeyTip);
            group.Medium("freep.anim.emphasis.blink", FreePRibbonText.AnimationEmphasisBlinkCommand.Label, RibbonCommandIconKind.Flash, FreePRibbonText.AnimationEmphasisBlinkCommand.KeyTip);
            group.Medium("freep.anim.emphasis.color-pulse", FreePRibbonText.AnimationEmphasisColorPulseCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.AnimationEmphasisColorPulseCommand.KeyTip);
            group.Medium("freep.anim.emphasis.change-color", FreePRibbonText.AnimationEmphasisChangeColorCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.AnimationEmphasisChangeColorCommand.KeyTip);
            group.Medium("freep.anim.emphasis.grow-with-color", FreePRibbonText.AnimationEmphasisGrowWithColorCommand.Label, RibbonCommandIconKind.Color, FreePRibbonText.AnimationEmphasisGrowWithColorCommand.KeyTip);
            group.Medium("freep.anim.emphasis.wave", FreePRibbonText.AnimationEmphasisWaveCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationEmphasisWaveCommand.KeyTip);
            group.Medium("freep.anim.emphasis.shimmer", FreePRibbonText.AnimationEmphasisShimmerCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationEmphasisShimmerCommand.KeyTip);
            group.Medium("freep.anim.emphasis.bold", FreePRibbonText.AnimationEmphasisBoldCommand.Label, RibbonCommandIconKind.Bold, FreePRibbonText.AnimationEmphasisBoldCommand.KeyTip);
            group.Medium("freep.anim.emphasis.underline", FreePRibbonText.AnimationEmphasisUnderlineCommand.Label, RibbonCommandIconKind.Underline, FreePRibbonText.AnimationEmphasisUnderlineCommand.KeyTip);
            if (profile.IncludeAnimationSeparators) group.Separator();
            group.Medium("freep.anim.exit.disappear", FreePRibbonText.AnimationExitDisappearCommand.Label, RibbonCommandIconKind.Delete, FreePRibbonText.AnimationExitDisappearCommand.KeyTip);
            group.Medium("freep.anim.exit.fade-out", FreePRibbonText.AnimationExitFadeOutCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationExitFadeOutCommand.KeyTip);
            group.Medium("freep.anim.exit.fly-out", FreePRibbonText.AnimationExitFlyOutCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.AnimationExitFlyOutCommand.KeyTip);
            group.Medium("freep.anim.exit.wipe", FreePRibbonText.AnimationExitWipeCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.AnimationExitWipeCommand.KeyTip);
            group.Medium("freep.anim.exit.split", FreePRibbonText.AnimationExitSplitCommand.Label, RibbonCommandIconKind.ArrowLeftRight, FreePRibbonText.AnimationExitSplitCommand.KeyTip);
            group.Medium("freep.anim.exit.zoom-out", FreePRibbonText.AnimationExitZoomOutCommand.Label, RibbonCommandIconKind.Zoom, FreePRibbonText.AnimationExitZoomOutCommand.KeyTip);
            group.Medium("freep.anim.exit.blinds", FreePRibbonText.AnimationExitBlindsCommand.Label, RibbonCommandIconKind.Grid, FreePRibbonText.AnimationExitBlindsCommand.KeyTip, dropdown: true, menu: BuildAdvancedExitAnimationMenu);
            if (profile.IncludeAnimationSeparators) group.Separator();
            group.Medium("freep.anim.none", FreePRibbonText.AnimationNoneCommand.Label, RibbonCommandIconKind.Clear, FreePRibbonText.AnimationNoneCommand.KeyTip);
        });
        tab.Group("animation-motion-paths", FreePRibbonText.AnimationMotionPathGroup.Label, FreePRibbonText.AnimationMotionPathGroup.KeyTip, 85, group =>
        {
            group.Medium("freep.anim.motion.right", FreePRibbonText.AnimationMotionRightCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.AnimationMotionRightCommand.KeyTip);
            group.Medium("freep.anim.motion.left", FreePRibbonText.AnimationMotionLeftCommand.Label, RibbonCommandIconKind.ArrowLeft, FreePRibbonText.AnimationMotionLeftCommand.KeyTip);
            group.Medium("freep.anim.motion.up", FreePRibbonText.AnimationMotionUpCommand.Label, RibbonCommandIconKind.ArrowUp, FreePRibbonText.AnimationMotionUpCommand.KeyTip);
            group.Medium("freep.anim.motion.down", FreePRibbonText.AnimationMotionDownCommand.Label, RibbonCommandIconKind.ArrowDown, FreePRibbonText.AnimationMotionDownCommand.KeyTip);
            group.Medium("freep.anim.motion.arc-right", FreePRibbonText.AnimationMotionArcRightCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionArcRightCommand.KeyTip);
            group.Medium("freep.anim.motion.arc-left", FreePRibbonText.AnimationMotionArcLeftCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionArcLeftCommand.KeyTip);
            group.Medium("freep.anim.motion.arc-up", FreePRibbonText.AnimationMotionArcUpCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionArcUpCommand.KeyTip);
            group.Medium("freep.anim.motion.arc-down", FreePRibbonText.AnimationMotionArcDownCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionArcDownCommand.KeyTip);
            group.Medium("freep.anim.motion.circle", FreePRibbonText.AnimationMotionCircleCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionCircleCommand.KeyTip);
            group.Medium("freep.anim.motion.loop", FreePRibbonText.AnimationMotionLoopCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionLoopCommand.KeyTip);
            group.Medium("freep.anim.motion.s", FreePRibbonText.AnimationMotionSCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionSCommand.KeyTip);
            group.Medium("freep.anim.motion.figure-eight", FreePRibbonText.AnimationMotionFigureEightCommand.Label, RibbonCommandIconKind.Effects, FreePRibbonText.AnimationMotionFigureEightCommand.KeyTip);
            group.Medium("freep.anim.motion.reverse", FreePRibbonText.AnimationMotionReverseCommand.Label, RibbonCommandIconKind.Rotate, FreePRibbonText.AnimationMotionReverseCommand.KeyTip);
        });
        tab.Group("animation-timing", FreePRibbonText.AnimationTimingGroup.Label, FreePRibbonText.AnimationTimingGroup.KeyTip, 90, group =>
        {
            group.ComboBox("freep.anim.trigger", FreePRibbonText.AnimationTriggerCommand.Label, control => control with
            {
                Items = FreePRibbonDefinitionData.AnimationTriggers,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.Next),
                KeyTip = FreePRibbonText.AnimationTriggerCommand.KeyTip,
                Width = profile.AnimationTriggerWidth
            });
            group.ComboBox("freep.anim.duration", FreePRibbonText.AnimationDurationCommand.Label, control => control with
            {
                Items = FreePRibbonDefinitionData.AnimationDurations,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                KeyTip = FreePRibbonText.AnimationDurationCommand.KeyTip,
                Width = 90
            });
            group.ComboBox("freep.anim.delay", FreePRibbonText.AnimationDelayCommand.Label, control => control with
            {
                Items = FreePRibbonDefinitionData.AnimationDelays,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.History),
                KeyTip = FreePRibbonText.AnimationDelayCommand.KeyTip,
                Width = 90
            });
            group.Medium("freep.anim.move-earlier", FreePRibbonText.AnimationMoveEarlierCommand.Label, RibbonCommandIconKind.Previous, FreePRibbonText.AnimationMoveEarlierCommand.KeyTip);
            group.Medium("freep.anim.move-later", FreePRibbonText.AnimationMoveLaterCommand.Label, RibbonCommandIconKind.Next, FreePRibbonText.AnimationMoveLaterCommand.KeyTip);
        });
        tab.Group("animation-pane", FreePRibbonText.AdvancedAnimationGroup.Label, FreePRibbonText.AdvancedAnimationGroup.KeyTip, 80, group =>
        {
            group.MediumToggle("freep.anim.pane", FreePRibbonText.AnimationPaneCommand.Label, RibbonCommandIconKind.List, FreePRibbonText.AnimationPaneCommand.KeyTip);
        });
    }

    private static void AddViewGroups(RibbonTabBuilder tab)
    {
        tab.Group("show", FreePRibbonText.ViewShowGroup.Label, FreePRibbonText.ViewShowGroup.KeyTip, 100, group =>
        {
            group.MediumToggle("freep.view.show.gridlines", FreePRibbonText.ViewGridlinesCommand.Label,
                RibbonCommandIconKind.Grid, FreePRibbonText.ViewGridlinesCommand.KeyTip);
            group.MediumToggle("freep.view.show.guides", FreePRibbonText.ViewGuidesCommand.Label,
                RibbonCommandIconKind.Align, FreePRibbonText.ViewGuidesCommand.KeyTip);
            group.Medium("freep.view.selection-pane", FreePRibbonText.ViewSelectionPaneCommand.Label,
                RibbonCommandIconKind.List, FreePRibbonText.ViewSelectionPaneCommand.KeyTip);
        });
        tab.Group("zoom", FreePRibbonText.ViewZoomGroup.Label, FreePRibbonText.ViewZoomGroup.KeyTip, 90, group =>
        {
            group.Large("freep.view.zoom", FreePRibbonText.ViewZoomCommand.Label, RibbonCommandIconKind.Zoom,
                FreePRibbonText.ViewZoomCommand.KeyTip);
            group.Medium("freep.view.fit-to-window", FreePRibbonText.ViewFitToWindowCommand.Label,
                RibbonCommandIconKind.Scale, FreePRibbonText.ViewFitToWindowCommand.KeyTip);
        });
    }

    private static void AddParagraphControls(RibbonGroupBuilder group)
    {
        group.Dropdown(
            PresentationListGalleryPlanner.BulletsCommandId,
            FreePRibbonText.BulletsCommand.Label,
            BuildListGalleryMenu(PresentationListGalleryPlanner.BuildBulletGalleryPlan()),
            dropdown => dropdown with
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.List),
                KeyTip = FreePRibbonText.BulletsCommand.KeyTip
            });
        group.Dropdown(
            PresentationListGalleryPlanner.NumberingCommandId,
            FreePRibbonText.NumberingCommand.Label,
            BuildListGalleryMenu(PresentationListGalleryPlanner.BuildNumberingGalleryPlan()),
            dropdown => dropdown with
            {
                PreferredLayout = RibbonCommandLayoutKind.Medium,
                Icon = new RibbonCommandIcon(RibbonCommandIconKind.List),
                KeyTip = FreePRibbonText.NumberingCommand.KeyTip
            });
        group.Icon("freep.paragraph.align-left", FreePRibbonText.AlignLeftCommand.Label, RibbonCommandIconKind.AlignLeft, FreePRibbonText.AlignLeftCommand.KeyTip);
        group.Icon("freep.paragraph.align-center", FreePRibbonText.AlignCenterCommand.Label, RibbonCommandIconKind.AlignCenter, FreePRibbonText.AlignCenterCommand.KeyTip);
        group.Icon("freep.paragraph.align-right", FreePRibbonText.AlignRightCommand.Label, RibbonCommandIconKind.AlignRight, FreePRibbonText.AlignRightCommand.KeyTip);
        group.Icon("freep.paragraph.align-justify", FreePRibbonText.AlignJustifyCommand.Label, RibbonCommandIconKind.Align, FreePRibbonText.AlignJustifyCommand.KeyTip);
        group.Icon("freep.indent-decrease", FreePRibbonText.IndentDecreaseCommand.Label, RibbonCommandIconKind.ArrowLeft, FreePRibbonText.IndentDecreaseCommand.KeyTip);
        group.Icon("freep.indent-increase", FreePRibbonText.IndentIncreaseCommand.Label, RibbonCommandIconKind.ArrowRight, FreePRibbonText.IndentIncreaseCommand.KeyTip);
    }

    internal static RibbonMenu BuildListGalleryMenu(PresentationListGalleryPlan plan) =>
        new(plan.Items.Select((item, index) => new RibbonMenuItem(
            item.PreviewText,
            new RibbonCommandId(item.CommandId),
            KeyTip: (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            IsEnabled = item.IsEnabled
        }).ToArray());

    internal static RibbonMenu BuildSmartArtColorGalleryMenu() =>
        new(SmartArtAuthoringPlanner.ColorGallery.Select((entry, index) => new RibbonMenuItem(
            entry.Title,
            new RibbonCommandId(entry.CommandId),
            KeyTip: (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());

    internal static RibbonMenu BuildChartTypeMenu() =>
        new(ChartDataDialogPlanner.ChartTypeOptions.Select((option, index) => new RibbonMenuItem(
            option.Label,
            new RibbonCommandId(ChartDataDialogPlanner.ChangeChartTypeOptionCommandId(option.Value)),
            KeyTip: (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());

    private static void BuildAdvancedEntranceAnimationMenu(RibbonMenuBuilder menu)
    {
        menu.Item("freep.anim.entrance.checkerboard", FreePRibbonText.AnimationEntranceCheckerboardCommand.Label, FreePRibbonText.AnimationEntranceCheckerboardCommand.KeyTip);
        menu.Item("freep.anim.entrance.box", FreePRibbonText.AnimationEntranceBoxCommand.Label, FreePRibbonText.AnimationEntranceBoxCommand.KeyTip);
        menu.Item("freep.anim.entrance.circle", FreePRibbonText.AnimationEntranceCircleCommand.Label, FreePRibbonText.AnimationEntranceCircleCommand.KeyTip);
        menu.Item("freep.anim.entrance.diamond", FreePRibbonText.AnimationEntranceDiamondCommand.Label, FreePRibbonText.AnimationEntranceDiamondCommand.KeyTip);
        menu.Item("freep.anim.entrance.plus", FreePRibbonText.AnimationEntrancePlusCommand.Label, FreePRibbonText.AnimationEntrancePlusCommand.KeyTip);
        menu.Item("freep.anim.entrance.strips", FreePRibbonText.AnimationEntranceStripsCommand.Label, FreePRibbonText.AnimationEntranceStripsCommand.KeyTip);
        menu.Item("freep.anim.entrance.wedge", FreePRibbonText.AnimationEntranceWedgeCommand.Label, FreePRibbonText.AnimationEntranceWedgeCommand.KeyTip);
        menu.Item("freep.anim.entrance.wheel", FreePRibbonText.AnimationEntranceWheelCommand.Label, FreePRibbonText.AnimationEntranceWheelCommand.KeyTip);
        menu.Item("freep.anim.entrance.random-bars", FreePRibbonText.AnimationEntranceRandomBarsCommand.Label, FreePRibbonText.AnimationEntranceRandomBarsCommand.KeyTip);
    }

    private static void BuildAdvancedExitAnimationMenu(RibbonMenuBuilder menu)
    {
        menu.Item("freep.anim.exit.checkerboard", FreePRibbonText.AnimationExitCheckerboardCommand.Label, FreePRibbonText.AnimationExitCheckerboardCommand.KeyTip);
        menu.Item("freep.anim.exit.box", FreePRibbonText.AnimationExitBoxCommand.Label, FreePRibbonText.AnimationExitBoxCommand.KeyTip);
        menu.Item("freep.anim.exit.circle", FreePRibbonText.AnimationExitCircleCommand.Label, FreePRibbonText.AnimationExitCircleCommand.KeyTip);
        menu.Item("freep.anim.exit.diamond", FreePRibbonText.AnimationExitDiamondCommand.Label, FreePRibbonText.AnimationExitDiamondCommand.KeyTip);
        menu.Item("freep.anim.exit.plus", FreePRibbonText.AnimationExitPlusCommand.Label, FreePRibbonText.AnimationExitPlusCommand.KeyTip);
        menu.Item("freep.anim.exit.strips", FreePRibbonText.AnimationExitStripsCommand.Label, FreePRibbonText.AnimationExitStripsCommand.KeyTip);
        menu.Item("freep.anim.exit.wedge", FreePRibbonText.AnimationExitWedgeCommand.Label, FreePRibbonText.AnimationExitWedgeCommand.KeyTip);
        menu.Item("freep.anim.exit.wheel", FreePRibbonText.AnimationExitWheelCommand.Label, FreePRibbonText.AnimationExitWheelCommand.KeyTip);
        menu.Item("freep.anim.exit.random-bars", FreePRibbonText.AnimationExitRandomBarsCommand.Label, FreePRibbonText.AnimationExitRandomBarsCommand.KeyTip);
    }

    private static void BuildExtendedTransitionMenu(RibbonMenuBuilder menu)
    {
        var index = 0;
        void Item(string commandId, string label) =>
            menu.Item(commandId, label, $"T{++index}");

        Item("freep.transition.fly", FreePRibbonText.TransitionFlyCommand.Label);
        Item("freep.transition.random", FreePRibbonText.TransitionRandomCommand.Label);
        Item("freep.transition.cube", FreePRibbonText.TransitionCubeCommand.Label);
        Item("freep.transition.rotate", FreePRibbonText.TransitionRotateCommand.Label);
        Item("freep.transition.flip", FreePRibbonText.TransitionFlipCommand.Label);
        Item("freep.transition.ferris", FreePRibbonText.TransitionFerrisCommand.Label);
        Item("freep.transition.flythrough", FreePRibbonText.TransitionFlythroughCommand.Label);
        Item("freep.transition.switch", FreePRibbonText.TransitionSwitchCommand.Label);
        Item("freep.transition.orbit", FreePRibbonText.TransitionOrbitCommand.Label);
        Item("freep.transition.honeycomb", FreePRibbonText.TransitionHoneycombCommand.Label);
        Item("freep.transition.glitter", FreePRibbonText.TransitionGlitterCommand.Label);
        Item("freep.transition.vortex", FreePRibbonText.TransitionVortexCommand.Label);
        Item("freep.transition.shred", FreePRibbonText.TransitionShredCommand.Label);
        Item("freep.transition.wind", FreePRibbonText.TransitionWindCommand.Label);
        Item("freep.transition.ripple", FreePRibbonText.TransitionRippleCommand.Label);
        Item("freep.transition.warp", FreePRibbonText.TransitionWarpCommand.Label);
        Item("freep.transition.fracture", FreePRibbonText.TransitionFractureCommand.Label);
        Item("freep.transition.crush", FreePRibbonText.TransitionCrushCommand.Label);
        Item("freep.transition.peel-off", FreePRibbonText.TransitionPeelOffCommand.Label);
        Item("freep.transition.page-curl-double", FreePRibbonText.TransitionPageCurlDoubleCommand.Label);
        Item("freep.transition.page-curl-single", FreePRibbonText.TransitionPageCurlSingleCommand.Label);
        Item("freep.transition.airplane", FreePRibbonText.TransitionAirplaneCommand.Label);
        Item("freep.transition.origami", FreePRibbonText.TransitionOrigamiCommand.Label);
        Item("freep.transition.prism", FreePRibbonText.TransitionPrismCommand.Label);
        Item("freep.transition.curtains", FreePRibbonText.TransitionCurtainsCommand.Label);
        Item("freep.transition.drape", FreePRibbonText.TransitionDrapeCommand.Label);
        Item("freep.transition.prestige", FreePRibbonText.TransitionPrestigeCommand.Label);
    }
}
