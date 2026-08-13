using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Wpf;

using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;

namespace FreeX.App.Host;

public partial class MainWindow
{
    /// <summary>
    /// Installs the declarative <see cref="FreeXRibbonDefinition"/> through the shared WPF renderer.
    /// Tab shells, commands, keytips, and adaptive policy all come from the shared definition before
    /// workbook and option state is applied.
    /// </summary>
    private void TryApplyDeclarativeRibbon()
    {
        if (RibbonTabs is null)
            return;

        try
        {
            var definition = FreeXRibbon.Build();
            BuildRibbonTabShells(definition);

            // Hidden backplane controls (MainWindow.RibbonBackplane.g.cs) hold state and serve as the
            // fallback sender for handlers. Definition commands bind directly to typed delegates; the
            // control bridge remains only for non-definition backplane commands.
            InitializeRibbonControlBackplane();
            var originals = RibbonBackplaneControls;
            var registry = BuildNativeRibbonRegistry();
            foreach (var (name, control) in originals)
            {
                if (!registry.TryGet(name, out _))
                    registry.Register(name, new WpfControlRibbonCommand(control));
            }

            foreach (var item in RibbonTabs.Items)
            {
                if (item is not TabItem tabItem)
                    continue;
                if (!RibbonMetadata.TryGetCatalogId(tabItem, out var catalogId))
                    continue;
                if (definition.FindTab(catalogId) is not { } definitionTab)
                    continue;

                if (definitionTab.Groups.Count == 0)
                    continue;

                var content = RibbonWpfRenderer.BuildTabContent(definitionTab, this, registry, _ribbonState);

                // The Home tab keeps the HomeRibbonPanel backplane in its rendered subtree so commands
                // injected into it at runtime (Excel add-ins / tests) still surface as keytip candidates.
                // It carries no laid-out children normally, so it does not affect the visual ribbon.
                if (string.Equals(catalogId, "HomeTab", StringComparison.Ordinal) &&
                    HomeRibbonPanel.Parent is null && content is Border { Child: Panel rootPanel })
                {
                    rootPanel.Children.Add(HomeRibbonPanel);
                }

                tabItem.Content = content;
                WireDeclarativeDropdownZones(content);
            }

            // Toggle/combo/enablement state now flows from the neutral RibbonStateStore to the rendered
            // controls (bound in RibbonWpfRenderer), so there is no hidden control to mirror. We still
            // capture the rendered controls by command name so the host can (a) share imperatively-built
            // context menus (e.g. the Shapes gallery) and (b) update per-control help text/labels that
            // are not part of RibbonCommandState.
            var renderedByName = CollectControlsByName();
            _renderedRibbonControls = renderedByName;
            WireDeclarativeStateSync(originals, renderedByName);
            // The Insert Shapes gallery menu is built imperatively (InitializeInsertShapeGalleryContextMenu)
            // before the ribbon exists, so attach it to the now-rendered "Shapes" button here.
            AttachInsertShapeGalleryContextMenu();
            // The Format as Table / Table Styles galleries are likewise populated imperatively
            // (PopulateFormatTableGalleryMenu / PopulateTableDesignStyleGalleryMenu, which may run before
            // the ribbon exists), so attach them to the now-rendered gallery buttons; their click handlers
            // (FormatTableBtn_Click / TableDesignStylesBtn_Click) open the attached menu.
            AttachFormatTableGalleryContextMenu();
            AttachTableDesignStyleGalleryContextMenu();
            // The gallery buttons (Shapes / Format as Table / Table Styles) only get their ContextMenu in
            // the Attach* calls above, after the per-tab pass ran — wire the dropdown split zone for them now.
            foreach (var item in RibbonTabs.Items)
            {
                if (item is TabItem { Content: DependencyObject tabContent })
                    WireDeclarativeDropdownZones(tabContent);
            }
            RepointBackplaneNamesToRenderedControls(renderedByName);
            WireRenderedMenuOpenedHandlers(renderedByName);
            WireRenderedFormatPainterDoubleClick(renderedByName);
            PopulateAndWireRenderedHomeCombos(renderedByName);
            PopulateAndWireRenderedPageLayoutCombos(renderedByName);
        }
        catch (Exception ex)
        {
            // Ribbon materialization must not take down the rest of the workbook shell.
            System.Diagnostics.Debug.WriteLine($"Declarative ribbon swap failed: {ex}");
        }
    }

