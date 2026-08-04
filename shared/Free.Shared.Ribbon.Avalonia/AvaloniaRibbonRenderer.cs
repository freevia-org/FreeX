using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Threading;
using Free.Shared.Ribbon;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using System.Runtime.CompilerServices;

namespace Free.Shared.Ribbon.Avalonia;

/// <summary>
/// Avalonia (cross-platform) realization of a declarative <see cref="RibbonTab"/>.
/// Visually replicates the WPF ribbon (RibbonWpfRenderer + the WPF style resources): a flat white
/// surface with a horizontal strip of groups, each a content row over a header label, controls laid out
/// by <see cref="RibbonControl.PreferredLayout"/> exactly as WPF does — Large = hero (big icon above
/// label), Medium = small icon + label in a row, Small = icon-only — groups that declare a
/// <see cref="RibbonRowBreak"/> stacking into explicit horizontal rows. Buttons are flat (transparent
/// idle, a light hover tint, a subtle checked fill for toggles), tabs use WPF's flat header with an
/// accent underline on the selected tab, and <see cref="RibbonCheckBox"/> renders as a real check box.
/// Behavior is resolved through an <see cref="IRibbonCommandRegistry"/> keyed by command id.
/// </summary>
public static class AvaloniaRibbonRenderer
{
    private const string FileRibbonTabId = "FileTab";
    private const string KeyTipBadgeTag = "RibbonKeyTipBadge";
    private const string SelectedTabUnderlineTag = "FreeX.SelectedTabUnderline";
    private const string PopupChromeClass = "freex-ribbon-popup-chrome";
    private const string SubmenuPlacementClass = "freex-ribbon-submenu-placement";
    private const double RibbonCheckBoxHeight = 16;
    private const double RibbonCheckGlyphSize = 11;
    private const int MaxRowsPerColumn = 3;
    private static readonly IReadOnlyDictionary<string, string> ContextualTabKeyTips =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PivotTableAnalyzeTab"] = "JA",
            ["PivotTableDesignTab"] = "JD",
            ["ChartDesignTab"] = "JC",
            ["ChartFormatTab"] = "JF",
            ["ShapeFormatTab"] = "JS",
            ["PictureFormatTab"] = "JP",
            ["TableDesignTab"] = "JT",
        };
    private static readonly AttachedProperty<string?> KeyTipProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("KeyTip", typeof(AvaloniaRibbonRenderer));
    private static readonly IReadOnlySet<string> StaticDrawUnavailableCommandIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Crop Picture",
        "Shape Gradient",
        "Shape Effects",
    };
    private static readonly ConditionalWeakTable<CheckBox, CheckBoxExecutionState> CheckBoxExecutionStates = new();
    private static readonly ConditionalWeakTable<Control, KeyTipFlyoutState> KeyTipFlyoutStates = new();
    private static readonly ConditionalWeakTable<MenuItem, MenuKeyTipState> MenuKeyTipStates = new();
    private static readonly ConditionalWeakTable<Application, object> PopupChromeStyleApplications = new();

    private sealed class KeyTipFlyoutState
    {
        public List<FlyoutBase> OpenFlyouts { get; } = new();
        public HashSet<MenuFlyout> RegisteredMenuFlyouts { get; } = new();
        public bool IsVisible { get; set; }
    }

    private sealed class MenuKeyTipState
    {
        public required Border Badge { get; init; }
        public object? OriginalIcon { get; set; }
    }

    private sealed class CheckBoxExecutionState
    {
        internal bool IsSynchronizing;
    }

    private sealed class ComboExecutionState
    {
        internal bool IsSynchronizing;
        internal bool HasPendingSelectionCommit;
        internal string? PendingSelectionValue;
    }

    private static readonly ConditionalWeakTable<ComboBox, ComboExecutionState> ComboExecutionStates = new();

    internal static AvaloniaRibbonPalette ResolvePalette(RibbonVisualPalette? palette = null) =>
        new(palette ?? RibbonVisualPalette.FromTheme(BrandThemes.FreeX));

    internal sealed class AvaloniaRibbonPalette
    {
        public AvaloniaRibbonPalette(RibbonVisualPalette palette)
        {
            SurfaceColor = AvaloniaThemeApplier.ToColor(palette.Surface);
            AccentColor = AvaloniaThemeApplier.ToColor(palette.Accent);
            DividerColor = AvaloniaThemeApplier.ToColor(palette.Divider);
            InlineDividerColor = AvaloniaThemeApplier.ToColor(palette.InlineDivider);
            GroupLabelColor = AvaloniaThemeApplier.ToColor(palette.GroupLabel);
            HoverColor = AvaloniaThemeApplier.ToColor(palette.Hover);
            HoverBorderColor = AvaloniaThemeApplier.ToColor(palette.HoverBorder);
            CheckedColor = AvaloniaThemeApplier.ToColor(palette.Checked);
            TabHoverColor = AvaloniaThemeApplier.ToColor(palette.TabHover);
            TabStripColor = AvaloniaThemeApplier.ToColor(palette.TabStrip);
            TabTextColor = AvaloniaThemeApplier.ToColor(palette.TabText);

            SurfaceBrush = new ImmutableSolidColorBrush(SurfaceColor);
            AccentBrush = new ImmutableSolidColorBrush(AccentColor);
            DividerBrush = new ImmutableSolidColorBrush(DividerColor);
            InlineDividerBrush = new ImmutableSolidColorBrush(InlineDividerColor);
            GroupLabelBrush = new ImmutableSolidColorBrush(GroupLabelColor);
            HoverBrush = new ImmutableSolidColorBrush(HoverColor);
            HoverBorderBrush = new ImmutableSolidColorBrush(HoverBorderColor);
            CheckedBrush = new ImmutableSolidColorBrush(CheckedColor);
            TabHoverBrush = new ImmutableSolidColorBrush(TabHoverColor);
            TabStripBrush = new ImmutableSolidColorBrush(TabStripColor);
            TabTextBrush = new ImmutableSolidColorBrush(TabTextColor);
            CheckBoxTemplate = CreateRibbonCheckBoxTemplate(this);
        }

        internal Color SurfaceColor { get; }
        internal Color AccentColor { get; }
        internal Color DividerColor { get; }
        internal Color InlineDividerColor { get; }
        internal Color GroupLabelColor { get; }
        internal Color HoverColor { get; }
        internal Color HoverBorderColor { get; }
        internal Color CheckedColor { get; }
        internal Color TabHoverColor { get; }
        internal Color TabStripColor { get; }
        internal Color TabTextColor { get; }
        internal IBrush SurfaceBrush { get; }
        internal IBrush AccentBrush { get; }
        internal IBrush DividerBrush { get; }
        internal IBrush InlineDividerBrush { get; }
        internal IBrush GroupLabelBrush { get; }
        internal IBrush HoverBrush { get; }
        internal IBrush HoverBorderBrush { get; }
        internal IBrush CheckedBrush { get; }
        internal IBrush TabHoverBrush { get; }
        internal IBrush TabStripBrush { get; }
        internal IBrush TabTextBrush { get; }
        internal FuncControlTemplate<CheckBox> CheckBoxTemplate { get; }
    }
    private static readonly FontFamily RibbonFontFamily =
        new("Segoe UI, Arial, Liberation Sans, Noto Sans, DejaVu Sans, Helvetica, sans-serif");
    private static readonly FuncControlTemplate<Button> RibbonButtonTemplate = new((button, _) =>
    {
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = button });
        presenter.Bind(Layoutable.HorizontalAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = button });
        presenter.Bind(Layoutable.VerticalAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = button });

        var border = new Border
        {
            CornerRadius = new CornerRadius(1),
            Child = presenter,
        };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = button });
        border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = button });
        border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = button });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = button });
        return border;
    });
    private static readonly FuncControlTemplate<ToggleButton> RibbonToggleButtonTemplate = new((button, _) =>
    {
        var presenter = new ContentPresenter();
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = button });
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = button });
        presenter.Bind(Layoutable.HorizontalAlignmentProperty, new Binding(nameof(ContentControl.HorizontalContentAlignment)) { Source = button });
        presenter.Bind(Layoutable.VerticalAlignmentProperty, new Binding(nameof(ContentControl.VerticalContentAlignment)) { Source = button });

        var border = new Border
        {
            CornerRadius = new CornerRadius(1),
            Child = presenter,
        };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = button });
        border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = button });
        border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = button });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = button });
        return border;
    });
    private static FuncControlTemplate<CheckBox> CreateRibbonCheckBoxTemplate(AvaloniaRibbonPalette palette) => new((checkBox, _) =>
    {
        var checkMark = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,5.5 L4.4,8 L9,2.7"),
            Stroke = palette.AccentBrush,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            IsVisible = checkBox.IsChecked == true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        checkBox.PropertyChanged += (_, args) =>
        {
            if (args.Property == ToggleButton.IsCheckedProperty)
                checkMark.IsVisible = checkBox.IsChecked == true;
        };

        var indicator = new Border
        {
            Width = RibbonCheckGlyphSize,
            Height = RibbonCheckGlyphSize,
            Background = palette.SurfaceBrush,
            BorderBrush = palette.HoverBorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 4, 0),
            Child = checkMark,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var presenter = new ContentPresenter
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content)) { Source = checkBox });
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate)) { Source = checkBox });

        return new Border
        {
            Padding = new Thickness(0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    indicator,
                    presenter,
                },
            },
        };
    });
    private static readonly FuncControlTemplate<TabItem> RibbonTabItemTemplate = new((tabItem, _) =>
    {
        var presenter = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(HeaderedContentControl.Header)) { Source = tabItem });

        var border = new Border
        {
            Child = presenter,
        };
        border.Bind(Border.BackgroundProperty, new Binding(nameof(TemplatedControl.Background)) { Source = tabItem });
        border.Bind(Border.BorderBrushProperty, new Binding(nameof(TemplatedControl.BorderBrush)) { Source = tabItem });
        border.Bind(Border.BorderThicknessProperty, new Binding(nameof(TemplatedControl.BorderThickness)) { Source = tabItem });
        border.Bind(Border.PaddingProperty, new Binding(nameof(TemplatedControl.Padding)) { Source = tabItem });
        return border;
    });

    /// <summary>
    /// Syncs every <see cref="ToggleButton"/> in the ribbon's live visual tree with its command's
    /// current <see cref="IRibbonStatefulCommand.GetState"/>. Call from the host's RefreshShell so
    /// Bold/Italic/Underline and other format-state buttons reflect the active-cell state.
    /// </summary>
    public static void SyncToggleStates(Control ribbon, IRibbonCommandRegistry? registry, RibbonVisualPalette? palette = null)
    {
        if (registry is null)
            return;
        var resolvedPalette = ResolvePalette(palette);
        foreach (var toggle in ribbon.GetVisualDescendants().OfType<ToggleButton>())
        {
            if (toggle.Tag is string id && !string.IsNullOrEmpty(id)
                && registry.TryGet(new RibbonCommandId(id), out var cmd)
                && cmd is IRibbonStatefulCommand stateful)
            {
                ApplyRibbonCommandState(toggle, stateful.GetState(), resolvedPalette);
            }
        }

        foreach (var combo in ribbon.GetVisualDescendants().OfType<ComboBox>())
        {
            if (combo.Tag is string id && !string.IsNullOrEmpty(id)
                && registry.TryGet(new RibbonCommandId(id), out var cmd)
                && cmd is IRibbonStatefulCommand stateful)
            {
                ApplyRibbonCommandState(combo, stateful.GetState(), resolvedPalette);
            }
        }
    }

    /// <summary>Builds the content panel for one tab (the body shown under the tab header).</summary>
    public static Control BuildTabContent(
        RibbonTab tab,
        IRibbonCommandRegistry? registry = null,
        Action? afterExecute = null,
        RibbonVisualPalette? palette = null)
        => BuildTabContent(tab, registry, afterExecute, ResolvePalette(palette));

    private static Control BuildTabContent(
        RibbonTab tab,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette resolvedPalette)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var panel = new AvaloniaRibbonAdaptivePanel
        {
            MinHeight = RibbonVisualMetrics.TabContentMinHeight,
        };

        var first = true;
        var usedGroupKeyTips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildGroupDivider(resolvedPalette));
            var collapsedKeyTip = RibbonCollapsedGroupPresentationPlanner.DeriveGroupKeyTip(
                group.Header,
                usedGroupKeyTips);
            panel.Children.Add(new AvaloniaRibbonGroupHost(
                group,
                BuildGroup(group, registry, afterExecute, resolvedPalette),
                registry,
                afterExecute,
                resolvedPalette,
                collapsedKeyTip));
            first = false;
        }

        // WPF: Border { Background=FreeXRibbonSurfaceBrush (white); Padding 0,4,0,0 } — no accent rule.
        if (string.Equals(tab.Id, "DrawTab", StringComparison.Ordinal))
            DisableStaticDrawUnavailableCommands(panel);

        return new Border
        {
            Background = resolvedPalette.SurfaceBrush,
            Padding = new Thickness(0, RibbonVisualMetrics.TabContentTopPadding, 0, 0),
            Child = panel,
        };
    }

    private static void DisableStaticDrawUnavailableCommands(Control root)
    {
        ForEachRibbonDescendant(root, control =>
        {
            if (control.Tag is string id && StaticDrawUnavailableCommandIds.Contains(id))
                control.IsEnabled = false;
        });
    }

    private static void ForEachRibbonDescendant(Control control, Action<Control> visit)
    {
        visit(control);
        switch (control)
        {
            case TabControl tabControl:
                foreach (var item in tabControl.Items.OfType<TabItem>())
                {
                    if (item.Header is Control header)
                        ForEachRibbonDescendant(header, visit);
                    ForEachRibbonDescendant(item, visit);
                }
                break;
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                    ForEachRibbonDescendant(child, visit);
                break;
            case ContentControl { Content: Control content }:
                ForEachRibbonDescendant(content, visit);
                break;
            case Decorator { Child: { } child }:
                ForEachRibbonDescendant(child, visit);
                break;
        }
    }

    private static Control BuildTabHeader(string header, string? keyTip, AvaloniaRibbonPalette palette)
    {
        var grid = new Grid
        {
            ClipToBounds = false,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                // Reserve exactly the underline thickness as an Auto row pinned to the bottom so the
                // accent bar always gets its full height instead of being squeezed to a hairline.
                new RowDefinition { Height = GridLength.Auto },
            },
            Height = RibbonTabChromeMetrics.HeaderHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 42,
            Margin = new Thickness(0, 0, RibbonTabChromeMetrics.InterTabGap, 0),
        };
        AddHeaderChild(grid, new TextBlock
        {
            Text = header,
            FontSize = RibbonTabChromeMetrics.FontSize,
            FontFamily = RibbonFontFamily,
            Foreground = palette.TabTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(
                RibbonTabChromeMetrics.HeaderHorizontalPadding,
                RibbonTabChromeMetrics.HeaderVerticalPadding,
                RibbonTabChromeMetrics.HeaderHorizontalPadding,
                RibbonTabChromeMetrics.HeaderVerticalPadding),
        }, 0);
        if (!string.IsNullOrWhiteSpace(keyTip))
        {
            var badge = new Border
            {
                Tag = KeyTipBadgeTag,
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x76, 0x70, 0x5C)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                Child = new TextBlock
                {
                    Text = keyTip.Trim().ToUpperInvariant(),
                    FontFamily = RibbonFontFamily,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                },
            };
            Grid.SetRow(badge, 0);
            badge.ZIndex = 10;
            grid.Children.Add(badge);
        }
        AddHeaderChild(grid, new Border
        {
            Tag = SelectedTabUnderlineTag,
            Height = RibbonTabChromeMetrics.SelectedUnderlineThickness,
            MinHeight = RibbonTabChromeMetrics.SelectedUnderlineThickness,
            Background = palette.AccentBrush,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
        }, 1);
        return grid;
    }

    private static void AddHeaderChild(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    /// <summary>Builds a single <see cref="TabItem"/> for a tab (header + content), tagged with the tab id.</summary>
    private static TabItem BuildTabItem(RibbonTab tab, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette) => new()
    {
        Header = BuildTabHeader(tab.Header, tab.KeyTip ??
            (tab.IsContextual ? ContextualTabKeyTips.GetValueOrDefault(tab.Id) : null), palette),
        Content = BuildTabContent(tab, registry, afterExecute, palette),
        Tag = tab.Id,
    };

    private static TabItem BuildFileTabItem(AvaloniaRibbonPalette palette) => new()
    {
        Header = BuildTabHeader("File", "F", palette),
        Content = new Border
        {
            Background = palette.SurfaceBrush,
            MinHeight = RibbonVisualMetrics.TabContentMinHeight,
        },
        Tag = FileRibbonTabId,
    };

    /// <summary>
    /// Builds a <see cref="TabControl"/> over a whole definition's tabs. When a
    /// <paramref name="contextSource"/> is supplied, the visible tab set is resolved from its current
    /// context (normal tabs plus any contextual tab whose activation key is active) and the strip is
    /// re-synced whenever the source raises <see cref="IRibbonContextSource.ContextChanged"/>:
    /// newly-active contextual tabs are inserted in declaration order, deactivated ones removed, and the
    /// previously-selected tab preserved if it is still visible (otherwise the first tab is selected).
    /// With no source, the strip is the definition's non-contextual tabs (back-compat).
    /// </summary>
    public static Control BuildRibbon(
        RibbonDefinition definition,
        IRibbonCommandRegistry? registry = null,
        IRibbonContextSource? contextSource = null,
        Action? afterExecute = null,
        RibbonVisualPalette? palette = null,
        Action? onFileTabSelected = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var resolvedPalette = ResolvePalette(palette);

        // WPF: white ribbon surface; no extra TabControl bottom border — the selected tab's 3px accent
        // underline is the only visual divider between the tab strip and the content area below.
        // Avalonia Fluent stacks the 1px control border and the 3px tab accent as two separate visible
        // lines; removing the TabControl border leaves just the single accent underline, matching WPF.
        var tabControl = new TabControl
        {
            Background = resolvedPalette.SurfaceBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ApplyRibbonTheme(tabControl, resolvedPalette);
        tabControl.Items.Add(BuildFileTabItem(resolvedPalette));

        var initialTabs = contextSource is null
            ? (IReadOnlyList<RibbonTab>)definition.VisibleTabs.ToArray()
            : ResolveTabStripTabs(definition, contextSource.Current);

        foreach (var tab in initialTabs)
            tabControl.Items.Add(BuildTabItem(tab, registry, afterExecute, resolvedPalette));

        if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = tabControl.Items.Count > 1 ? 1 : 0;
        var lastContentTabIndex = tabControl.SelectedIndex;
        var restoringContentTab = false;
        UpdateTabHeaderSelectionStates(tabControl);
        tabControl.SelectionChanged += (_, _) =>
        {
            if (restoringContentTab)
                return;

            if ((tabControl.SelectedItem as TabItem)?.Tag is string selectedId &&
                string.Equals(selectedId, FileRibbonTabId, StringComparison.Ordinal))
            {
                onFileTabSelected?.Invoke();
                if (lastContentTabIndex >= 0 && lastContentTabIndex < tabControl.Items.Count)
                {
                    restoringContentTab = true;
                    tabControl.SelectedIndex = lastContentTabIndex;
                    restoringContentTab = false;
                }
            }
            else
            {
                lastContentTabIndex = tabControl.SelectedIndex;
            }

            UpdateTabHeaderSelectionStates(tabControl);
        };
        if (contextSource is not null)
            contextSource.ContextChanged += (_, _) => SyncContextualTabs(tabControl, definition, registry, contextSource, afterExecute, resolvedPalette);

        return tabControl;
    }

    public static void SetTopLevelKeyTipsVisible(Control ribbon, bool visible)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        var state = KeyTipFlyoutStates.GetOrCreateValue(ribbon);
        if (!visible)
            CloseKeyTipFlyouts(ribbon);

        state.IsVisible = visible;
        if (visible)
            RegisterRibbonFlyouts(ribbon, state);

        ForEachRibbonDescendant(ribbon, control =>
        {
            if (control is Border { Tag: string tag } badge &&
                string.Equals(tag, KeyTipBadgeTag, StringComparison.Ordinal))
            {
                badge.IsVisible = visible;
            }
        });
    }

    /// <summary>
    /// Shows or hides key-tip badges for the currently visible root scope of a menu flyout.
    /// Nested submenu badges are revealed when their parent scope opens; hiding always clears the
    /// complete menu tree so Escape, completion, and failed routes cannot leave stale badges behind.
    /// </summary>
    public static void SetMenuKeyTipsVisible(FlyoutBase flyout, bool visible)
    {
        if (flyout is MenuFlyout menuFlyout)
            SetMenuKeyTipsVisible(menuFlyout, visible);
    }

    public static void SetMenuKeyTipsVisible(MenuFlyout flyout, bool visible)
    {
        ArgumentNullException.ThrowIfNull(flyout);
        foreach (var item in flyout.Items.OfType<MenuItem>())
            SetMenuItemKeyTipsVisible(item, visible, recurse: !visible);
    }

    /// <summary>Reveals or clears the immediate child scope of a rendered submenu item.</summary>
    public static void SetMenuKeyTipsVisible(MenuItem parent, bool visible)
    {
        ArgumentNullException.ThrowIfNull(parent);
        foreach (var item in parent.Items.OfType<MenuItem>())
            SetMenuItemKeyTipsVisible(item, visible, recurse: !visible);
    }

    private static void RegisterRibbonFlyouts(Control ribbon, KeyTipFlyoutState state)
    {
        var controls = new List<Control>();
        ForEachRibbonDescendant(ribbon, controls.Add);
        foreach (var menuFlyout in controls
                     .OfType<Button>()
                     .Select(button => button.Flyout)
                     .OfType<MenuFlyout>()
                     .Distinct())
        {
            if (!state.RegisteredMenuFlyouts.Add(menuFlyout))
                continue;

            menuFlyout.Opened += (_, _) =>
            {
                if (!state.IsVisible)
                    return;

                SetMenuKeyTipsVisible(menuFlyout, true);
                if (!state.OpenFlyouts.Contains(menuFlyout))
                    state.OpenFlyouts.Add(menuFlyout);
            };
        }
    }

    private static void SetMenuItemKeyTipsVisible(MenuItem item, bool visible, bool recurse)
    {
        if (MenuKeyTipStates.TryGetValue(item, out var state))
        {
            // Avalonia exposes no stable trailing-content slot on MenuItem for arbitrary multi-character
            // key tips. The Icon presenter is the only public slot that accepts a live Control without
            // replacing Header; capture a pre-existing icon before the first hidden pass so cleanup
            // restores the menu's original geometry/content exactly.
            if (!visible && !ReferenceEquals(item.Icon, state.Badge))
                state.OriginalIcon = item.Icon;
            state.Badge.IsVisible = visible;
            item.Icon = visible ? state.Badge : state.OriginalIcon;
        }

        if (!recurse)
            return;

        foreach (var child in item.Items.OfType<MenuItem>())
            SetMenuItemKeyTipsVisible(child, visible, recurse: true);
    }

    private static void RegisterMenuKeyTip(MenuItem item, string? keyTip)
    {
        SetKeyTip(item, keyTip);
        if (string.IsNullOrWhiteSpace(keyTip))
            return;

        MenuKeyTipStates.Add(item, new MenuKeyTipState
        {
            Badge = CreateKeyTipBadge(keyTip),
            OriginalIcon = item.Icon,
        });
    }

    private static Border CreateKeyTipBadge(string keyTip) => new()
    {
        Tag = KeyTipBadgeTag,
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xCE)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x76, 0x70, 0x5C)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(2),
        Padding = new Thickness(3, 0),
        MinWidth = 18,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        IsVisible = false,
        Child = new TextBlock
        {
            Text = keyTip.Trim().ToUpperInvariant(),
            FontFamily = RibbonFontFamily,
            FontSize = 10,
            Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    public static bool TryActivateTopLevelKeyTip(Control ribbon, string keyTip)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        if (string.IsNullOrWhiteSpace(keyTip))
            return false;

        var tabControl = FindTabControl(ribbon);
        if (tabControl is null)
            return false;

        var normalized = keyTip.Trim();
        foreach (var item in tabControl.Items.OfType<TabItem>())
        {
            var badge = FindKeyTipBadge(item.Header as Control);
            if (badge?.Child is not TextBlock label ||
                !string.Equals(label.Text, normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            tabControl.SelectedItem = item;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Activates a catalogued ribbon key-tip path against the rendered controls. Menu paths open the
    /// live flyout and submenu before a terminal menu item is invoked, matching the WPF access-key scope.
    /// </summary>
    public static bool TryActivateKeyTip(Control ribbon, string keyTip)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        if (string.IsNullOrWhiteSpace(keyTip))
            return false;

        var tabControl = FindTabControl(ribbon);
        if (tabControl is null)
            return false;

        var normalized = keyTip.Trim().ToUpperInvariant();
        var tab = tabControl.Items.OfType<TabItem>()
            .Select(item => (Item: item, KeyTip: GetTabKeyTip(item)))
            .Where(candidate => candidate.KeyTip is not null &&
                                normalized.StartsWith(candidate.KeyTip, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.KeyTip!.Length)
            .FirstOrDefault();
        if (tab.Item is null || tab.KeyTip is null)
            return false;

        tabControl.SelectedItem = tab.Item;
        var remainder = normalized[tab.KeyTip.Length..];
        if (remainder.Length == 0)
            return true;

        var controls = new List<Control>();
        if (tab.Item.Content is Control content)
            ForEachRibbonDescendant(content, controls.Add);

        var candidateControl = controls
            .Select(control => (Control: control, KeyTip: GetKeyTip(control)))
            .Where(candidate => candidate.KeyTip is not null &&
                                remainder.StartsWith(candidate.KeyTip, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.KeyTip!.Length)
            .Select(candidate => (candidate.Control, KeyTip: candidate.KeyTip!))
            .FirstOrDefault();
        if (candidateControl.Control is null || !candidateControl.Control.IsEnabled)
            return false;

        var controlRemainder = remainder[candidateControl.KeyTip.Length..];
        if (candidateControl.Control is ComboBox combo)
        {
            if (controlRemainder.Length != 0)
                return false;

            combo.Focus();
            combo.IsDropDownOpen = true;
            return true;
        }

        if (candidateControl.Control is not Button button)
            return false;

        if (button.Flyout is not { } flyout)
        {
            if (controlRemainder.Length != 0)
                return false;

            if (button is ToggleButton toggle)
                toggle.IsChecked = toggle.IsChecked != true;
            else
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            return true;
        }

        if (flyout is not MenuFlyout menuFlyout)
        {
            if (controlRemainder.Length != 0)
                return false;

            OpenKeyTipFlyout(ribbon, flyout, button);
            return true;
        }

        OpenKeyTipFlyout(ribbon, menuFlyout, button);
        if (controlRemainder.Length == 0)
            return true;

        var activated = TryActivateMenuKeyTip(menuFlyout, controlRemainder);
        if (!activated)
            CloseKeyTipFlyouts(ribbon);
        return activated;
    }

    /// <summary>Closes flyouts opened by keyboard key-tip navigation without disturbing pointer flyouts.</summary>
    public static void CloseKeyTipFlyouts(Control ribbon)
    {
        ArgumentNullException.ThrowIfNull(ribbon);
        if (!KeyTipFlyoutStates.TryGetValue(ribbon, out var state))
            return;

        foreach (var flyout in state.OpenFlyouts)
        {
            SetMenuKeyTipsVisible(flyout, false);
            flyout.Hide();
        }
        state.OpenFlyouts.Clear();
    }

    private static void OpenKeyTipFlyout(Control ribbon, FlyoutBase flyout, Control anchor)
    {
        CloseKeyTipFlyouts(ribbon);
        flyout.ShowAt(anchor);
        SetMenuKeyTipsVisible(flyout, true);
        var state = KeyTipFlyoutStates.GetOrCreateValue(ribbon);
        if (!state.OpenFlyouts.Contains(flyout))
            state.OpenFlyouts.Add(flyout);
    }

    private static bool TryActivateMenuKeyTip(MenuFlyout flyout, string input)
    {
        IReadOnlyList<MenuItem> items = flyout.Items.OfType<MenuItem>().ToArray();
        var offset = 0;
        while (offset < input.Length)
        {
            var match = items
                .Select(item => (Item: item, KeyTip: GetKeyTip(item)))
                .Where(candidate => candidate.KeyTip is not null &&
                                    input[offset..].StartsWith(candidate.KeyTip, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.KeyTip!.Length)
                .FirstOrDefault();
            if (match.Item is null || match.KeyTip is null || !match.Item.IsEnabled)
                return false;

            offset += match.KeyTip.Length;
            if (match.Item.Items.Count > 0)
            {
                match.Item.IsSubMenuOpen = true;
                SetMenuKeyTipsVisible(match.Item, true);
                items = match.Item.Items.OfType<MenuItem>().ToArray();
                continue;
            }

            if (offset != input.Length)
                return false;

            match.Item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, match.Item));
            return true;
        }

        return true;
    }

    private static string? GetTabKeyTip(TabItem item) =>
        FindKeyTipBadge(item.Header as Control)?.Child is TextBlock label ? label.Text : null;

    private static string? GetKeyTip(Control control) => control.GetValue(KeyTipProperty);

    private static void SetKeyTip(Control control, string? keyTip)
    {
        if (!string.IsNullOrWhiteSpace(keyTip))
            control.SetValue(KeyTipProperty, keyTip.Trim().ToUpperInvariant());
    }

    private static TabControl? FindTabControl(Control control)
    {
        if (control is TabControl tabControl)
            return tabControl;

        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                    if (FindTabControl(child) is { } match)
                        return match;
                break;
            case ContentControl { Content: Control content }:
                return FindTabControl(content);
            case Decorator { Child: { } child }:
                return FindTabControl(child);
        }

        return null;
    }

    private static Border? FindKeyTipBadge(Control? control)
    {
        if (control is null)
            return null;
        if (control is Border { Tag: string tag } border &&
            string.Equals(tag, KeyTipBadgeTag, StringComparison.Ordinal))
        {
            return border;
        }

        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
                if (FindKeyTipBadge(child) is { } match)
                    return match;
        }
        else if (control is ContentControl { Content: Control content })
        {
            return FindKeyTipBadge(content);
        }
        else if (control is Decorator { Child: { } child })
        {
            return FindKeyTipBadge(child);
        }

        return null;
    }

    /// <summary>
    /// Reconciles the tab strip with the source's current context: the resolver yields the exact ordered
    /// set of tabs that should be visible; we diff by tab id (the <see cref="TabItem.Tag"/>), inserting
    /// missing tabs at their resolved position and removing stale ones, preserving the user's selection.
    /// </summary>
    private static void SyncContextualTabs(
        TabControl tabControl,
        RibbonDefinition definition,
        IRibbonCommandRegistry? registry,
        IRibbonContextSource contextSource,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var desired = ResolveTabStripTabs(definition, contextSource.Current);
        var selectedId = (tabControl.SelectedItem as TabItem)?.Tag as string;

        // Remove tabs no longer desired.
        var desiredIds = new HashSet<string>(desired.Select(t => t.Id), StringComparer.Ordinal);
        for (var i = tabControl.Items.Count - 1; i >= 0; i--)
        {
            if (tabControl.Items[i] is TabItem item &&
                item.Tag is string id &&
                !string.Equals(id, FileRibbonTabId, StringComparison.Ordinal) &&
                !desiredIds.Contains(id))
                tabControl.Items.RemoveAt(i);
        }

        // Insert missing tabs at their resolved (declaration-order) position.
        // We compute each insertion index against the LIVE TabControl by finding the desired
        // tab's closest predecessor (in the desired list) that is already present, then inserting
        // immediately after it.  This avoids the off-by-N shift that results from using the
        // desired-list ordinal (i+1) after earlier insertions have already moved indices forward.
        for (var i = 0; i < desired.Count; i++)
        {
            var tab = desired[i];
            var existingIndex = IndexOfTab(tabControl, tab.Id);
            if (existingIndex >= 0)
            {
                // Context sources also raise when the selected object changes inside an already-active
                // context. Rebuild contextual content so every control and flyout item re-queries its
                // stateful command while preserving the visible tab set and selected tab id.
                if (tab.Context is not null)
                {
                    tabControl.Items.RemoveAt(existingIndex);
                    tabControl.Items.Insert(
                        existingIndex,
                        BuildTabItem(tab, registry, afterExecute, palette));
                }
                continue;
            }

            // Find the closest predecessor in the desired list that already lives in the control.
            var insertAfter = 0; // default: insert after the File tab (index 0)
            for (var p = i - 1; p >= 0; p--)
            {
                var predecessorIndex = IndexOfTab(tabControl, desired[p].Id);
                if (predecessorIndex >= 0)
                {
                    insertAfter = predecessorIndex;
                    break;
                }
            }

            tabControl.Items.Insert(Math.Min(insertAfter + 1, tabControl.Items.Count), BuildTabItem(tab, registry, afterExecute, palette));
        }

        // Preserve selection if still visible; otherwise select the first tab.
        var restoreIndex = selectedId is null ? -1 : IndexOfTab(tabControl, selectedId);
        if (restoreIndex >= 0)
            tabControl.SelectedIndex = restoreIndex;
        else if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = tabControl.Items.Count > 1 ? 1 : 0;

        UpdateTabHeaderSelectionStates(tabControl);
    }

    private static void UpdateTabHeaderSelectionStates(TabControl tabControl)
    {
        foreach (var item in tabControl.Items.OfType<TabItem>())
            SetTabHeaderSelected(item.Header as Control, ReferenceEquals(item, tabControl.SelectedItem));
    }

    private static void SetTabHeaderSelected(Control? header, bool isSelected)
    {
        if (header is null)
            return;

        foreach (var border in EnumerateHeaderBorders(header))
        {
            if (border.Tag is string tag && string.Equals(tag, SelectedTabUnderlineTag, StringComparison.Ordinal))
                border.IsVisible = isSelected;
        }
    }

    private static IEnumerable<Border> EnumerateHeaderBorders(Control control)
    {
        if (control is Border border)
            yield return border;
        if (control is Panel panel)
        {
            foreach (var child in panel.Children.OfType<Control>())
            foreach (var match in EnumerateHeaderBorders(child))
                yield return match;
        }
    }

    private static int IndexOfTab(TabControl tabControl, string tabId)
    {
        for (var i = 0; i < tabControl.Items.Count; i++)
            if (tabControl.Items[i] is TabItem item && item.Tag is string id && string.Equals(id, tabId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static IReadOnlyList<RibbonTab> ResolveTabStripTabs(RibbonDefinition definition, RibbonContextState state)
    {
        var resolved = RibbonContextResolver.Resolve(definition, state);
        if (!resolved.Any(tab => tab.IsContextual))
            return resolved;

        var ordered = new List<RibbonTab>(resolved.Count);
        var contextual = resolved.Where(tab => tab.IsContextual)
            .OrderBy(tab => WpfContextualTabOrder(tab.Id))
            .ToArray();

        foreach (var tab in resolved)
        {
            if (tab.IsContextual)
                continue;

            if (string.Equals(tab.Id, "HelpTab", StringComparison.Ordinal))
                ordered.AddRange(contextual);

            ordered.Add(tab);
        }

        if (!ordered.Any(tab => tab.IsContextual))
            ordered.AddRange(contextual);

        return ordered;
    }

    private static int WpfContextualTabOrder(string tabId) => tabId switch
    {
        "ShapeFormatTab" => 0,
        "PictureFormatTab" => 1,
        "ChartDesignTab" => 2,
        "ChartFormatTab" => 3,
        "TableDesignTab" => 4,
        "PivotTableAnalyzeTab" => 5,
        "PivotTableDesignTab" => 6,
        _ => 100,
    };

    /// <summary>
    /// Applies the ribbon theme styles to the tab control, replicating the WPF look:
    /// <list type="bullet">
    /// <item>Tabs are flat (transparent, neutral foreground); the selected tab gets a white body and an
    /// accent underline — NOT green text/semibold (matches the WPF TabItem template).</item>
    /// <item>Ribbon buttons/toggles are flat: transparent idle, a light hover tint + subtle border on
    /// pointer-over, and a subtle accent-tinted fill when a toggle is checked (matches RibbonBtn /
    /// RibbonIconButton / RibbonToggleBtn).</item>
    /// </list>
    /// </summary>
    internal static void ApplyRibbonTheme(TabControl tabControl, AvaloniaRibbonPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(tabControl);
        palette ??= ResolvePalette();

        // ── Tab headers (gray strip, near-black labels; selected = white body + accent underline;
        // hover = soft accent tint). Matches the WPF TabItem ControlTemplate (MainWindowResources.xaml:
        // transparent template Border with BorderThickness 0,0,0,3; IsSelected -> accent border + white
        // body; IsMouseOver -> FreeXAccentSoftBrush). Foreground is the near-black FreeXTextBrush. ──
        var tabBase = new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.TabStripBrush),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.FontSizeProperty, RibbonTabChromeMetrics.FontSize),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
                new Setter(TemplatedControl.TemplateProperty, RibbonTabItemTemplate),
                new Setter(TemplatedControl.ForegroundProperty, palette.TabTextBrush),
                // Avalonia Fluent default tab height is ~48px vs WPF's compact header row; constrain it.
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, RibbonTabChromeMetrics.HeaderHeight),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)),
            },
        };

        var tabHover = new Style(x => x.OfType<TabItem>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.TabHoverBrush),
                new Setter(TemplatedControl.ForegroundProperty, palette.TabTextBrush),
            },
        };

        var tabSelected = new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                // WPF selected tab: white body + near-black label. The underline is drawn inside
                // BuildTabHeader so Avalonia Fluent cannot stack a second selected line under it.
                new Setter(TemplatedControl.BackgroundProperty, palette.SurfaceBrush),
                new Setter(TemplatedControl.ForegroundProperty, palette.TabTextBrush),
                new Setter(TemplatedControl.BorderBrushProperty, palette.AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
            },
        };

        var tabTemplateBorder = new Style(x => x.OfType<TabItem>().Class(":selected").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
            },
        };

        // Selected + hovered keeps the white body (don't let the hover tint repaint a selected tab).
        var tabSelectedHover = new Style(x => x.OfType<TabItem>().Class(":selected").Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.SurfaceBrush),
                new Setter(TemplatedControl.ForegroundProperty, palette.TabTextBrush),
            },
        };

        // Avalonia Fluent theme may override the TabItem Foreground via internal pseudo-class triggers;
        // targeting the rendered TextBlock directly wins over any theme-level override.
        var tabTextForeground = new Style(x => x.OfType<TabItem>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.ForegroundProperty, palette.TabTextBrush),
                new Setter(TextBlock.FontFamilyProperty, RibbonFontFamily),
            },
        };

        // ── Buttons: flat, transparent idle; light hover tint + subtle border on pointer-over. ──
        var buttonBase = new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
                new Setter(TemplatedControl.TemplateProperty, RibbonButtonTemplate),
            },
        };
        var buttonHover = new Style(x => x.OfType<Button>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.HoverBrush),
                new Setter(TemplatedControl.BorderBrushProperty, palette.HoverBorderBrush),
            },
        };

        // ── Toggle buttons: flat idle; hover tint; subtle accent fill when checked. ──
        var toggleBase = new Style(x => x.OfType<ToggleButton>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
            },
        };
        var toggleHover = new Style(x => x.OfType<ToggleButton>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.HoverBrush),
                new Setter(TemplatedControl.BorderBrushProperty, palette.HoverBorderBrush),
            },
        };
        var toggleChecked = new Style(x => x.OfType<ToggleButton>().Class(":checked"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.CheckedBrush),
                new Setter(TemplatedControl.BorderBrushProperty, palette.AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        };
        var toggleCheckedHover = new Style(x => x.OfType<ToggleButton>().Class(":checked").Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, palette.CheckedBrush),
                new Setter(TemplatedControl.BorderBrushProperty, palette.AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        };
        var toggleCheckedTemplateBorder = new Style(x => x.OfType<ToggleButton>().Class(":checked").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, palette.CheckedBrush),
                new Setter(Border.BorderBrushProperty, palette.AccentBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
            },
        };

        // ComboBox: Avalonia Fluent default height ~34px vs WPF ~26px — constrain to match.
        var comboBase = new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, RibbonVisualMetrics.SmallRowHeight),
                new Setter(Layoutable.HeightProperty, RibbonVisualMetrics.SmallRowHeight),
                new Setter(Layoutable.MaxHeightProperty, RibbonVisualMetrics.SmallRowHeight),
                new Setter(TemplatedControl.BorderBrushProperty, palette.DividerBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.FontSizeProperty, 12d),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0, 18, 0)),
            },
        };

        var checkBase = new Style(x => x.OfType<CheckBox>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, RibbonCheckBoxHeight),
                new Setter(Layoutable.HeightProperty, RibbonCheckBoxHeight),
                new Setter(Layoutable.MaxHeightProperty, RibbonCheckBoxHeight),
                new Setter(TemplatedControl.FontSizeProperty, 12d),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(TemplatedControl.TemplateProperty, palette.CheckBoxTemplate),
            },
        };

        var disabledButtons = new Style(x => x.OfType<Button>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(Visual.OpacityProperty, 0.45d),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            },
        };
        var disabledToggles = new Style(x => x.OfType<ToggleButton>().Class(":disabled"))
        {
            Setters =
            {
                new Setter(Visual.OpacityProperty, 0.45d),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
            },
        };
        var disabledButtonTemplateBorder = new Style(x => x.OfType<Button>().Class(":disabled").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
            },
        };
        var disabledToggleTemplateBorder = new Style(x => x.OfType<ToggleButton>().Class(":disabled").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
            },
        };
        var disabledChecks = new Style(x => x.OfType<CheckBox>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45d) },
        };
        var disabledCombos = new Style(x => x.OfType<ComboBox>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.55d) },
        };

        tabControl.Styles.Add(tabBase);
        tabControl.Styles.Add(tabHover);
        tabControl.Styles.Add(tabSelected);
        tabControl.Styles.Add(tabSelectedHover);
        tabControl.Styles.Add(tabTemplateBorder);
        tabControl.Styles.Add(tabTextForeground);
        tabControl.Styles.Add(buttonBase);
        tabControl.Styles.Add(buttonHover);
        tabControl.Styles.Add(toggleBase);
        tabControl.Styles.Add(toggleHover);
        tabControl.Styles.Add(toggleChecked);
        tabControl.Styles.Add(toggleCheckedHover);
        tabControl.Styles.Add(toggleCheckedTemplateBorder);
        tabControl.Styles.Add(comboBase);
        tabControl.Styles.Add(checkBase);
        tabControl.Styles.Add(disabledButtons);
        tabControl.Styles.Add(disabledToggles);
        tabControl.Styles.Add(disabledButtonTemplateBorder);
        tabControl.Styles.Add(disabledToggleTemplateBorder);
        tabControl.Styles.Add(disabledChecks);
        tabControl.Styles.Add(disabledCombos);

        var popupBorder = new Style(x => x.OfType<MenuFlyoutPresenter>().Class(PopupChromeClass).Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, palette.SurfaceBrush),
                new Setter(Border.BorderBrushProperty, palette.DividerBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(RibbonVisualMetrics.PopupChrome.BorderThickness)),
                new Setter(Border.CornerRadiusProperty, new CornerRadius(RibbonVisualMetrics.PopupChrome.CornerRadius)),
                new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = RibbonVisualMetrics.PopupChrome.ShadowDepth,
                    Blur = RibbonVisualMetrics.PopupChrome.ShadowBlurRadius,
                    Color = Color.FromArgb((byte)(RibbonVisualMetrics.PopupChrome.ShadowOpacity * 255), 0, 0, 0),
                })),
            },
        };
        tabControl.Styles.Add(popupBorder);
    }

    private static Control BuildGroup(RibbonGroup group, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        var grid = new Grid
        {
            Tag = group.Id,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(RibbonVisualMetrics.GroupLabelHeight)),
            },
        };

        var content = BuildGroupContent(group, registry, afterExecute, palette);
        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        // WPF RibbonGroupLabelBorder: a 1px top rule in FreeXBorderBrush over the centered muted label.
        var labelBorder = new Border
        {
            BorderBrush = palette.DividerBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = RibbonVisualMetrics.GroupLabelHeight,
            Child = new TextBlock
            {
                Text = group.Header,
                FontSize = 12,
                FontFamily = RibbonFontFamily,
                Foreground = palette.GroupLabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Grid.SetRow(labelBorder, 1);
        grid.Children.Add(labelBorder);

        return grid;
    }

    private static Control BuildGroupContent(RibbonGroup group, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        var lane = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 2, 2, 0),
        };

        var controls = group.Controls;
        var index = 0;

        // Leading large "hero" buttons each occupy their own full-height column (mirrors WPF).
        while (index < controls.Count && controls[index].PreferredLayout == RibbonCommandLayoutKind.Large)
        {
            lane.Children.Add(BuildLargeControl(controls[index], registry, afterExecute, palette));
            index++;
        }

        var rest = controls.Skip(index).ToList();
        if (rest.Count == 0)
            return lane;

        if (rest.Any(c => c is RibbonRowBreak))
            lane.Children.Add(BuildExplicitRows(rest, registry, afterExecute, palette));
        else
            BuildAutoColumns(rest, lane, registry, afterExecute, palette);

        return lane;
    }

    // Groups that declare RowBreaks lay out as stacked horizontal rows (e.g. Font: combos row, then B/I/U row).
    private static Control BuildExplicitRows(
        IReadOnlyList<RibbonControl> controls,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Top };
        var current = NewRow(isFirst: true);

        foreach (var control in controls)
        {
            if (control is RibbonRowBreak)
            {
                rows.Children.Add(current);
                current = NewRow(isFirst: false);
                continue;
            }

            current.Children.Add(BuildInlineControl(control, registry, afterExecute, palette));
        }

        rows.Children.Add(current);
        return rows;
    }

    private static StackPanel NewRow(bool isFirst) => new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(0, isFirst ? 0 : 2, 0, 0),
    };

    // Groups without explicit rows pack medium/small/combo controls into columns of up to three.
    private static void BuildAutoColumns(
        IReadOnlyList<RibbonControl> controls,
        StackPanel lane,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        StackPanel? column = null;
        var columnIsCombo = false;

        void Flush()
        {
            if (column is not null)
            {
                lane.Children.Add(column);
                column = null;
            }
        }

        foreach (var control in controls)
        {
            switch (control)
            {
                case RibbonSeparator:
                    Flush();
                    lane.Children.Add(BuildInlineDivider(palette));
                    break;
                case { PreferredLayout: RibbonCommandLayoutKind.Large }:
                    Flush();
                    lane.Children.Add(BuildLargeControl(control, registry, afterExecute, palette));
                    break;
                default:
                    // Keep comboboxes and buttons in separate columns so a group reads like WPF's.
                    var isCombo = control is RibbonComboBox;
                    if (column is not null && columnIsCombo != isCombo)
                        Flush();
                    column ??= NewColumn();
                    columnIsCombo = isCombo;
                    column.Children.Add(BuildInlineControl(control, registry, afterExecute, palette));
                    if (column.Children.Count >= MaxRowsPerColumn)
                        Flush();
                    break;
            }
        }

        Flush();
    }

    private static StackPanel NewColumn() => new()
    {
        Orientation = Orientation.Vertical,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(1, 1, 1, 0),
    };

    // Dispatches a single non-large control to its WPF-matching form: combo, checkbox, icon-only (Small),
    // or icon+label (Medium / default).
    private static Control BuildInlineControl(
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var element = control switch
        {
            RibbonSeparator => BuildInlineDivider(palette),
            RibbonComboBox combo => BuildComboControl(combo, registry, afterExecute, palette),
            RibbonCheckBox check => BuildCheckControl(check, registry, afterExecute, palette),
            { PreferredLayout: RibbonCommandLayoutKind.Large } => BuildLargeControl(control, registry, afterExecute, palette),
            { PreferredLayout: RibbonCommandLayoutKind.Small } => BuildIconControl(control, registry, afterExecute, palette),
            _ => BuildMediumControl(control, registry, afterExecute, palette),
        };
        if (control is not RibbonSplitButton)
            SetKeyTip(element, control.KeyTip);
        return element;
    }

    // WPF BuildCheckControl: a real CheckBox carrying the label.
    private static Control BuildCheckControl(RibbonCheckBox check, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        var box = new CheckBox
        {
            Content = check.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            Height = RibbonCheckBoxHeight,
            MinHeight = RibbonCheckBoxHeight,
            MaxHeight = RibbonCheckBoxHeight,
            Template = palette.CheckBoxTemplate,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1),
            Tag = check.CommandId.Value,
        };
        ApplyStateAndEnablement(box, check.CommandId, registry, palette);
        var executionState = CheckBoxExecutionStates.GetOrCreateValue(box);
        box.IsCheckedChanged += (_, _) =>
        {
            if (!executionState.IsSynchronizing)
                Execute(check.CommandId, registry, afterExecute);
        };
        return box;
    }

    // WPF BuildLargeControl: a hero button — big icon (~32px) above a centered (wrapping) caption. For a
    // split/dropdown control, WPF folds a centered chevron into a band BELOW the label (a distinct dropdown
    // affordance) rather than running "▾" into the caption text.
    private static Control BuildLargeControl(RibbonControl control, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildLargeSplitControl(splitButton, registry, afterExecute, palette);

        // Center the icon+label cluster vertically in the hero button so large icons sit in the middle
        // of the row like Windows, instead of pinned to the top (StackPanel defaults to top alignment).
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(NewIcon(control, RibbonVisualMetrics.LargeIconSize, HorizontalAlignment.Center));

        // The hero button is Width 70 / Padding 3 (≈64 content). WPF's caption wraps on WORD boundaries
        // (so "Conditional Formatting" lays out as "Conditional" / "Formatting"); Avalonia's plain Wrap
        // would break the long word mid-character ("Conditiona\nl"). WrapWithOverflow wraps at word
        // boundaries and never splits a word, and a MaxWidth matching the button content keeps the two
        // words on their own lines exactly like WPF.
        stack.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWithOverflow,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 74,
        });

        // Large dropdown affordance: a centered chevron on its own line under the label. True split
        // buttons are handled by BuildLargeSplitControl so their primary and menu actions remain distinct.
        if (HasMenu(control))
        {
            stack.Children.Add(Chevron(new Thickness(0, 1, 0, 0), palette));
        }

        // WPF RibbonLargeButton: compact hero column, Padding 3,2.
        var button = NewButtonLike(control, palette);
        button.Width = 80;
        button.Height = 76;
        button.Padding = new Thickness(4, 2);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)button).Content = stack;
        WireControl(button, control, registry, afterExecute, palette);
        return button;
    }

    private static Control BuildLargeSplitControl(
        RibbonSplitButton control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var primaryContent = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                NewIcon(control, RibbonVisualMetrics.LargeIconSize, HorizontalAlignment.Center),
                new TextBlock
                {
                    Text = control.Label,
                    FontSize = 12,
                    FontFamily = RibbonFontFamily,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.WrapWithOverflow,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    MaxWidth = 74,
                },
            },
        };
        var primary = new Button
        {
            Content = primaryContent,
            Tag = control.CommandId.Value,
            Padding = new Thickness(4, 2, 4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        primary.Click += (_, _) => Execute(control.CommandId, registry, afterExecute);

        // Use a plain Button with an attached flyout. DropDownButton supplies its own built-in
        // arrow in addition to Content, which doubled the shared chevron on Linux.
        var dropdown = new Button
        {
            Content = Chevron(new Thickness(0), palette),
            Tag = $"{control.CommandId.Value}.Dropdown",
            Width = 80,
            MinWidth = 80,
            Height = 20,
            MinHeight = 20,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var dropdownFlyout = control.Menu.BuildFlyout(registry, afterExecute);
        ConfigureMenuFlyout(dropdownFlyout, dropdown, palette, RibbonPopupInteractionContract.CollapsedGroup);
        dropdown.Flyout = dropdownFlyout;
        SetKeyTip(dropdown, control.KeyTip);

        ApplyStateAndEnablement(primary, control.CommandId, registry, palette);
        ApplyControlEnablement(dropdown, control, registry, palette);

        var split = new Grid
        {
            Width = 80,
            Height = 76,
            RowDefinitions = new RowDefinitions("*,20"),
        };
        Grid.SetRow(primary, 0);
        Grid.SetRow(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    // WPF BuildMediumControl: small icon (16px) + label in a horizontal row.
    private static Control BuildMediumControl(RibbonControl control, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildMediumSplitControl(splitButton, registry, afterExecute, palette);

        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(NewIcon(control, RibbonVisualMetrics.MediumIconSize, HorizontalAlignment.Center));
        content.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
        });
        if (HasMenu(control))
            content.Children.Add(Chevron(palette));

        // WPF RibbonBtn: Height 22, MinWidth 84, left-aligned content, Padding 4,2.
        var button = NewButtonLike(control, palette);
        button.Height = RibbonVisualMetrics.SmallRowHeight;
        button.MinWidth = 88;
        button.Padding = new Thickness(4, 2);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)button).Content = content;
        WireControl(button, control, registry, afterExecute, palette);
        return button;
    }

    private static Control BuildMediumSplitControl(
        RibbonSplitButton control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(NewIcon(control, RibbonVisualMetrics.MediumIconSize, HorizontalAlignment.Center));
        content.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
        });

        var primary = new Button
        {
            Content = content,
            Tag = control.CommandId.Value,
            MinWidth = 84,
            Height = RibbonVisualMetrics.SmallRowHeight,
            Padding = new Thickness(4, 2),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        WireControl(primary, control, registry, afterExecute, palette, attachMenu: false);

        var dropdown = new Button
        {
            Content = Chevron(palette),
            Tag = $"{control.CommandId.Value}.Dropdown",
            Width = 20,
            MinWidth = 20,
            Height = RibbonVisualMetrics.SmallRowHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        WireControl(dropdown, control, registry, afterExecute, palette);
        SetKeyTip(dropdown, control.KeyTip);

        var split = new Grid { MinWidth = 104 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        Grid.SetColumn(primary, 0);
        Grid.SetColumn(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    // WPF BuildIconControl: Small layout is ICON-ONLY (~18px) — no label. With a menu, append a chevron.
    private static Control BuildIconControl(RibbonControl control, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        if (control is RibbonSplitButton splitButton)
            return BuildIconSplitControl(splitButton, registry, afterExecute, palette);

        var hasMenu = HasMenu(control);
        Control content;
        if (hasMenu)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(NewIcon(control, RibbonVisualMetrics.SmallIconSize, HorizontalAlignment.Center));
            stack.Children.Add(Chevron(palette));
            content = stack;
        }
        else
        {
            content = NewIcon(control, RibbonVisualMetrics.SmallIconSize, HorizontalAlignment.Center);
        }

        // WPF RibbonIconButton / RibbonIconToggleButton: icon-centred compact button, wider when a menu chevron is present.
        var button = NewButtonLike(control, palette);
        button.Width = hasMenu ? 42 : 30;
        button.Height = RibbonVisualMetrics.SmallRowHeight;
        button.Padding = new Thickness(1, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)button).Content = content;
        WireControl(button, control, registry, afterExecute, palette);
        return button;
    }

    private static Control BuildIconSplitControl(
        RibbonSplitButton control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var primary = new Button
        {
            Content = NewIcon(control, RibbonVisualMetrics.SmallIconSize, HorizontalAlignment.Center),
            Tag = control.CommandId.Value,
            Width = 30,
            Height = RibbonVisualMetrics.SmallRowHeight,
            Padding = new Thickness(1, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        WireControl(primary, control, registry, afterExecute, palette, attachMenu: false);

        var dropdown = new Button
        {
            Content = Chevron(new Thickness(0), palette),
            Tag = $"{control.CommandId.Value}.Dropdown",
            Width = 14,
            MinWidth = 14,
            Height = RibbonVisualMetrics.SmallRowHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        WireControl(dropdown, control, registry, afterExecute, palette);
        SetKeyTip(dropdown, control.KeyTip);

        var split = new Grid { Width = 44, MinWidth = 44 };
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        Grid.SetColumn(primary, 0);
        Grid.SetColumn(dropdown, 1);
        split.Children.Add(primary);
        split.Children.Add(dropdown);
        return split;
    }

    private static Control BuildComboControl(RibbonComboBox combo, IRibbonCommandRegistry? registry, Action? afterExecute, AvaloniaRibbonPalette palette)
    {
        var box = new ComboBox
        {
            Width = combo.Width ?? 110,
            Height = RibbonVisualMetrics.SmallRowHeight,
            MinHeight = RibbonVisualMetrics.SmallRowHeight,
            MaxHeight = RibbonVisualMetrics.SmallRowHeight,
            IsEditable = true,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            Padding = new Thickness(6, 0, 18, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 2, 0),
            Background = palette.SurfaceBrush,
            BorderBrush = palette.DividerBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = false,
            Tag = combo.CommandId.Value,
        };
        foreach (var item in combo.Items)
            box.Items.Add(item);
        var executionState = ComboExecutionStates.GetOrCreateValue(box);
        RibbonCommandState? state = null;
        if (registry is not null
            && registry.TryGet(combo.CommandId, out var command)
            && command is IRibbonStatefulCommand stateful)
        {
            state = stateful.GetState();
        }

        // Seed from the command state before applying the normal first-item fallback. This matters
        // for editable values such as a user-entered font or scale that are not in the catalog.
        executionState.IsSynchronizing = true;
        try
        {
            var stateIndex = state?.Value is { Length: > 0 } value
                ? combo.Items.ToList().FindIndex(item => string.Equals(item, value, StringComparison.Ordinal))
                : -1;
            if (stateIndex >= 0)
                box.SelectedIndex = stateIndex;
            else if (state?.Value is { } stateValue)
            {
                box.SelectedIndex = -1;
                box.Text = stateValue;
            }
            else if (combo.Items.Count > 0)
                box.SelectedIndex = 0;
        }
        finally
        {
            executionState.IsSynchronizing = false;
        }

        // A user pick executes the control's command, passing the chosen value so the host applies it
        // (e.g. font size). The initial programmatic SelectedIndex is suppressed by a ready flag.
        var ready = false;
        box.SelectionChanged += (_, _) =>
        {
            if (!ready || executionState.IsSynchronizing)
                return;

            var value = ResolveComboValue(box);
            ExecuteWithValue(combo.CommandId, registry, value, afterExecute);
            executionState.HasPendingSelectionCommit = true;
            executionState.PendingSelectionValue = value;
        };
        box.KeyDown += (_, e) =>
        {
            if (!ready || executionState.IsSynchronizing)
                return;

            if (e.Key != Key.Enter)
            {
                // The WPF duplicate window only spans the selection event immediately followed by
                // Enter. Escape, arrow navigation, and text input begin a new interaction, so a
                // later Enter must not be swallowed by an old selection commit.
                ClearPendingComboSelection(executionState);
                return;
            }

            var value = ResolveComboValue(box);
            if (executionState.HasPendingSelectionCommit
                && string.Equals(executionState.PendingSelectionValue, value, StringComparison.Ordinal))
            {
                // Avalonia can deliver Enter after the selection event. WPF has already committed
                // that selection, so do not execute the same value a second time.
                executionState.HasPendingSelectionCommit = false;
                executionState.PendingSelectionValue = null;
            }
            else
            {
                executionState.HasPendingSelectionCommit = false;
                executionState.PendingSelectionValue = null;
                ExecuteWithValue(combo.CommandId, registry, value, afterExecute);
            }

            e.Handled = true;
        };
        box.LostFocus += (_, _) =>
        {
            if (!ready || executionState.IsSynchronizing)
                return;

            var value = ResolveComboValue(box);
            if (executionState.HasPendingSelectionCommit
                && string.Equals(executionState.PendingSelectionValue, value, StringComparison.Ordinal))
            {
                // SelectionChanged already committed this value. WPF does not execute it a second
                // time merely because the editable combo then loses keyboard focus.
                ClearPendingComboSelection(executionState);
                return;
            }

            ClearPendingComboSelection(executionState);
            ExecuteWithValue(combo.CommandId, registry, value, afterExecute);
        };
        ready = true;

        ApplyEnablement(box, combo, registry, palette);
        return box;
    }

    private static Control NewIcon(RibbonControl control, double size, HorizontalAlignment h)
    {
        // Pass the visible command label so the icon builder resolves the same per-command SVG slug the
        // WPF host uses; internal ids like "home.underline" deliberately stay out of the asset lookup.
        var icon = AvaloniaRibbonIcons.Build(
            control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
            size,
            control.Label);
        icon.HorizontalAlignment = h;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    private static Control Chevron(AvaloniaRibbonPalette palette) => Chevron(new Thickness(1, 0, 1, 0), palette);

    private static Control Chevron(Thickness margin, AvaloniaRibbonPalette palette)
    {
        var path = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,2 L6,6 L10,2"),
            Stroke = palette.TabTextBrush,
            StrokeThickness = 1.45,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Stretch = Stretch.None,
            IsHitTestVisible = false,
        };

        return new Viewbox
        {
            Width = 10,
            Height = 8,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin,
            Child = path,
            IsHitTestVisible = false,
        };
    }

    private static ContentControl NewButtonLike(RibbonControl control, AvaloniaRibbonPalette palette)
    {
        if (control is RibbonToggleButton or RibbonCheckBox)
        {
            var toggle = new ToggleButton
            {
                Tag = control.CommandId.Value,
                Template = RibbonToggleButtonTemplate,
            };
            toggle.PropertyChanged += (_, args) =>
            {
                if (args.Property == ToggleButton.IsCheckedProperty)
                    ApplyToggleCheckedChrome(toggle, palette);
            };
            ApplyToggleCheckedChrome(toggle, palette);
            return toggle;
        }

        return new Button { Tag = control.CommandId.Value };
    }

    private static void ApplyToggleCheckedChrome(ToggleButton toggle, AvaloniaRibbonPalette palette)
    {
        if (toggle.IsChecked == true)
        {
            toggle.Background = palette.CheckedBrush;
            toggle.BorderBrush = palette.AccentBrush;
            toggle.BorderThickness = new Thickness(1);
            return;
        }

        toggle.ClearValue(TemplatedControl.BackgroundProperty);
        toggle.ClearValue(TemplatedControl.BorderBrushProperty);
        toggle.BorderThickness = new Thickness(1);
    }

    /// <summary>
    /// Attaches the menu flyout (for dropdown/split buttons), click routing, and enablement.
    /// </summary>
    private static void WireControl(
        ContentControl element,
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette,
        bool attachMenu = true)
    {
        if (attachMenu && BuildMenu(control) is { } menu && element is Button menuButton)
        {
            var flyout = menu.BuildFlyout(registry, afterExecute);
            ConfigureMenuFlyout(flyout, menuButton, palette, RibbonPopupInteractionContract.CollapsedGroup);
            menuButton.Flyout = flyout;
        }
        else if (element is Button button)
        {
            button.Click += (_, _) => Execute(control.CommandId, registry, afterExecute);
        }
        else if (element is ToggleButton toggle)
        {
            toggle.Click += (_, _) => Execute(control.CommandId, registry, afterExecute);
        }

        if (attachMenu)
            ApplyControlEnablement(element, control, registry, palette);
        else
            ApplyStateAndEnablement(element, control.CommandId, registry, palette);
    }

    private static void ApplyEnablement(Control element, RibbonControl control, IRibbonCommandRegistry? registry, AvaloniaRibbonPalette? palette = null)
        => ApplyStateAndEnablement(element, control.CommandId, registry, palette);

    private static void ApplyStateAndEnablement(Control element, RibbonCommandId commandId, IRibbonCommandRegistry? registry, AvaloniaRibbonPalette? palette = null)
    {
        // No registry => preview/design mode: leave controls enabled so the layout renders fully.
        // With a registry, an unregistered command id renders disabled (never throws).
        if (registry is null)
            return;
        if (string.IsNullOrEmpty(commandId.Value))
            return;
        if (!registry.TryGet(commandId, out var cmd))
        {
            element.IsEnabled = false;
            return;
        }
        // Stateful commands expose IsEnabled in their state (e.g. Draw-tab commands disabled by
        // default when no stylus/pen context is active). Respect that at build time.
        if (cmd is IRibbonStatefulCommand stateful)
        {
            ApplyRibbonCommandState(element, stateful.GetState(), palette ?? ResolvePalette());
            return;
        }

        element.IsEnabled = true;
    }

    private static void ApplyControlEnablement(
        Control element,
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        AvaloniaRibbonPalette palette)
    {
        if (registry is null || string.IsNullOrEmpty(control.CommandId.Value))
            return;

        var menu = BuildMenu(control);
        var commandIsLive = menu is { Items.Count: > 0 } || registry.TryGet(control.CommandId, out _);
        if (!commandIsLive)
        {
            element.IsEnabled = false;
            return;
        }

        if (registry.TryGet(control.CommandId, out var command) && command is IRibbonStatefulCommand stateful)
        {
            ApplyRibbonCommandState(element, stateful.GetState(), palette);
            return;
        }

        element.IsEnabled = true;
    }

    private static void ApplyRibbonCommandState(Control element, RibbonCommandState state, AvaloniaRibbonPalette palette)
    {
        element.IsEnabled = state.IsEnabled;
        switch (element)
        {
            case CheckBox checkBox:
                SetCheckBoxStateWithoutExecuting(checkBox, state.IsChecked);
                break;
            case ToggleButton toggle:
                toggle.IsChecked = state.IsChecked;
                ApplyToggleCheckedChrome(toggle, palette);
                break;
            case ComboBox combo when state.Value is { } value:
                SetComboValueWithoutExecuting(combo, value);
                break;
        }
    }

    private static string? ResolveComboValue(ComboBox box)
    {
        var value = box.SelectedItem?.ToString();
        return string.IsNullOrWhiteSpace(value) ? box.Text : value;
    }

    private static void ClearPendingComboSelection(ComboExecutionState executionState)
    {
        executionState.HasPendingSelectionCommit = false;
        executionState.PendingSelectionValue = null;
    }

    private static void SetComboValueWithoutExecuting(ComboBox combo, string value)
    {
        var executionState = ComboExecutionStates.GetOrCreateValue(combo);
        executionState.IsSynchronizing = true;
        try
        {
            var matchingIndex = combo.Items.ToList().FindIndex(item =>
                string.Equals(item?.ToString(), value, StringComparison.Ordinal));
            if (combo.SelectedIndex != matchingIndex)
                combo.SelectedIndex = matchingIndex;
            if (!string.Equals(combo.Text, value, StringComparison.Ordinal))
                combo.Text = value;
        }
        finally
        {
            executionState.IsSynchronizing = false;
            ClearPendingComboSelection(executionState);
        }
    }

    private static void SetCheckBoxStateWithoutExecuting(CheckBox checkBox, bool isChecked)
    {
        if (checkBox.IsChecked == isChecked)
            return;

        var executionState = CheckBoxExecutionStates.GetOrCreateValue(checkBox);
        executionState.IsSynchronizing = true;
        try
        {
            checkBox.IsChecked = isChecked;
        }
        finally
        {
            executionState.IsSynchronizing = false;
        }
    }

    private static RibbonMenu? BuildMenu(RibbonControl control) => control switch
    {
        RibbonSplitButton split => split.Menu,
        RibbonDropdown dropdown => dropdown.Menu,
        _ => null,
    };

    private static MenuFlyout BuildFlyout(this RibbonMenu menu, IRibbonCommandRegistry? registry, Action? afterExecute)
    {
        var flyout = new MenuFlyout();
        foreach (var item in menu.Items)
            flyout.Items.Add(BuildMenuItem(item, registry, afterExecute));
        return flyout;
    }

    private static Control BuildMenuItem(RibbonMenuItem item, IRibbonCommandRegistry? registry, Action? afterExecute)
    {
        if (item.Kind == RibbonMenuItemKind.Separator)
            return new Separator();

        var menuItem = new MenuItem
        {
            Header = item.Header,
            InputGesture = null,
            Tag = item.CommandId?.Value,
            IsEnabled = item.IsEnabled,
        };
        if (item.IsChecked is { } isChecked)
        {
            menuItem.ToggleType = MenuItemToggleType.CheckBox;
            menuItem.IsChecked = isChecked;
        }
        RegisterMenuKeyTip(menuItem, item.KeyTip);

        if (!string.IsNullOrEmpty(item.InputGesture))
            menuItem.InputGesture = TryParseGesture(item.InputGesture);

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                menuItem.Items.Add(BuildMenuItem(child, registry, afterExecute));
        }
        else if (item.CommandId is { } commandId)
        {
            menuItem.Click += (_, _) => Execute(commandId, registry, afterExecute);
            if (item.IsEnabled)
                ApplyEnablement(menuItem, commandId, registry);
        }

        return menuItem;
    }

    private static global::Avalonia.Input.KeyGesture? TryParseGesture(string gesture)
    {
        try
        {
            return global::Avalonia.Input.KeyGesture.Parse(gesture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ApplyEnablement(MenuItem item, RibbonCommandId commandId, IRibbonCommandRegistry? registry)
    {
        if (registry is null || string.IsNullOrEmpty(commandId.Value))
            return;
        item.IsEnabled = registry.TryGet(commandId, out var cmd)
            && (cmd is not IRibbonStatefulCommand stateful || stateful.GetState().IsEnabled);
    }

    private static void Execute(RibbonCommandId commandId, IRibbonCommandRegistry? registry, Action? afterExecute)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
        {
            command.Execute(RibbonCommandContext.Empty);
            afterExecute?.Invoke();
        }
    }

    private static void ExecuteWithValue(
        RibbonCommandId commandId,
        IRibbonCommandRegistry? registry,
        string? value,
        Action? afterExecute)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
        {
            command.Execute(RibbonCommandContext.ForSelectedValue(value));
            afterExecute?.Invoke();
        }
    }

    private static bool HasMenu(RibbonControl control) =>
        control is RibbonSplitButton or RibbonDropdown;

    private static MenuFlyout BuildCollapsedGroupFlyout(
        RibbonGroup group,
        IRibbonCommandRegistry? registry,
        Action? afterExecute,
        AvaloniaRibbonPalette palette)
    {
        var flyout = new MenuFlyout();
        foreach (var control in RibbonCollapsedGroupPresentationPlanner.GetOverflowControls(group))
        {
            switch (control)
            {
                case RibbonSeparator:
                    flyout.Items.Add(new Separator());
                    break;
                case RibbonSplitButton split:
                    AddCollapsedSplitButtonItems(flyout, split, registry, afterExecute);
                    break;
                default:
                    flyout.Items.Add(BuildCollapsedGroupMenuItem(control, registry, afterExecute));
                    break;
            }
        }

        return flyout;
    }

    private static void AddCollapsedSplitButtonItems(
        MenuFlyout flyout,
        RibbonSplitButton split,
        IRibbonCommandRegistry? registry,
        Action? afterExecute)
    {
        // A collapsed ribbon group cannot preserve the two independent hit targets of an
        // expanded split button. Keep the primary action as an invokable leaf, then append any
        // additional menu actions. Skipping the menu's duplicate primary command turns Outline
        // into Group, Ungroup, Clear Outline instead of two submenu parents that cannot execute.
        flyout.Items.Add(BuildCollapsedGroupPrimaryAction(split, registry, afterExecute));

        foreach (var menuItem in split.Menu.Items)
        {
            if (menuItem.Kind != RibbonMenuItemKind.Separator &&
                menuItem.CommandId is { } commandId &&
                (commandId == split.CommandId ||
                 string.Equals(menuItem.Header, split.Label, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            flyout.Items.Add(BuildMenuItem(menuItem, registry, afterExecute));
        }
    }

    private static MenuItem BuildCollapsedGroupPrimaryAction(
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute)
    {
        var item = new MenuItem
        {
            Header = control.Label,
            Tag = control.CommandId.Value,
        };
        RegisterMenuKeyTip(item, control.KeyTip);
        item.Click += (_, _) => Execute(control.CommandId, registry, afterExecute);
        ApplyEnablement(item, control.CommandId, registry);
        return item;
    }

    private static Control BuildCollapsedGroupMenuItem(
        RibbonControl control,
        IRibbonCommandRegistry? registry,
        Action? afterExecute)
    {
        var item = new MenuItem
        {
            Header = control.Label,
            Tag = control.CommandId.Value,
        };
        RegisterMenuKeyTip(item, control.KeyTip);

        if (BuildMenu(control) is { } menu)
        {
            foreach (var menuItem in menu.Items)
                item.Items.Add(BuildMenuItem(menuItem, registry, afterExecute));
        }
        else
        {
            item.Click += (_, _) => Execute(control.CommandId, registry, afterExecute);
        }

        if (BuildMenu(control) is { Items.Count: > 0 })
            ApplyControlEnablement(item, control, registry, ResolvePalette());
        else
            ApplyEnablement(item, control.CommandId, registry);
        return item;
    }

    // WPF BuildInlineDivider: a 1px theme-owned rule, stretched, margin 3.
    private static Control BuildInlineDivider(AvaloniaRibbonPalette palette) => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(3),
        Fill = palette.InlineDividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    // WPF RibbonGroupDivider: a 1px theme divider between groups, margin 2,5,3,18.
    private static Control BuildGroupDivider(AvaloniaRibbonPalette palette) => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(2, 5, 3, 18),
        Fill = palette.DividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private sealed class AvaloniaRibbonGroupHost : ContentControl
    {
        public const double CollapsedWidth = 64;

        private readonly RibbonGroup _group;
        private readonly Control _full;
        private readonly IRibbonCommandRegistry? _registry;
        private readonly Action? _afterExecute;
        private readonly AvaloniaRibbonPalette _palette;
        private readonly string _collapsedKeyTip;
        private Control? _collapsedButton;
        private bool _collapsed;

        public AvaloniaRibbonGroupHost(
            RibbonGroup group,
            Control full,
            IRibbonCommandRegistry? registry,
            Action? afterExecute,
            AvaloniaRibbonPalette palette,
            string collapsedKeyTip)
        {
            _group = group;
            _full = full;
            _registry = registry;
            _afterExecute = afterExecute;
            _palette = palette;
            _collapsedKeyTip = collapsedKeyTip;
            Priority = group.Priority;
            VerticalAlignment = VerticalAlignment.Stretch;
            Content = full;
        }

        public int Priority { get; }
        public string GroupId => _group.Id;
        public double FullWidth { get; set; }

        public bool Collapsed
        {
            get => _collapsed;
            set
            {
                if (_collapsed == value)
                    return;

                _collapsed = value;
                Content = value ? (_collapsedButton ??= BuildCollapsedButton()) : _full;
            }
        }

        private Control BuildCollapsedButton()
        {
            var representativeIcon = RibbonCollapsedGroupPresentationPlanner.GetRepresentativeIcon(_group);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(AvaloniaRibbonIcons.Build(
                representativeIcon.Icon.Kind,
                34,
                representativeIcon.CommandName ?? _group.Header));
            stack.Children.Add(new TextBlock
            {
                Text = _group.Header,
                FontSize = 11,
                Foreground = _palette.GroupLabelBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.WrapWithOverflow,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 56,
                Margin = new Thickness(0, 2, 0, 0),
            });
            stack.Children.Add(Chevron(new Thickness(0, 2, 0, 0), _palette));

            var button = new Button
            {
                Width = 58,
                Height = 76,
                Padding = new Thickness(2),
                // Center the collapsed-group icon/label cluster within the button so the icon sits in
                // the middle of the row (Windows parity) rather than top-pinned.
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = stack,
                Tag = $"collapsed:{_group.Id}",
            };
            var flyout = BuildCollapsedGroupFlyout(_group, _registry, _afterExecute, _palette);
            ConfigureCollapsedGroupFlyout(flyout, button, _palette);
            button.Flyout = flyout;
            SetKeyTip(button, _collapsedKeyTip);
            button.Classes.Add("freex-ribbon-collapsed-group");
            return button;
        }
    }

    private static void ConfigureCollapsedGroupFlyout(
        MenuFlyout flyout,
        Button anchor,
        AvaloniaRibbonPalette palette)
        => ConfigureMenuFlyout(flyout, anchor, palette, RibbonPopupInteractionContract.CollapsedGroup);

    private static void ConfigureMenuFlyout(
        MenuFlyout flyout,
        Control anchor,
        AvaloniaRibbonPalette palette,
        RibbonPopupInteractionContract contract)
    {
        var chrome = RibbonVisualMetrics.PopupChrome;
        flyout.Placement = contract.Placement switch
        {
            RibbonPopupPlacement.BelowAnchor => PlacementMode.Bottom,
            RibbonPopupPlacement.AboveAnchor => PlacementMode.Top,
            _ => PlacementMode.Bottom,
        };
        flyout.HorizontalOffset = 0;
        flyout.VerticalOffset = contract.AnchorGap;
        if (contract.RepositionAtScreenEdge)
        {
            flyout.PlacementConstraintAdjustment =
                PopupPositionerConstraintAdjustment.SlideX |
                PopupPositionerConstraintAdjustment.FlipY;
        }
        flyout.FlyoutPresenterClasses.Add(PopupChromeClass);
        if (Application.Current is { } application)
        {
            PopupChromeStyleApplications.GetValue(application, _ =>
            {
                application.Styles.Add(CreatePopupPresenterStyle(chrome, palette));
                application.Styles.Add(CreatePopupPresenterBorderStyle(chrome, palette));
                application.Styles.Add(CreateSubmenuPopupPlacementStyle(contract));
                return new object();
            });
        }
        var topLevelItems = flyout.Items.OfType<MenuItem>().ToArray();
        foreach (var item in topLevelItems)
            ConfigureMenuItem(item, parent: null, topLevelItems, flyout, contract, chrome);
        flyout.Opened += (_, _) =>
        {
            if (!contract.FocusFirstEnabledItemOnOpen)
                return;

            var items = flyout.Items.OfType<MenuItem>().ToArray();
            var states = items
                .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
                .ToArray();
            var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
            if (index >= 0)
                items[index].Focus(NavigationMethod.Tab);
        };
        flyout.Closed += (_, _) =>
        {
            if (contract.RestoreFocusToAnchorOnClose)
                anchor.Focus(NavigationMethod.Tab);
        };

    }

    private static void ConfigureMenuItem(
        MenuItem item,
        MenuItem? parent,
        IReadOnlyList<MenuItem> siblings,
        MenuFlyout flyout,
        RibbonPopupInteractionContract contract,
        RibbonPopupChromeMetrics chrome)
    {
        item.MinHeight = parent is null ? chrome.ItemMinHeight : chrome.Submenu.ItemMinHeight;
        item.Padding = ToThickness(parent is null ? chrome.ItemPadding : chrome.Submenu.ItemPadding);

        var children = item.Items.OfType<MenuItem>().ToArray();
        if (children.Length > 0)
            item.Classes.Add(SubmenuPlacementClass);
        foreach (var child in children)
            ConfigureMenuItem(child, item, children, flyout, contract, chrome);

        item.AddHandler(
            InputElement.KeyDownEvent,
            (_, args) => HandleMenuItemKey(flyout, contract, item, parent, siblings, args),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private static Style CreatePopupPresenterStyle(
        RibbonPopupChromeMetrics chrome,
        AvaloniaRibbonPalette palette) => new(x => x.OfType<MenuFlyoutPresenter>().Class(PopupChromeClass))
    {
        Setters =
        {
            new Setter(TemplatedControl.BackgroundProperty, palette.SurfaceBrush),
            new Setter(TemplatedControl.BorderBrushProperty, palette.DividerBrush),
            new Setter(TemplatedControl.MinWidthProperty, chrome.MinWidth),
            new Setter(TemplatedControl.MaxWidthProperty, chrome.MaxWidth),
            new Setter(TemplatedControl.PaddingProperty, ToThickness(chrome.PopupPadding)),
            new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(chrome.BorderThickness)),
        },
    };

    private static Style CreatePopupPresenterBorderStyle(
        RibbonPopupChromeMetrics chrome,
        AvaloniaRibbonPalette palette) => new(x => x.OfType<MenuFlyoutPresenter>().Class(PopupChromeClass).Template().OfType<Border>())
    {
        Setters =
        {
            new Setter(Border.BackgroundProperty, palette.SurfaceBrush),
            new Setter(Border.BorderBrushProperty, palette.DividerBrush),
            new Setter(Border.BorderThicknessProperty, new Thickness(chrome.BorderThickness)),
            new Setter(Border.CornerRadiusProperty, new CornerRadius(chrome.CornerRadius)),
            new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = chrome.ShadowDepth,
                Blur = chrome.ShadowBlurRadius,
                Color = Color.FromArgb((byte)(chrome.ShadowOpacity * 255), 0, 0, 0),
            })),
        },
    };

    private static Style CreateSubmenuPopupPlacementStyle(
        RibbonPopupInteractionContract contract) => new(x => x.OfType<MenuItem>().Class(SubmenuPlacementClass).Template().OfType<Popup>())
    {
        Setters =
        {
            new Setter(Popup.PlacementProperty, PlacementMode.Right),
            new Setter(
                Popup.PlacementConstraintAdjustmentProperty,
                contract.Submenu.RepositionAtScreenEdge
                    ? PopupPositionerConstraintAdjustment.FlipX | PopupPositionerConstraintAdjustment.SlideY
                    : PopupPositionerConstraintAdjustment.None),
            new Setter(Popup.HorizontalOffsetProperty, contract.Submenu.AnchorGap),
        },
    };

    private static Thickness ToThickness(RibbonPopupInsets insets) =>
        new(insets.Left, insets.Top, insets.Right, insets.Bottom);


    private static void HandleMenuItemKey(
        MenuFlyout flyout,
        RibbonPopupInteractionContract contract,
        MenuItem currentItem,
        MenuItem? parent,
        IReadOnlyList<MenuItem> siblings,
        KeyEventArgs args)
    {
        if (args.Handled || !ReferenceEquals(args.Source, currentItem))
            return;

        var children = currentItem.Items.OfType<MenuItem>().ToArray();
        if (args.Key == Key.Right &&
            RibbonPopupInteractionPlanner.PlanNavigation(
                RibbonPopupNavigationKey.Right, children.Length > 0, contract) == RibbonPopupNavigation.OpenSubmenu)
        {
            currentItem.IsSubMenuOpen = true;
            FocusFirstEnabledChild(currentItem, children, contract);
            args.Handled = true;
            return;
        }

        var dismissal = args.Key switch
        {
            Key.Escape => RibbonPopupInteractionPlanner.PlanDismissal(
                RibbonPopupDismissKey.Escape, parent is not null, contract),
            Key.Left => RibbonPopupInteractionPlanner.PlanDismissal(
                RibbonPopupDismissKey.Left, parent is not null, contract),
            _ => RibbonPopupDismissal.None,
        };
        if (dismissal == RibbonPopupDismissal.CloseSubmenu && parent is not null)
        {
            parent.IsSubMenuOpen = false;
            if (contract.Submenu.RestoreFocusToParentOnClose)
            {
                parent.Focusable = true;
                parent.IsSelected = true;
                TopLevel.GetTopLevel(parent)?.FocusManager?.Focus(parent, NavigationMethod.Tab);
            }
            args.Handled = true;
            return;
        }

        if (dismissal == RibbonPopupDismissal.ClosePopup)
        {
            flyout.Hide();
            args.Handled = true;
            return;
        }

        if (parent is not null && !contract.Submenu.TraverseEnabledItems ||
            parent is null && !contract.TraverseEnabledItems ||
            args.Key is not (Key.Up or Key.Down or Key.Home or Key.End))
            return;

        var currentIndex = -1;
        for (var siblingIndex = 0; siblingIndex < siblings.Count; siblingIndex++)
        {
            if (ReferenceEquals(siblings[siblingIndex], currentItem))
            {
                currentIndex = siblingIndex;
                break;
            }
        }
        if (currentIndex < 0)
            return;

        var states = siblings
            .Select(item => new RibbonPopupFocusItem(item.Focusable, item.IsEnabled))
            .ToArray();
        var targetIndex = args.Key switch
        {
            Key.Home => RibbonPopupInteractionPlanner.FindFirstFocusableItem(states),
            Key.End => RibbonPopupInteractionPlanner.FindLastFocusableItem(states),
            Key.Up => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, -1),
            Key.Down => RibbonPopupInteractionPlanner.FindAdjacentFocusableItem(states, currentIndex, 1),
            _ => -1,
        };
        if (targetIndex >= 0)
        {
            siblings[targetIndex].Focus(NavigationMethod.Directional);
            args.Handled = true;
        }
    }

    private static void FocusFirstEnabledChild(
        MenuItem parent,
        IReadOnlyList<MenuItem> children,
        RibbonPopupInteractionContract contract)
    {
        if (!contract.Submenu.FocusFirstEnabledItemOnOpen)
            return;

        var states = children
            .Select(child => new RibbonPopupFocusItem(child.Focusable, child.IsEnabled))
            .ToArray();
        var index = RibbonPopupInteractionPlanner.FindFirstFocusableItem(states);
        if (index < 0)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (parent.IsSubMenuOpen)
                    TopLevel.GetTopLevel(parent)?.FocusManager?.Focus(children[index], NavigationMethod.Directional);
            },
            DispatcherPriority.Input);
    }

    private sealed class AvaloniaRibbonAdaptivePanel : Panel
    {
        private const double GroupSpacing = 6;

        protected override Size MeasureOverride(Size availableSize)
        {
            var children = Children.ToList();
            var hosts = children.OfType<AvaloniaRibbonGroupHost>().ToList();
            var infinite = new Size(double.PositiveInfinity, availableSize.Height);
            var spacing = GroupSpacing * Math.Max(0, children.Count - 1);

            foreach (var child in children)
                child.Measure(infinite);

            foreach (var host in hosts)
            {
                if (!host.Collapsed && host.DesiredSize.Width > host.FullWidth)
                    host.FullWidth = host.DesiredSize.Width;
                else if (host.FullWidth <= 0)
                    host.FullWidth = host.DesiredSize.Width;
            }

            var nonHostWidth = children
                .Where(child => child is not AvaloniaRibbonGroupHost)
                .Sum(child => child.DesiredSize.Width);
            var available = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
            var decisions = RibbonAdaptiveCollapsePolicy.Plan(
                available,
                hosts
                    .Select(host => new RibbonAdaptiveCollapseGroup(
                        host.GroupId,
                        host.FullWidth,
                        AvaloniaRibbonGroupHost.CollapsedWidth,
                        host.Priority))
                    .ToList(),
                fixedChromeWidth: nonHostWidth + spacing);

            for (var index = 0; index < hosts.Count; index++)
                hosts[index].Collapsed = decisions[index].IsCollapsed;

            foreach (var child in children)
                child.Measure(infinite);

            var width = children.Sum(child => child.DesiredSize.Width) + spacing;
            var height = children.Count > 0 ? children.Max(child => child.DesiredSize.Height) : 0;
            return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, available), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            foreach (var child in Children)
            {
                var width = child.DesiredSize.Width;
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width + GroupSpacing;
            }

            return finalSize;
        }
    }
}
