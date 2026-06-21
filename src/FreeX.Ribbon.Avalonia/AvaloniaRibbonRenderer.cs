using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;

namespace FreeX.Ribbon.Avalonia;

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
    private const string SelectedTabUnderlineTag = "FreeX.SelectedTabUnderline";
    private const double SmallRowHeight = 26;
    private const double TabHeaderHeight = 28;
    private const double RibbonCheckBoxHeight = 18;
    private const double RibbonCheckGlyphSize = 11;
    private const double LargeIconSize = 32;
    private const double MediumIconSize = 22;
    private const double SmallIconSize = 22;
    private const int MaxRowsPerColumn = 3;
    private static readonly IReadOnlySet<string> StaticDrawUnavailableCommandIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "Crop Picture",
        "Shape Gradient",
        "Shape Effects",
    };

    // Ribbon palette — matched 1:1 to the WPF resources so the Avalonia ribbon visually replicates
    // Windows. Exposed internally so the theme is unit-testable.
    //   SurfaceColor    = FreeXRibbonSurfaceBrush      (#FFFFFF) — ThemeResources.xaml:16
    //   AccentColor     = FreeXAccentBrush             (#0F6D8C) — ThemeResources.xaml:3
    //   DividerColor    = FreeXBorderBrush             (#DADCE0) — ThemeResources.xaml:20 (group divider + label rule)
    //   InlineDivider   = hardcoded #CCCCCC                       — RibbonWpfRenderer.BuildInlineDivider
    //   GroupLabelColor = FreeXMutedTextBrush          (#5F6368) — ThemeResources.xaml:14 (GroupLbl)
    //   HoverColor      = FreeXRibbonButtonHoverBrush  (#BEE6FD) — ThemeResources.xaml:12
    //   HoverBorder     = FreeXBorderStrongBrush       (#C8CCD0) — ThemeResources.xaml:21
    //   CheckedColor    = FreeXAccentPressedBrush      (#CCEAF2) — ThemeResources.xaml:11 (toggle IsChecked fill)
    //   TabHoverColor   = FreeXAccentSoftBrush         (#E6F6FA) — ThemeResources.xaml:10
    //   TabStripColor   = FreeXChromeSurfaceBrush      (#F7F8F8) — the light-gray surround behind the tabs
    //                     (FreeXChromeSurfaceBrush analog) so the white selected tab pops out of a gray strip.
    //   TabTextColor    = FreeXTextBrush               (#1F1F1F) — ThemeResources.xaml:13 (near-black tab labels)
    internal static readonly Color SurfaceColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static readonly Color AccentColor = Color.FromRgb(0x0F, 0x6D, 0x8C);
    internal static readonly Color DividerColor = Color.FromRgb(0xDA, 0xDC, 0xE0);
    internal static readonly Color InlineDividerColor = Color.FromRgb(0xCC, 0xCC, 0xCC);
    internal static readonly Color GroupLabelColor = Color.FromRgb(0x5F, 0x63, 0x68);
    internal static readonly Color HoverColor = Color.FromRgb(0xBE, 0xE6, 0xFD);
    internal static readonly Color HoverBorderColor = Color.FromRgb(0xC8, 0xCC, 0xD0);
    internal static readonly Color CheckedColor = Color.FromRgb(0xE6, 0xF6, 0xFA);
    internal static readonly Color TabHoverColor = Color.FromRgb(0xE6, 0xF6, 0xFA);
    internal static readonly Color TabStripColor = Color.FromRgb(0xF7, 0xF8, 0xF8);
    internal static readonly Color TabTextColor = Color.FromRgb(0x1F, 0x1F, 0x1F);

    private static readonly IBrush SurfaceBrush = new SolidColorBrush(SurfaceColor);
    private static readonly IBrush AccentBrush = new SolidColorBrush(AccentColor);
    private static readonly IBrush DividerBrush = new SolidColorBrush(DividerColor);
    private static readonly IBrush InlineDividerBrush = new SolidColorBrush(InlineDividerColor);
    private static readonly IBrush GroupLabelBrush = new SolidColorBrush(GroupLabelColor);
    private static readonly IBrush HoverBrush = new SolidColorBrush(HoverColor);
    private static readonly IBrush HoverBorderBrush = new SolidColorBrush(HoverBorderColor);
    private static readonly IBrush CheckedBrush = new SolidColorBrush(CheckedColor);
    private static readonly IBrush TabHoverBrush = new SolidColorBrush(TabHoverColor);
    private static readonly IBrush TabStripBrush = new SolidColorBrush(TabStripColor);
    private static readonly IBrush TabTextBrush = new SolidColorBrush(TabTextColor);
    private static readonly FontFamily RibbonFontFamily =
        new("Arial Narrow, Aptos Narrow, Liberation Sans Narrow, Nimbus Sans Narrow, DejaVu Sans Condensed, Arial, Liberation Sans, Noto Sans, DejaVu Sans, Helvetica, sans-serif");
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
    private static readonly FuncControlTemplate<CheckBox> RibbonCheckBoxTemplate = new((checkBox, _) =>
    {
        var checkMark = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M2,5.5 L4.4,8 L9,2.7"),
            Stroke = AccentBrush,
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
            Background = Brushes.White,
            BorderBrush = HoverBorderBrush,
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
    public static void SyncToggleStates(Control ribbon, IRibbonCommandRegistry? registry)
    {
        if (registry is null)
            return;
        foreach (var toggle in ribbon.GetVisualDescendants().OfType<ToggleButton>())
        {
            if (toggle.Tag is string id && !string.IsNullOrEmpty(id)
                && registry.TryGet(new RibbonCommandId(id), out var cmd)
                && cmd is IRibbonStatefulCommand stateful)
            {
                ApplyRibbonCommandState(toggle, stateful.GetState());
            }
        }
    }

    /// <summary>Builds the content panel for one tab (the body shown under the tab header).</summary>
    public static Control BuildTabContent(RibbonTab tab, IRibbonCommandRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(tab);

        var panel = new AvaloniaRibbonAdaptivePanel
        {
            MinHeight = 82,
        };

        var first = true;
        foreach (var group in tab.Groups)
        {
            if (!first)
                panel.Children.Add(BuildGroupDivider());
            panel.Children.Add(new AvaloniaRibbonGroupHost(group, BuildGroup(group, registry), registry));
            first = false;
        }

        // WPF: Border { Background=FreeXRibbonSurfaceBrush (white); Padding 0,4,0,0 } — no accent rule.
        if (string.Equals(tab.Id, "DrawTab", StringComparison.Ordinal))
            DisableStaticDrawUnavailableCommands(panel);

        return new Border
        {
            Background = SurfaceBrush,
            Padding = new Thickness(0, 2, 0, 0),
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

    private static Control BuildTabHeader(string header)
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                new RowDefinition { Height = new GridLength(3) },
            },
            Height = TabHeaderHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 42,
        };
        AddHeaderChild(grid, new TextBlock
        {
            Text = header,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            Foreground = TabTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5, 0),
        }, 0);
        AddHeaderChild(grid, new Border
        {
            Tag = SelectedTabUnderlineTag,
            Height = 3,
            Background = AccentBrush,
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
    private static TabItem BuildTabItem(RibbonTab tab, IRibbonCommandRegistry? registry) => new()
    {
        Header = BuildTabHeader(tab.Header),
        Content = BuildTabContent(tab, registry),
        Tag = tab.Id,
    };

    private static TabItem BuildFileTabItem() => new()
    {
        Header = BuildTabHeader("File"),
        Content = new Border
        {
            Background = SurfaceBrush,
            MinHeight = 82,
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
        IRibbonContextSource? contextSource = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // WPF: white ribbon surface; no extra TabControl bottom border — the selected tab's 3px accent
        // underline is the only visual divider between the tab strip and the content area below.
        // Avalonia Fluent stacks the 1px control border and the 3px tab accent as two separate visible
        // lines; removing the TabControl border leaves just the single accent underline, matching WPF.
        var tabControl = new TabControl
        {
            Background = SurfaceBrush,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ApplyRibbonTheme(tabControl);
        tabControl.Items.Add(BuildFileTabItem());

        var initialTabs = contextSource is null
            ? (IReadOnlyList<RibbonTab>)definition.VisibleTabs.ToArray()
            : ResolveTabStripTabs(definition, contextSource.Current);

        foreach (var tab in initialTabs)
            tabControl.Items.Add(BuildTabItem(tab, registry));

        if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = tabControl.Items.Count > 1 ? 1 : 0;
        UpdateTabHeaderSelectionStates(tabControl);
        tabControl.SelectionChanged += (_, _) => UpdateTabHeaderSelectionStates(tabControl);
        if (contextSource is not null)
            contextSource.ContextChanged += (_, _) => SyncContextualTabs(tabControl, definition, registry, contextSource);

        return tabControl;
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
        IRibbonContextSource contextSource)
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

        // Insert missing tabs at their resolved (declaration-order) index.
        for (var i = 0; i < desired.Count; i++)
        {
            var tab = desired[i];
            var existingIndex = IndexOfTab(tabControl, tab.Id);
            if (existingIndex < 0)
                tabControl.Items.Insert(Math.Min(i + 1, tabControl.Items.Count), BuildTabItem(tab, registry));
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
    internal static void ApplyRibbonTheme(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        // ── Tab headers (gray strip, near-black labels; selected = white body + accent underline;
        // hover = soft accent tint). Matches the WPF TabItem ControlTemplate (MainWindowResources.xaml:
        // transparent template Border with BorderThickness 0,0,0,3; IsSelected -> accent border + white
        // body; IsMouseOver -> FreeXAccentSoftBrush). Foreground is the near-black FreeXTextBrush. ──
        var tabBase = new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, TabStripBrush),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.FontSizeProperty, 12d),
                new Setter(TemplatedControl.FontFamilyProperty, RibbonFontFamily),
                new Setter(TemplatedControl.TemplateProperty, RibbonTabItemTemplate),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
                // Avalonia Fluent default tab height is ~48px vs WPF's compact header row; constrain it.
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, TabHeaderHeight),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0)),
                new Setter(InputElement.CursorProperty, new Cursor(StandardCursorType.Hand)),
            },
        };

        var tabHover = new Style(x => x.OfType<TabItem>().Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, TabHoverBrush),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
            },
        };

        var tabSelected = new Style(x => x.OfType<TabItem>().Class(":selected"))
        {
            Setters =
            {
                // WPF selected tab: white body + near-black label. The underline is drawn inside
                // BuildTabHeader so Avalonia Fluent cannot stack a second selected line under it.
                new Setter(TemplatedControl.BackgroundProperty, SurfaceBrush),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
                new Setter(TemplatedControl.BorderBrushProperty, AccentBrush),
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
                new Setter(TemplatedControl.BackgroundProperty, SurfaceBrush),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
            },
        };

        // Avalonia Fluent theme may override the TabItem Foreground via internal pseudo-class triggers;
        // targeting the rendered TextBlock directly wins over any theme-level override.
        var tabTextForeground = new Style(x => x.OfType<TabItem>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.ForegroundProperty, TabTextBrush),
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
                new Setter(TemplatedControl.BackgroundProperty, HoverBrush),
                new Setter(TemplatedControl.BorderBrushProperty, HoverBorderBrush),
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
                new Setter(TemplatedControl.BackgroundProperty, HoverBrush),
                new Setter(TemplatedControl.BorderBrushProperty, HoverBorderBrush),
            },
        };
        var toggleChecked = new Style(x => x.OfType<ToggleButton>().Class(":checked"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, CheckedBrush),
                new Setter(TemplatedControl.BorderBrushProperty, AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        };
        var toggleCheckedHover = new Style(x => x.OfType<ToggleButton>().Class(":checked").Class(":pointerover"))
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, CheckedBrush),
                new Setter(TemplatedControl.BorderBrushProperty, AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
            },
        };
        var toggleCheckedTemplateBorder = new Style(x => x.OfType<ToggleButton>().Class(":checked").Template().OfType<Border>())
        {
            Setters =
            {
                new Setter(Border.BackgroundProperty, CheckedBrush),
                new Setter(Border.BorderBrushProperty, AccentBrush),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)),
            },
        };

        // ComboBox: Avalonia Fluent default height ~34px vs WPF ~26px — constrain to match.
        var comboBase = new Style(x => x.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(Layoutable.MinHeightProperty, SmallRowHeight),
                new Setter(Layoutable.HeightProperty, SmallRowHeight),
                new Setter(Layoutable.MaxHeightProperty, SmallRowHeight),
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
                new Setter(TemplatedControl.TemplateProperty, RibbonCheckBoxTemplate),
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
    }

    private static Control BuildGroup(RibbonGroup group, IRibbonCommandRegistry? registry)
    {
        var grid = new Grid
        {
            Tag = group.Id,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(new GridLength(17)),
            },
        };

        var content = BuildGroupContent(group, registry);
        Grid.SetRow(content, 0);
        grid.Children.Add(content);

        // WPF RibbonGroupLabelBorder: a 1px top rule in FreeXBorderBrush over the centered muted label.
        var labelBorder = new Border
        {
            BorderBrush = DividerBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            MinHeight = 17,
            Child = new TextBlock
            {
                Text = group.Header,
                FontSize = 12,
                FontFamily = RibbonFontFamily,
                Foreground = GroupLabelBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            },
        };
        Grid.SetRow(labelBorder, 1);
        grid.Children.Add(labelBorder);

        return grid;
    }

    private static Control BuildGroupContent(RibbonGroup group, IRibbonCommandRegistry? registry)
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
            lane.Children.Add(BuildLargeControl(controls[index], registry));
            index++;
        }

        var rest = controls.Skip(index).ToList();
        if (rest.Count == 0)
            return lane;

        if (rest.Any(c => c is RibbonRowBreak))
            lane.Children.Add(BuildExplicitRows(rest, registry));
        else
            BuildAutoColumns(rest, lane, registry);

        return lane;
    }

    // Groups that declare RowBreaks lay out as stacked horizontal rows (e.g. Font: combos row, then B/I/U row).
    private static Control BuildExplicitRows(IReadOnlyList<RibbonControl> controls, IRibbonCommandRegistry? registry)
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

            current.Children.Add(BuildInlineControl(control, registry));
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
    private static void BuildAutoColumns(IReadOnlyList<RibbonControl> controls, StackPanel lane, IRibbonCommandRegistry? registry)
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
                    lane.Children.Add(BuildInlineDivider());
                    break;
                case { PreferredLayout: RibbonCommandLayoutKind.Large }:
                    Flush();
                    lane.Children.Add(BuildLargeControl(control, registry));
                    break;
                default:
                    // Keep comboboxes and buttons in separate columns so a group reads like WPF's.
                    var isCombo = control is RibbonComboBox;
                    if (column is not null && columnIsCombo != isCombo)
                        Flush();
                    column ??= NewColumn();
                    columnIsCombo = isCombo;
                    column.Children.Add(BuildInlineControl(control, registry));
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
    private static Control BuildInlineControl(RibbonControl control, IRibbonCommandRegistry? registry) => control switch
    {
        RibbonSeparator => BuildInlineDivider(),
        RibbonComboBox combo => BuildComboControl(combo, registry),
        RibbonCheckBox check => BuildCheckControl(check, registry),
        { PreferredLayout: RibbonCommandLayoutKind.Large } => BuildLargeControl(control, registry),
        { PreferredLayout: RibbonCommandLayoutKind.Small } => BuildIconControl(control, registry),
        _ => BuildMediumControl(control, registry),
    };

    // WPF BuildCheckControl: a real CheckBox carrying the label.
    private static Control BuildCheckControl(RibbonCheckBox check, IRibbonCommandRegistry? registry)
    {
        var box = new CheckBox
        {
            Content = check.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            Height = RibbonCheckBoxHeight,
            MinHeight = RibbonCheckBoxHeight,
            MaxHeight = RibbonCheckBoxHeight,
            Template = RibbonCheckBoxTemplate,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1),
            Tag = check.CommandId.Value,
        };
        ApplyStateAndEnablement(box, check.CommandId, registry);
        box.IsCheckedChanged += (_, _) => Execute(check.CommandId, registry);
        return box;
    }

    // WPF BuildLargeControl: a hero button — big icon (~32px) above a centered (wrapping) caption. For a
    // split/dropdown control, WPF folds a centered chevron into a band BELOW the label (a distinct dropdown
    // affordance) rather than running "▾" into the caption text.
    private static Control BuildLargeControl(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(NewIcon(control, LargeIconSize, HorizontalAlignment.Center));

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

        // Large split/dropdown affordance: a centered chevron on its OWN line under the label, so the
        // hero button reads as visually split (icon + label = primary, the chevron band = open the menu) —
        // matching the WPF hero split-button layout. The flyout is still attached to the whole button by
        // WireControl, so the primary click opens the menu as before; only the visual changes.
        if (HasMenu(control))
        {
            stack.Children.Add(Chevron(new Thickness(0, 1, 0, 0)));
        }

        // WPF RibbonLargeButton: compact hero column, Padding 3,2.
        var button = NewButtonLike(control);
        button.Width = 80;
        button.Height = 76;
        button.Padding = new Thickness(4, 2);
        ((ContentControl)button).Content = stack;
        WireControl(button, control, registry);
        return button;
    }

    // WPF BuildMediumControl: small icon (16px) + label in a horizontal row.
    private static Control BuildMediumControl(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(NewIcon(control, MediumIconSize, HorizontalAlignment.Center));
        content.Children.Add(new TextBlock
        {
            Text = control.Label,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
        });
        if (HasMenu(control))
            content.Children.Add(Chevron());

        // WPF RibbonBtn: Height 22, MinWidth 84, left-aligned content, Padding 4,2.
        var button = NewButtonLike(control);
        button.Height = SmallRowHeight;
        button.MinWidth = 88;
        button.Padding = new Thickness(4, 2);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)button).Content = content;
        WireControl(button, control, registry);
        return button;
    }

    // WPF BuildIconControl: Small layout is ICON-ONLY (~18px) — no label. With a menu, append a chevron.
    private static Control BuildIconControl(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        var hasMenu = HasMenu(control);
        Control content;
        if (hasMenu)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(NewIcon(control, SmallIconSize, HorizontalAlignment.Center));
            stack.Children.Add(Chevron());
            content = stack;
        }
        else
        {
            content = NewIcon(control, SmallIconSize, HorizontalAlignment.Center);
        }

        // WPF RibbonIconButton / RibbonIconToggleButton: icon-centred compact button, wider when a menu chevron is present.
        var button = NewButtonLike(control);
        button.Width = hasMenu ? 42 : 30;
        button.Height = SmallRowHeight;
        button.Padding = new Thickness(1, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ((ContentControl)button).Content = content;
        WireControl(button, control, registry);
        return button;
    }

    private static Control BuildComboControl(RibbonComboBox combo, IRibbonCommandRegistry? registry)
    {
        var box = new ComboBox
        {
            Width = combo.Width ?? 110,
            Height = SmallRowHeight,
            MinHeight = SmallRowHeight,
            MaxHeight = SmallRowHeight,
            FontSize = 12,
            FontFamily = RibbonFontFamily,
            Padding = new Thickness(6, 0, 18, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 1, 0),
            Background = Brushes.White,
            Tag = combo.CommandId.Value,
        };
        foreach (var item in combo.Items)
            box.Items.Add(item);
        if (combo.Items.Count > 0)
            box.SelectedIndex = 0;

        // A user pick executes the control's command, passing the chosen value so the host applies it
        // (e.g. font size). The initial programmatic SelectedIndex is suppressed by a ready flag.
        var ready = false;
        box.SelectionChanged += (_, _) =>
        {
            if (ready)
                ExecuteWithValue(combo.CommandId, registry, box.SelectedItem as string);
        };
        ready = true;

        ApplyEnablement(box, combo, registry);
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

    private static Control Chevron() => Chevron(new Thickness(1, 0, 1, 0));

    private static TextBlock Chevron(Thickness margin)
    {
        return new TextBlock
        {
            Text = "\u25BE",
            FontSize = 9,
            FontFamily = RibbonFontFamily,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin,
        };
    }

    private static ContentControl NewButtonLike(RibbonControl control)
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
                    ApplyToggleCheckedChrome(toggle);
            };
            ApplyToggleCheckedChrome(toggle);
            return toggle;
        }

        return new Button { Tag = control.CommandId.Value };
    }

    private static void ApplyToggleCheckedChrome(ToggleButton toggle)
    {
        if (toggle.IsChecked == true)
        {
            toggle.Background = CheckedBrush;
            toggle.BorderBrush = AccentBrush;
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
    private static void WireControl(ContentControl element, RibbonControl control, IRibbonCommandRegistry? registry)
    {
        if (BuildMenu(control) is { } menu && element is Button menuButton)
        {
            menuButton.Flyout = menu.BuildFlyout(registry);
        }
        else if (element is Button button)
        {
            button.Click += (_, _) => Execute(control.CommandId, registry);
        }
        else if (element is ToggleButton toggle)
        {
            toggle.Click += (_, _) => Execute(control.CommandId, registry);
        }

        ApplyStateAndEnablement(element, control.CommandId, registry);
    }

    private static void ApplyEnablement(Control element, RibbonControl control, IRibbonCommandRegistry? registry)
        => ApplyStateAndEnablement(element, control.CommandId, registry);

    private static void ApplyStateAndEnablement(Control element, RibbonCommandId commandId, IRibbonCommandRegistry? registry)
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
            ApplyRibbonCommandState(element, stateful.GetState());
            return;
        }

        element.IsEnabled = true;
    }

    private static void ApplyRibbonCommandState(Control element, RibbonCommandState state)
    {
        element.IsEnabled = state.IsEnabled;
        switch (element)
        {
            case CheckBox checkBox:
                checkBox.IsChecked = state.IsChecked;
                break;
            case ToggleButton toggle:
                toggle.IsChecked = state.IsChecked;
                ApplyToggleCheckedChrome(toggle);
                break;
            case ComboBox combo when state.Value is { } value:
                combo.Text = value;
                break;
        }
    }

    private static RibbonMenu? BuildMenu(RibbonControl control) => control switch
    {
        RibbonSplitButton split => split.Menu,
        RibbonDropdown dropdown => dropdown.Menu,
        _ => null,
    };

    private static MenuFlyout BuildFlyout(this RibbonMenu menu, IRibbonCommandRegistry? registry)
    {
        var flyout = new MenuFlyout();
        foreach (var item in menu.Items)
            flyout.Items.Add(BuildMenuItem(item, registry));
        return flyout;
    }

    private static Control BuildMenuItem(RibbonMenuItem item, IRibbonCommandRegistry? registry)
    {
        if (item.Kind == RibbonMenuItemKind.Separator)
            return new Separator();

        var menuItem = new MenuItem
        {
            Header = item.Header,
            InputGesture = null,
            Tag = item.CommandId?.Value,
        };

        if (!string.IsNullOrEmpty(item.InputGesture))
            menuItem.InputGesture = TryParseGesture(item.InputGesture);

        if (item.Children.Count > 0)
        {
            foreach (var child in item.Children)
                menuItem.Items.Add(BuildMenuItem(child, registry));
        }
        else if (item.CommandId is { } commandId)
        {
            menuItem.Click += (_, _) => Execute(commandId, registry);
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

    private static void Execute(RibbonCommandId commandId, IRibbonCommandRegistry? registry)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.Empty);
    }

    private static void ExecuteWithValue(RibbonCommandId commandId, IRibbonCommandRegistry? registry, string? value)
    {
        if (registry is null)
            return;
        if (registry.TryGet(commandId, out var command) && command is not null)
            command.Execute(RibbonCommandContext.ForSelectedValue(value));
    }

    private static bool HasMenu(RibbonControl control) =>
        control is RibbonSplitButton or RibbonDropdown;

    private static MenuFlyout BuildCollapsedGroupFlyout(RibbonGroup group, IRibbonCommandRegistry? registry)
    {
        var flyout = new MenuFlyout();
        foreach (var control in group.Controls)
        {
            switch (control)
            {
                case RibbonRowBreak:
                    break;
                case RibbonSeparator:
                    flyout.Items.Add(new Separator());
                    break;
                case RibbonComboBox combo:
                    flyout.Items.Add(new MenuItem { Header = combo.Label, IsEnabled = false, Tag = combo.CommandId.Value });
                    break;
                default:
                    flyout.Items.Add(BuildCollapsedGroupMenuItem(control, registry));
                    break;
            }
        }

        return flyout;
    }

    private static Control BuildCollapsedGroupMenuItem(RibbonControl control, IRibbonCommandRegistry? registry)
    {
        var item = new MenuItem
        {
            Header = control.Label,
            Tag = control.CommandId.Value,
        };

        if (BuildMenu(control) is { } menu)
        {
            foreach (var menuItem in menu.Items)
                item.Items.Add(BuildMenuItem(menuItem, registry));
        }
        else
        {
            item.Click += (_, _) => Execute(control.CommandId, registry);
        }

        ApplyEnablement(item, control.CommandId, registry);
        return item;
    }

    // WPF BuildInlineDivider: a 1px hardcoded #CCCCCC rule, stretched, margin 3.
    private static Control BuildInlineDivider() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(3),
        Fill = InlineDividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    // WPF RibbonGroupDivider: a 1px FreeXBorderBrush rule between groups, margin 2,5,3,18.
    private static Control BuildGroupDivider() => new Rectangle
    {
        Width = 1,
        Margin = new Thickness(2, 5, 3, 18),
        Fill = DividerBrush,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private sealed class AvaloniaRibbonGroupHost : ContentControl
    {
        public const double CollapsedWidth = 64;

        private readonly RibbonGroup _group;
        private readonly Control _full;
        private readonly IRibbonCommandRegistry? _registry;
        private Control? _collapsedButton;
        private bool _collapsed;

        public AvaloniaRibbonGroupHost(RibbonGroup group, Control full, IRibbonCommandRegistry? registry)
        {
            _group = group;
            _full = full;
            _registry = registry;
            Priority = group.Priority;
            VerticalAlignment = VerticalAlignment.Stretch;
            Content = full;
        }

        public int Priority { get; }
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
            var iconSource = _group.Controls.FirstOrDefault(control =>
                control is not RibbonRowBreak and not RibbonSeparator && control.Icon is not null);

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            stack.Children.Add(AvaloniaRibbonIcons.Build(
                iconSource?.Icon?.Kind ?? RibbonCommandIconKind.Generic,
                34,
                iconSource?.CommandId.Value ?? _group.Header));
            stack.Children.Add(new TextBlock
            {
                Text = _group.Header,
                FontSize = 11,
                Foreground = GroupLabelBrush,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.WrapWithOverflow,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 56,
                Margin = new Thickness(0, 2, 0, 0),
            });
            stack.Children.Add(new TextBlock
            {
                Text = "\u25BE",
                FontSize = 9,
                FontFamily = RibbonFontFamily,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.85,
            });

            var button = new Button
            {
                Width = 58,
                Height = 76,
                Padding = new Thickness(2),
                Content = stack,
                Flyout = BuildCollapsedGroupFlyout(_group, _registry),
                Tag = $"collapsed:{_group.Id}",
            };
            button.Classes.Add("freex-ribbon-collapsed-group");
            return button;
        }
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
            var total = hosts.Sum(host => host.FullWidth) + nonHostWidth + spacing;
            var collapsed = new HashSet<AvaloniaRibbonGroupHost>();

            foreach (var host in hosts.OrderBy(host => host.Priority))
            {
                if (total <= available)
                    break;

                collapsed.Add(host);
                total += AvaloniaRibbonGroupHost.CollapsedWidth - host.FullWidth;
            }

            foreach (var host in hosts)
                host.Collapsed = collapsed.Contains(host);

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