    private readonly Dictionary<string, TabItem> _ribbonTabsByCatalogId =
        new(StringComparer.Ordinal);

    private void BuildRibbonTabShells(RibbonDefinition definition)
    {
        var selectedCatalogId = RibbonTabs.SelectedItem is DependencyObject selected &&
                                RibbonMetadata.TryGetCatalogId(selected, out var selectedId)
            ? selectedId
            : FreeXRibbonTabIds.Home;

        _suppressRibbonSelectionChangedNormalization = true;
        try
        {
            RibbonTabs.Items.Clear();
            _ribbonTabsByCatalogId.Clear();

            foreach (var tab in definition.Tabs)
            {
                var item = new TabItem
                {
                    Header = tab.Header,
                    Visibility = tab.IsContextual ? Visibility.Collapsed : Visibility.Visible,
                };
                RibbonMetadata.SetCatalogId(item, tab.Id);
                RibbonTooltip.SetKeyTip(item, tab.KeyTip ?? string.Empty);
                RibbonTabs.Items.Add(item);
                _ribbonTabsByCatalogId.Add(tab.Id, item);
            }

            RibbonTabs.SelectedItem = FindRibbonTabByCatalogId(selectedCatalogId) ??
                                      FindRibbonTabByCatalogId(FreeXRibbonTabIds.Home);
        }
        finally
        {
            _suppressRibbonSelectionChangedNormalization = false;
        }
    }

    private TabItem? FindRibbonTabByCatalogId(string catalogId) =>
        _ribbonTabsByCatalogId.GetValueOrDefault(catalogId);

