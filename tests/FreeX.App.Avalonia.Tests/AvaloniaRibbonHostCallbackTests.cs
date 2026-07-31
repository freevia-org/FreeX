using System.Collections.Generic;
using System.Linq;

using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.DrawingUI;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Verifies the Avalonia ribbon registry binds the shell's host callbacks to the right command ids, so the
/// declarative ribbon invokes the same handlers as the native menus (charts/CF/table/quick-analysis/etc.).
/// The ribbon definition is now the single-source shared <see cref="FreeXRibbon"/>; the shell registers
/// handlers under its historical dotted ids, which are re-keyed to the canonical ids the shared definition
/// emits via <see cref="AvaloniaCommandIdAdapter"/>. Tests therefore resolve through the adapter. Pure
/// registry assertions — no running shell or UI thread required.
/// </summary>
public sealed class AvaloniaRibbonHostCallbackTests
{
    private static readonly RibbonCommandContext EmptyContext =
        new(new Dictionary<string, object?>());

    private static RibbonCommandId Canonical(string avaloniaId) =>
        new(AvaloniaCommandIdAdapter.ToCanonical(avaloniaId));

    [Theory]
    [InlineData("data.textToColumns")]
    [InlineData("data.consolidate")]
    [InlineData("insert.table")]
    [InlineData("home.formatAsTable")]
    [InlineData("home.conditional")]
    [InlineData("data.quickAnalysis")]
    [InlineData("data.sortAsc")]
    [InlineData("data.sortDesc")]
    [InlineData("data.filter")]
    [InlineData("data.validation")]
    [InlineData("data.validationDialog")]
    [InlineData("home.cut")]
    [InlineData("home.copy")]
    [InlineData("home.paste")]
    [InlineData("home.alignLeft")]
    [InlineData("home.alignCenter")]
    [InlineData("home.alignRight")]
    [InlineData("home.wrapText")]
    [InlineData("home.merge")]
    [InlineData("home.mergeCenter")]
    [InlineData("home.currency")]
    [InlineData("home.percent")]
    [InlineData("home.comma")]
    public void BuildRegistry_WithCallbacks_BindsRealCommand(string commandId)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, AllWired());

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ActionRibbonCommand>(command);
    }

    [Theory]
    [InlineData("insert.table")]
    [InlineData("home.conditional")]
    [InlineData("data.sortAsc")]
    public void BuildRegistry_WithoutCallbacks_LeavesNoOp(string commandId)
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
    }

    [Fact]
    public void WiredCommands_Execute_InvokeTheHostCallback()
    {
        var fired = new HashSet<string>();
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            OpenTextToColumns = () => fired.Add("textToColumns"),
            OpenConsolidate = () => fired.Add("consolidate"),
            InsertTable = () => fired.Add("table"),
            ConditionalFormatting = () => fired.Add("conditional"),
            QuickAnalysis = () => fired.Add("quickAnalysis"),
            SortAscending = () => fired.Add("sortAsc"),
            SortDescending = () => fired.Add("sortDesc"),
            DataValidation = () => fired.Add("validation"),
            Copy = () => fired.Add("copy"),
            AlignCenter = () => fired.Add("alignCenter"),
            PercentFormat = () => fired.Add("percent"),
        };
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, callbacks);

        Execute(registry, "data.quickAnalysis");
        Execute(registry, "insert.table");
        Execute(registry, "home.conditional");
        Execute(registry, "data.sortDesc");
        Execute(registry, "home.copy");
        Execute(registry, "home.alignCenter");
        Execute(registry, "home.percent");

        Assert.Contains("quickAnalysis", fired);
        Assert.Contains("table", fired);
        Assert.Contains("conditional", fired);
        Assert.Contains("sortDesc", fired);
        Assert.Contains("copy", fired);
        Assert.Contains("alignCenter", fired);
        Assert.Contains("percent", fired);
    }

    [Fact]
    public void ExtraCommands_BindParameterizedMenuItems_AndExecute()
    {
        // ExtraCommands keys that have a canonical mapping route to the shared definition's ids; the menu/swatch
        // items without a canonical equivalent pass through unchanged (and are registered under that raw id).
        var fired = new List<string>();
        var callbacks = new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["home.fmtGeneral"] = () => fired.Add("general"),
                ["home.fmtDate"] = () => fired.Add("date"),
                ["home.fillYellow"] = () => fired.Add("yellow"),
                ["home.bordersAll"] = () => fired.Add("bordersAll"),
                ["home.pasteValues"] = () => fired.Add("pasteValues"),
            },
        };
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, callbacks);

        Assert.True(registry.TryGet(Canonical("home.bordersAll"), out var c));
        Assert.IsType<ActionRibbonCommand>(c);

        Execute(registry, "home.fmtGeneral");
        Execute(registry, "home.fmtDate");
        Execute(registry, "home.fillYellow");
        Execute(registry, "home.bordersAll");
        Execute(registry, "home.pasteValues");

        Assert.Equal(new[] { "general", "date", "yellow", "bordersAll", "pasteValues" }, fired);
    }

    [Fact]
    public void BuildDefinition_IsTheSharedSingleSourceDefinition()
    {
        // The Avalonia ribbon is now built from the exact same definition the WPF app consumes.
        var headers = AvaloniaRibbonComposition.BuildDefinition().Tabs.Select(t => t.Header).ToList();
        var shared = FreeXRibbon.Build().Tabs.Select(t => t.Header).ToList();

        Assert.Equal(shared, headers);
    }

    [Fact]
    public void BuildDefinition_HasWindowsTabStructure()
    {
        var headers = AvaloniaRibbonComposition.BuildDefinition().Tabs.Select(t => t.Header).ToList();

        foreach (var expected in new[] { "Home", "Insert", "Data", "Page Layout", "Formulas", "Review", "View" })
            Assert.Contains(expected, headers);
    }

    [Fact]
    public void BuildDefinition_OutlineGroupAndUngroupAreTrueSplitButtons()
    {
        var outline = AvaloniaRibbonComposition.BuildDefinition()
            .FindTab("DataTab")!
            .FindGroup("DataOutlineGroup")!;

        Assert.IsType<RibbonSplitButton>(outline.Controls[0]);
        Assert.IsType<RibbonSplitButton>(outline.Controls[1]);
        Assert.IsType<RibbonButton>(outline.Controls[2]);
    }

    [Fact]
    public void BuildDefinition_ShapesUsesTheCompleteSharedGallery()
    {
        var draw = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(tab => tab.Id == "DrawTab");
        var shapes = Assert.IsType<RibbonSplitButton>(draw.Groups
            .SelectMany(group => group.Controls)
            .Single(control => control.CommandId.Value == "Shapes"));

        Assert.Equal(
            DrawingInsertionPlanner.ShapeGroups.Select(group => group.Label),
            shapes.Menu.Items.Select(group => group.Header));
        Assert.Equal(
            DrawingInsertionPlanner.ShapeItems.Select(item => item.Label),
            shapes.Menu.Items.SelectMany(group => group.Children).Select(item => item.Header));
    }

    [Fact]
    public void ShapeGalleryCommand_InvokesTheSelectedShapeKind()
    {
        DrawingShapeKind? inserted = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null,
            _ => { },
            new AvaloniaRibbonHostCallbacks { InsertShape = kind => inserted = kind });
        var commandId = new RibbonCommandId(
            AvaloniaRibbonComposition.GetShapeCommandId(DrawingShapeKind.Diamond));

        Assert.True(registry.TryGet(commandId, out var command));
        command!.Execute(EmptyContext);

        Assert.Equal(DrawingShapeKind.Diamond, inserted);
    }

    [Fact]
    public void BuildDefinition_FormulasTab_HasExpectedGroups()
    {
        var formulas = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Formulas");
        var groups = formulas.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Function Library", groups);
        Assert.Contains("Defined Names", groups);
        Assert.Contains("Formula Auditing", groups);
        Assert.Contains("Calculation", groups);
    }

    [Fact]
    public void BuildDefinition_ReviewTab_HasNotesAndProtectGroups()
    {
        var review = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Review");
        var groups = review.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Notes", groups);
        Assert.Contains("Protect", groups);
    }

    [Theory]
    [InlineData("formulas.insertFunction")]
    [InlineData("formulas.nameManager")]
    [InlineData("formulas.autoSum")]
    [InlineData("review.protectSheet")]
    [InlineData("review.checkAccessibility")]
    [InlineData("review.convertNotesToComments")]
    [InlineData("help.copyDiagnostics")]
    [InlineData("help.legalNotices")]
    [InlineData("view.gridlines")]
    [InlineData("view.freezePanes")]
    [InlineData("view.zoom100")]
    [InlineData("pageLayout.margins")]
    [InlineData("home.strikethrough")]
    [InlineData("home.increaseFont")]
    [InlineData("home.alignTop")]
    [InlineData("home.increaseIndent")]
    [InlineData("home.increaseDecimal")]
    [InlineData("data.flashFill")]
    [InlineData("data.removeDuplicates")]
    [InlineData("data.advancedFilter")]
    [InlineData("data.whatIf")]
    [InlineData("view.unhide")]
    [InlineData("view.split")]
    [InlineData("pageLayout.printTitles")]
    [InlineData("formulas.mathTrig")]
    [InlineData("formulas.lookupReference")]
    [InlineData("home.formatCells")]
    // Home ▸ Editing ▸ Fill / Clear dropdown items are wired under their raw canonical menu ids.
    [InlineData("Down")]
    [InlineData("Right")]
    [InlineData("Up")]
    [InlineData("Left")]
    [InlineData("Series")]
    [InlineData("Clear All")]
    [InlineData("Clear Formats")]
    [InlineData("Clear Contents")]
    [InlineData("Clear Comments and Notes")]
    [InlineData("Clear Hyperlinks")]
    // Home ▸ Editing ▸ AutoSum / Find & Select dropdown items (raw canonical menu ids).
    [InlineData("Sum")]
    [InlineData("Average")]
    [InlineData("Count Numbers")]
    [InlineData("Count All")]
    [InlineData("Max")]
    [InlineData("Min")]
    [InlineData("More Functions")]
    [InlineData("Find")]
    [InlineData("Replace")]
    [InlineData("Go To")]
    [InlineData("Go To Special")]
    [InlineData("Formulas")]
    [InlineData("Notes")]
    [InlineData("Constants")]
    [InlineData("Data Validation")]
    [InlineData("Select Objects")]
    [InlineData("Selection Pane")]
    // Home ▸ Font ▸ Borders / Underline / Orientation dropdown items (raw canonical menu ids).
    [InlineData("Inside Borders")]
    [InlineData("Top Border")]
    [InlineData("Bottom Border")]
    [InlineData("Left Border")]
    [InlineData("Right Border")]
    [InlineData("More Borders")]
    [InlineData("Horizontal")]
    [InlineData("Angle Counterclockwise")]
    [InlineData("Angle Clockwise")]
    [InlineData("Vertical Text")]
    [InlineData("Rotate Text Up")]
    [InlineData("Rotate Text Down")]
    // Home ▸ Cells (Insert/Delete/Format) + Conditional Formatting preset dropdown items.
    [InlineData("Insert Cells")]
    [InlineData("Insert Sheet")]
    [InlineData("Delete Cells")]
    [InlineData("Format Cells")]
    [InlineData("Protect Sheet")]
    [InlineData("Unhide Sheet")]
    [InlineData("New Rule")]
    [InlineData("Clear Rules")]
    [InlineData("Data Bars")]
    [InlineData("Color Scales")]
    [InlineData("Greater Than")]
    [InlineData("Top 10 Items")]
    public void NewTabCommands_AreRealCommandIds_AndBindViaExtraCommands(string commandId)
    {
        // The canonical id exists in the shared definition (so it seeds a NoOp default to begin with) ...
        var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
        Assert.True(defaults.TryGet(Canonical(commandId), out var noOp));
        Assert.IsType<EmptyRibbonCommand>(noOp);

        // ... and ExtraCommands (how MainWindow wires the new tabs) overrides it with a real command.
        var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action> { [commandId] = () => { } },
        });
        Assert.True(wired.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ActionRibbonCommand>(command);
    }

    [Fact]
    public void ConditionalFormatPopupCatalogRows_AreRealRawCommandIds_AndBindViaExtraCommands()
    {
        foreach (var item in ConditionalFormatPresetGalleryPlanner.PopupItems)
        {
            Assert.Contains(item.CommandId, AvaloniaExtraCommandIds.RawCanonical);

            var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
            Assert.True(defaults.TryGet(Canonical(item.CommandId), out var noOp), $"Conditional-format popup id '{item.CommandId}' is not in the shared definition.");
            Assert.IsType<EmptyRibbonCommand>(noOp);

            var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action> { [item.CommandId] = () => { } },
            });
            Assert.True(wired.TryGet(Canonical(item.CommandId), out var command));
            Assert.IsType<ActionRibbonCommand>(command);
        }
    }

    [Fact]
    public void EveryCellStylePreset_DisplayNameIsARealMenuId_AndBindsViaExtraCommands()
    {
        // MainWindow wires the Home ▸ Styles ▸ Cell Styles gallery items by looping CellStylePreset and using
        // each preset's display name as the canonical ribbon menu id. Verify every display name is a real id
        // the shared definition emits (seeded NoOp) and that wiring it via ExtraCommands overrides the NoOp.
        foreach (var preset in System.Enum.GetValues<FreeX.App.Services.CellStylePreset>())
        {
            var id = FreeX.App.Services.CellStyleDiffPlanner.GetCellStylePresetDisplayName(preset);

            var defaults = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });
            Assert.True(defaults.TryGet(Canonical(id), out var noOp), $"Cell-style id '{id}' is not in the shared definition.");
            Assert.IsType<EmptyRibbonCommand>(noOp);

            var wired = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action> { [id] = () => { } },
            });
            Assert.True(wired.TryGet(Canonical(id), out var command));
            Assert.IsType<ActionRibbonCommand>(command);
        }
    }

    [Fact]
    public void BuildDefinition_HomeTab_MatchesWindowsGroups()
    {
        var home = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Home");
        var groups = home.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Clipboard", "Font", "Alignment", "Number", "Styles", "Cells", "Editing" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_DataTab_HasForecastAndOutlineGroups()
    {
        var data = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Data");
        var groups = data.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Forecast", groups);
        Assert.Contains("Outline", groups);
    }

    [Fact]
    public void BuildDefinition_InsertTab_MatchesWindowsGroups()
    {
        var insert = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "Insert");
        var groups = insert.Groups.Select(g => g.Header).ToList();

        foreach (var expected in new[] { "Tables", "Charts", "Sparklines", "Filters", "Links", "Comments", "Text", "Symbols" })
            Assert.Contains(expected, groups);
    }

    [Fact]
    public void BuildDefinition_ViewTab_HasShowZoomWindowGroups()
    {
        var view = AvaloniaRibbonComposition.BuildDefinition().Tabs.Single(t => t.Header == "View");
        var groups = view.Groups.Select(g => g.Header).ToList();

        Assert.Contains("Show", groups);
        Assert.Contains("Zoom", groups);
        Assert.Contains("Window", groups);
    }

    [Fact]
    public void SetFontSize_BindsFontSizeCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontSize = v => applied = v });

        Assert.True(registry.TryGet(Canonical("home.fontSize"), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("14"));
        Assert.Equal("14", applied);
    }

    [Fact]
    public void WithoutSetFontSize_FontSizeComboStaysNoOp()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical("home.fontSize"), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
    }

    [Fact]
    public void SetFontName_BindsFontNameCombo_AndPassesSelectedValue()
    {
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { SetFontName = v => applied = v });

        Assert.True(registry.TryGet(Canonical("home.fontName"), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue("Arial"));
        Assert.Equal("Arial", applied);
    }

    [Fact]
    public void WithoutSetFontName_FontNameComboStaysNoOp()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(Canonical("home.fontName"), out var command));
        Assert.IsType<EmptyRibbonCommand>(command);
    }

    [Theory]
    [InlineData("pageLayout.width", "2 pages")]
    [InlineData("pageLayout.height", "3 pages")]
    [InlineData("pageLayout.scale", "85%")]
    public void PageLayoutScaleCombos_PassSelectedValueToValueAwareCallbacks(
        string commandId,
        string selectedValue)
    {
        var openedPageSetup = false;
        string? applied = null;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null,
            _ => { },
            new AvaloniaRibbonHostCallbacks
            {
                ExtraCommands = new Dictionary<string, Action>
                {
                    [commandId] = () => openedPageSetup = true,
                },
                SetPageLayoutScaleWidth = commandId == "pageLayout.width" ? value => applied = value : null,
                SetPageLayoutScaleHeight = commandId == "pageLayout.height" ? value => applied = value : null,
                SetPageLayoutScalePercent = commandId == "pageLayout.scale" ? value => applied = value : null,
            });

        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        Assert.IsType<ValueRibbonCommand>(command);

        command!.Execute(RibbonCommandContext.ForSelectedValue(selectedValue));

        Assert.Equal(selectedValue, applied);
        Assert.False(openedPageSetup);
    }

    [Fact]
    public void DrawCommands_DefaultToWindowsStaticDrawEnablement()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { });

        Assert.True(registry.TryGet(new RibbonCommandId("Bring Forward"), out var bringForward));
        Assert.IsType<EmptyRibbonCommand>(bringForward);
        Assert.True(registry.TryGet(new RibbonCommandId("Shape Fill"), out var shapeFill));
        Assert.IsType<EmptyRibbonCommand>(shapeFill);

        Assert.True(registry.TryGet(new RibbonCommandId("Crop Picture"), out var crop));
        var cropState = Assert.IsAssignableFrom<IRibbonStatefulCommand>(crop);
        Assert.False(cropState.GetState().IsEnabled);
        Assert.True(registry.TryGet(new RibbonCommandId("Shape Gradient"), out var gradient));
        var gradientState = Assert.IsAssignableFrom<IRibbonStatefulCommand>(gradient);
        Assert.False(gradientState.GetState().IsEnabled);
        Assert.True(registry.TryGet(new RibbonCommandId("Shape Effects"), out var effects));
        var effectsState = Assert.IsAssignableFrom<IRibbonStatefulCommand>(effects);
        Assert.False(effectsState.GetState().IsEnabled);
    }

    [Fact]
    public void ExtraCommandStates_RegisterStatefulRelayCommand()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["view.gridlines"] = () => { },
            },
            ExtraCommandStates = new Dictionary<string, Func<RibbonCommandState>>
            {
                ["view.gridlines"] = () => new RibbonCommandState(IsChecked: true),
            },
        });

        Assert.True(registry.TryGet(Canonical("view.gridlines"), out var command));
        var stateful = Assert.IsAssignableFrom<IRibbonStatefulCommand>(command);
        Assert.True(stateful.GetState().IsChecked);
    }

    [Fact]
    public void DrawPicturesAndShapes_BindToInsertCallbacks_WhenProvided()
    {
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            InsertPicture = () => { },
            InsertShape = _ => { },
        });

        Assert.True(registry.TryGet(Canonical("insert.picture"), out var picture));
        Assert.True(registry.TryGet(Canonical("insert.shapes"), out var shapes));
        Assert.IsType<ActionRibbonCommand>(picture);
        Assert.IsType<ActionRibbonCommand>(shapes);
    }

    [Fact]
    public void DrawingObjectRotateAndSize_ExecuteThroughSharedCanonicalCommands()
    {
        var fired = new List<string>();
        var registry = AvaloniaRibbonComposition.BuildRegistry(() => null, _ => { }, new AvaloniaRibbonHostCallbacks
        {
            ExtraCommands = new Dictionary<string, Action>
            {
                ["shapeFormat.rotate"] = () => fired.Add("rotate"),
                ["shapeFormat.size"] = () => fired.Add("size"),
            },
        });

        Execute(registry, "shapeFormat.rotate");
        Execute(registry, "shapeFormat.size");

        Assert.Equal(new[] { "rotate", "size" }, fired);

        var definition = AvaloniaRibbonComposition.BuildDefinition();
        foreach (var tabId in new[] { "DrawTab", "ShapeFormatTab" })
        {
            var ids = definition.FindTab(tabId)!.Groups
                .SelectMany(group => group.Controls)
                .Select(control => control.CommandId.Value)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Rotate Object", ids);
            Assert.Contains("Object Size", ids);
        }
    }

    [Fact]
    public void InsertTable_BindsBothRibbonAndHomeButtons_ToTheSameAction()
    {
        var count = 0;
        var registry = AvaloniaRibbonComposition.BuildRegistry(
            () => null, _ => { }, new AvaloniaRibbonHostCallbacks { InsertTable = () => count++ });

        Execute(registry, "insert.table");
        Execute(registry, "home.formatAsTable");

        Assert.Equal(2, count);
    }

    /// <summary>
    /// The keystone single-source guarantee: every Avalonia handler id the adapter knows maps to a canonical
    /// id that the shared <see cref="FreeXRibbon.Build"/> definition actually emits, so each handler binds to a
    /// real control/menu item the renderer queries.
    /// </summary>
    [Fact]
    public void EveryAvaloniaHandlerId_MapsToACanonicalIdPresentInTheSharedDefinition()
    {
        var canonicalIds = AvaloniaRibbonComposition
            .EnumerateCommandIds(FreeXRibbon.Build())
            .Select(id => id.Value)
            .ToHashSet(StringComparer.Ordinal);

        var unmapped = AvaloniaCommandIdAdapter.AvaloniaIds
            .Where(avaloniaId => !canonicalIds.Contains(AvaloniaCommandIdAdapter.ToCanonical(avaloniaId)))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unmapped.Length == 0,
            "Avalonia handler ids whose canonical mapping is absent from FreeXRibbon.Build(): "
                + string.Join(", ", unmapped));
    }

    /// <summary>
    /// The documented orphans (features with no canonical control in the shared definition) pass through
    /// <see cref="AvaloniaCommandIdAdapter.ToCanonical"/> unchanged and are intentionally NOT present in the
    /// shared definition — so their handler registration is harmless dead weight, never a hijack of an
    /// unrelated canonical control.
    /// </summary>
    [Fact]
    public void OrphanAvaloniaIds_AreAbsentFromTheSharedDefinition_AndPassThroughUnchanged()
    {
        var canonicalIds = AvaloniaRibbonComposition
            .EnumerateCommandIds(FreeXRibbon.Build())
            .Select(id => id.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var orphan in AvaloniaCommandIdAdapter.OrphanAvaloniaIds)
        {
            Assert.Equal(orphan, AvaloniaCommandIdAdapter.ToCanonical(orphan));
            Assert.DoesNotContain(orphan, canonicalIds);
            Assert.False(AvaloniaCommandIdAdapter.IsKnownAvaloniaId(orphan),
                $"Orphan '{orphan}' must not also have a canonical mapping.");
        }
    }

    [Fact]
    public void ToCanonical_And_ToAvalonia_RoundTripForPrimaryIds()
    {
        // ToAvalonia maps a canonical id back to its primary Avalonia id; for ids that are not aliased, the
        // round-trip is stable.
        Assert.Equal("Bold", AvaloniaCommandIdAdapter.ToCanonical("home.bold"));
        Assert.Equal("home.bold", AvaloniaCommandIdAdapter.ToAvalonia("Bold"));
        Assert.Equal("Change Chart Type#ChangeChartTypeBtn_Click", AvaloniaCommandIdAdapter.ToCanonical("chartDesign.changeType"));

        // Unknown ids pass through unchanged.
        Assert.Equal("not.a.real.id", AvaloniaCommandIdAdapter.ToCanonical("not.a.real.id"));
        Assert.Equal("Not A Real Canonical", AvaloniaCommandIdAdapter.ToAvalonia("Not A Real Canonical"));
    }

    private static AvaloniaRibbonHostCallbacks AllWired() => new()
    {
        OpenTextToColumns = () => { },
        OpenConsolidate = () => { },
        InsertTable = () => { },
        ConditionalFormatting = () => { },
        QuickAnalysis = () => { },
        SortAscending = () => { },
        SortDescending = () => { },
        ToggleFilter = () => { },
        DataValidation = () => { },
        Cut = () => { },
        Copy = () => { },
        Paste = () => { },
        AlignLeft = () => { },
        AlignCenter = () => { },
        AlignRight = () => { },
        WrapText = () => { },
        MergeAndCenter = () => { },
        CurrencyFormat = () => { },
        PercentFormat = () => { },
        CommaStyle = () => { },
    };

    private static void Execute(IRibbonCommandRegistry registry, string commandId)
    {
        Assert.True(registry.TryGet(Canonical(commandId), out var command));
        command!.Execute(EmptyContext);
    }
}