    private TabItem? FileTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.File);
    private TabItem? ShapeFormatTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.ShapeFormat);
    private TabItem? PictureFormatTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.PictureFormat);
    private TabItem? ChartDesignTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.ChartDesign);
    private TabItem? ChartFormatTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.ChartFormat);
    private TabItem? TableDesignTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.TableDesign);
    private TabItem? PivotTableAnalyzeTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.PivotTableAnalyze);
    private TabItem? PivotTableDesignTab => FindRibbonTabByCatalogId(FreeXRibbonTabIds.PivotTableDesign);

    /// <summary>
    /// Attaches the Excel-style split-button dropdown zone (hover highlight + click-zone handler) to every
    /// rendered menu button in a tab. The renderer already gives menu buttons a ContextMenu and a tagged
    /// dropdown chevron; this wires the same runtime zone treatment the XAML ribbon used, directly on the
    /// rendered controls so it does not depend on the static-surface normalization pass. The Ensure* calls
    /// are idempotent (guarded by attached flags) and the highlight recomputes lazily on hover/resize.
    /// </summary>
    private void WireDeclarativeDropdownZones(DependencyObject content)
    {
        foreach (var button in EnumerateLogicalDescendants(content)
                     .Concat(EnumerateVisualDescendants(content))
                     .OfType<ButtonBase>()
                     .Distinct())
        {
            if (RibbonMetadata.IsCollapsedGroupButton(button) ||
                (button.ContextMenu is null && !RibbonMetadata.IsDropdownMenuButton(button)))
                continue;

            EnsureRibbonDropdownChevron(button);
            EnsureRibbonDropdownZoneHandler(button);
            EnsureRibbonDropdownZoneHighlight(button);
        }
    }

    /// <summary>
    /// Builds the native command registry from the generated typed delegate catalog, so command
    /// execution does not depend on reflection or the XAML control tree.
    /// </summary>
    private RibbonCommandRegistry BuildNativeRibbonRegistry()
    {
        var registry = new RibbonCommandRegistry();
        foreach (var (commandId, binding) in FreeXRibbonHandlers)
            registry.Register(commandId, new WpfDelegateRibbonCommand(
                this,
                binding.Handler,
                RibbonBackplaneControls.GetValueOrDefault(commandId)));

        return registry;
    }

    private sealed class WpfDelegateRibbonCommand(
        MainWindow owner,
        Action<MainWindow, object, RoutedEventArgs> handler,
        object? fallbackSender) : IRibbonCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            var sender = (context.Parameters.TryGetValue(RibbonWpfRenderer.SenderKey, out var value)
                    ? value
                    : null)
                ?? fallbackSender
                ?? owner;

            try
            {
                handler(owner, sender, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ribbon command delegate threw: {ex}");
            }
        }
    }

    /// <summary>Maps each ribbon control (and menu item) to the first instance found, keyed by CommandName.</summary>
    private Dictionary<string, Control> CollectControlsByName()
    {
        var map = new Dictionary<string, Control>(StringComparer.Ordinal);
        foreach (var item in RibbonTabs!.Items)
        {
            if (item is not TabItem { Content: DependencyObject content })
                continue;

            foreach (var element in EnumerateLogicalTree(content))
            {
                if (element is Control control &&
                    RibbonMetadata.TryGetCommandName(control, out var name) &&
                    !map.ContainsKey(name))
                {
                    map[name] = control;
                }
            }
        }

        return map;
    }

    /// <summary>Rendered ribbon controls keyed by command name, captured at swap time. Used to share
    /// imperatively-built context menus and to update per-control help text/labels that are not part
    /// of the neutral <see cref="RibbonCommandState"/>.</summary>
    private IReadOnlyDictionary<string, Control> _renderedRibbonControls =
        new Dictionary<string, Control>(StringComparer.Ordinal);

    /// <summary>Returns the visible rendered ribbon control for a command name, if the declarative
    /// ribbon has been built.</summary>
    private Control? FindRenderedRibbonControl(string commandName) =>
        _renderedRibbonControls.TryGetValue(commandName, out var control) ? control : null;

    /// <summary>
    /// Shares imperatively-built context menus from the legacy backplane buttons onto the rendered
    /// buttons. Toggle/combo/enablement state is no longer mirrored here — it flows from the
    /// <see cref="RibbonStateStore"/> to the rendered controls via the renderer's store binding.
    /// </summary>
    private static void WireDeclarativeStateSync(
        IReadOnlyDictionary<string, Control> originals,
        IReadOnlyDictionary<string, Control> rendered)
    {
        foreach (var (name, original) in originals)
        {
            if (!rendered.TryGetValue(name, out var target))
                continue;

            // Some ribbon buttons own a context menu that is built imperatively in code (e.g. the Draw
            // "Shapes" gallery in InitializeInsertShapeGalleryContextMenu), not from the declarative
            // menu model. Share that live menu onto the rendered button so a keytip opens the same
            // gallery (the keytip path resets PlacementTarget when it opens the menu).
            if (original is ButtonBase { ContextMenu: { } sourceMenu } &&
                target is ButtonBase targetButton && targetButton.ContextMenu is null)
            {
                targetButton.ContextMenu = sourceMenu;
            }
        }
    }

    /// <summary>
    /// Wires the on-open refresh for rendered dropdowns whose menu reflects live state (e.g. Arrange
    /// All check-marks the current window arrangement). The declarative menu model is static, so the
    /// host attaches the same Opened handler the original XAML used.
    /// </summary>
    private void WireRenderedMenuOpenedHandlers(IReadOnlyDictionary<string, Control> rendered)
    {
        if (rendered.TryGetValue("Arrange All", out var arrangeAll) &&
            arrangeAll is ButtonBase { ContextMenu: { } arrangeMenu })
        {
            arrangeMenu.Opened += ArrangeAllContextMenu_Opened;
        }
    }

    /// <summary>Guards against re-wiring the rendered Format Painter double-click handler.</summary>
    private bool _renderedFormatPainterDoubleClickWired;

    /// <summary>
    /// Attaches the persistent (double-click) capture handler to the *rendered* Format Painter button.
    /// The single-click path routes through the command binding (<see cref="FormatPainterBtn_Click"/> via
    /// the registry), but the double-click <see cref="FormatPainterBtn_PreviewMouseLeftButtonDown"/> handler
    /// — which arms persistent painter mode — is not part of the command model, so the XAML→declarative
    /// cutover dropped it. Re-attach it here by command name, mirroring how the original XAML button wired
    /// <c>PreviewMouseLeftButtonDown="FormatPainterBtn_PreviewMouseLeftButtonDown"</c>.
    /// </summary>
    private void WireRenderedFormatPainterDoubleClick(IReadOnlyDictionary<string, Control> rendered)
    {
        if (_renderedFormatPainterDoubleClickWired)
            return;

        if (rendered.TryGetValue("Format Painter", out var control) && control is ButtonBase formatPainterButton)
        {
            formatPainterButton.PreviewMouseLeftButtonDown += FormatPainterBtn_PreviewMouseLeftButtonDown;
            _renderedFormatPainterDoubleClickWired = true;
        }
    }

    /// <summary>Guards against re-wiring the rendered combos if the ribbon is rebuilt.</summary>
    private bool _renderedHomeCombosWired;

    /// <summary>
    /// Populates the three editable Home combos (Font, Font Size, Number Format) on the *rendered*
    /// declarative ribbon with their full item sources and wires their commit events to the existing
    /// host handlers. Selecting/typing now drives <see cref="ApplyStyleDiff"/> through the rendered
    /// control (its <c>sender</c>), so the combos are functional without any hidden backplane stub.
    /// </summary>
    private void PopulateAndWireRenderedHomeCombos(IReadOnlyDictionary<string, Control> rendered)
    {
        if (rendered.TryGetValue("Font", out var fontControl) && fontControl is ComboBox fontBox)
        {
            PopulateRenderedComboItems(fontBox, HomeFontFamilyNames);
            SetRenderedComboInitialSelection(fontBox,
                HomeFontFamilyNames.Contains("Calibri") ? "Calibri" : HomeFontFamilyNames.FirstOrDefault());
            if (!_renderedHomeCombosWired)
            {
                fontBox.SelectionChanged += FontNameBox_SelectionChanged;
                fontBox.KeyDown += FontNameBox_KeyDown;
                fontBox.LostKeyboardFocus += FontNameBox_LostKeyboardFocus;
            }
        }

        if (rendered.TryGetValue("Font Size", out var sizeControl) && sizeControl is ComboBox sizeBox)
        {
            PopulateRenderedComboItems(sizeBox, HomeFontSizeOptions);
            SetRenderedComboInitialSelection(sizeBox, "11");
            if (!_renderedHomeCombosWired)
            {
                sizeBox.SelectionChanged += FontSizeBox_SelectionChanged;
                sizeBox.KeyDown += FontSizeBox_KeyDown;
                sizeBox.LostKeyboardFocus += FontSizeBox_LostKeyboardFocus;
            }
        }

        if (rendered.TryGetValue("Number Format", out var numberControl) && numberControl is ComboBox numberBox)
        {
            PopulateRenderedComboItems(numberBox, HomeNumberFormatLabels);
            _suppressToolbarSync = true;
            try
            {
                numberBox.SelectedIndex = HomeNumberFormatDropdownPlanner.DefaultSelectionIndex;
            }
            finally
            {
                _suppressToolbarSync = false;
            }
            if (!_renderedHomeCombosWired)
                numberBox.SelectionChanged += NumberFormatBox_SelectionChanged;
        }

        _renderedHomeCombosWired = true;
    }

    /// <summary>Guards against re-wiring the rendered Page Layout combos if the ribbon is rebuilt.</summary>
    private bool _renderedPageLayoutCombosWired;

    /// <summary>
    /// Populates the three editable Page Layout Scale-to-Fit combos (Scale Width, Scale Height,
    /// Scale Percent) on the *rendered* declarative ribbon with their item sources and wires their
    /// commit events to the existing host handlers. Typing/selecting drives
    /// <see cref="ApplyPageLayoutScaleToFit"/> through the rendered control (its <c>sender</c>), so the
    /// combos are functional without any hidden backplane stub. After population the current sheet's
    /// scale values are synced into the rendered combos for the initial display.
    /// </summary>
    private void PopulateAndWireRenderedPageLayoutCombos(IReadOnlyDictionary<string, Control> rendered)
    {
        if (rendered.TryGetValue("Scale Width", out var widthControl) && widthControl is ComboBox widthBox)
        {
            PopulateRenderedComboItems(widthBox, PageLayoutInputParser.ScalePageCountOptions);
            if (!_renderedPageLayoutCombosWired)
            {
                widthBox.SelectionChanged += PageLayoutScaleWidthBox_SelectionChanged;
                widthBox.KeyDown += PageLayoutScaleWidthBox_KeyDown;
                widthBox.LostKeyboardFocus += PageLayoutScaleWidthBox_LostKeyboardFocus;
            }
        }

        if (rendered.TryGetValue("Scale Height", out var heightControl) && heightControl is ComboBox heightBox)
        {
            PopulateRenderedComboItems(heightBox, PageLayoutInputParser.ScalePageCountOptions);
            if (!_renderedPageLayoutCombosWired)
            {
                heightBox.SelectionChanged += PageLayoutScaleHeightBox_SelectionChanged;
                heightBox.KeyDown += PageLayoutScaleHeightBox_KeyDown;
                heightBox.LostKeyboardFocus += PageLayoutScaleHeightBox_LostKeyboardFocus;
            }
        }

        if (rendered.TryGetValue("Scale Percent", out var percentControl) && percentControl is ComboBox percentBox)
        {
            PopulateRenderedComboItems(percentBox, PageLayoutInputParser.ScalePercentOptions);
            if (!_renderedPageLayoutCombosWired)
            {
                percentBox.SelectionChanged += PageLayoutScalePercentBox_SelectionChanged;
                percentBox.KeyDown += PageLayoutScalePercentBox_KeyDown;
                percentBox.LostKeyboardFocus += PageLayoutScalePercentBox_LostKeyboardFocus;
            }
        }

        _renderedPageLayoutCombosWired = true;

        // The rendered combos now exist; push the current sheet's scale values into them for display.
        SyncPageLayoutScaleToFitControls(_workbook.GetSheet(_currentSheetId));
    }

    /// <summary>System font families (sorted), cached for the rendered Home Font combo.</summary>
    private static readonly System.Collections.Generic.IReadOnlyList<string> HomeFontFamilyNames =
        System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Default font-size choices for the rendered Home Font Size combo.</summary>
    private static readonly System.Collections.Generic.IReadOnlyList<string> HomeFontSizeOptions =
        new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "24", "28", "36", "48", "72" };

    /// <summary>Number-format labels for the rendered Home Number Format combo.</summary>
    private static readonly System.Collections.Generic.IReadOnlyList<string> HomeNumberFormatLabels =
        HomeNumberFormatDropdownPlanner.Options.Select(option => option.Label).ToArray();

    /// <summary>Replaces a rendered combo's declarative placeholder items with the full host source.</summary>
    private static void PopulateRenderedComboItems(
        ComboBox combo,
        System.Collections.Generic.IReadOnlyList<string> items)
    {
        combo.ItemsSource = null;
        combo.Items.Clear();
        foreach (var item in items)
            combo.Items.Add(item);
    }

    /// <summary>Sets a combo's initial selected item without raising the host commit handlers.</summary>
    private void SetRenderedComboInitialSelection(ComboBox combo, string? value)
    {
        if (value is null) return;
        _suppressToolbarSync = true;
        try
        {
            combo.SelectedItem = value;
            combo.Text = value;
        }
        finally
        {
            _suppressToolbarSync = false;
        }
    }

    /// <summary>
    /// Re-points each backplane control's original x:Name to the visible rendered control so that
    /// <see cref="FrameworkElement.FindName"/> resolves the on-screen control (e.g. opening
    /// NumberFormatBox's dropdown via keytip is observable). Handlers keep using the backplane C#
    /// fields directly, so their state holders are unaffected — only name-based lookups move.
    /// </summary>
    private void RepointBackplaneNamesToRenderedControls(IReadOnlyDictionary<string, Control> rendered)
    {
        foreach (var (commandName, xName) in RibbonBackplaneControlNames)
        {
            if (!rendered.TryGetValue(commandName, out var target))
                continue;

            // Unregister any prior binding (a stub-backed control, or none for commands whose stub
            // was retired) then point the x:Name at the visible rendered control. These are split so
            // that when no prior name is registered, UnregisterName throwing does not skip RegisterName.
            try { UnregisterName(xName); }
            catch (System.ArgumentException) { /* name was not registered (retired stub) — fine */ }

            try { RegisterName(xName, target); }
            catch (System.ArgumentException) { /* already registered to this target — fine */ }
        }
    }

    private static IEnumerable<DependencyObject> EnumerateLogicalTree(DependencyObject root)
    {
        // A control's ContextMenu (its dropdown contents) is not always reached by GetChildren,
        // so descend into it explicitly to register the menu-item commands too.
        if (root is FrameworkElement { ContextMenu: { } contextMenu })
        {
            yield return contextMenu;
            foreach (var descendant in EnumerateLogicalTree(contextMenu))
                yield return descendant;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject node)
                continue;

            yield return node;
            foreach (var descendant in EnumerateLogicalTree(node))
                yield return descendant;
        }
    }

}
