using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
    private const string MenuChevron = "\u25BE";
    private const double SmallRowHeight = 21;
    private const double LargeIconSize = 30;
    private const double MediumIconSize = 16;
    private const double SmallIconSize = 18;
    private const int MaxRowsPerColumn = 3;

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
    //   TabStripColor   = chrome surface               (#F5F6F7) — the light-gray surround behind the tabs
    //                     (FreeXChromeSurfaceBrush analog) so the white selected tab pops out of a gray strip.
    //   TabTextColor    = FreeXTextBrush               (#1F1F1F) — ThemeResources.xaml:13 (near-black tab labels)
    internal static readonly Color SurfaceColor = Color.FromRgb(0xFF, 0xFF, 0xFF);
    internal static readonly Color AccentColor = Color.FromRgb(0x0F, 0x6D, 0x8C);
    internal static readonly Color DividerColor = Color.FromRgb(0xDA, 0xDC, 0xE0);
    internal static readonly Color InlineDividerColor = Color.FromRgb(0xCC, 0xCC, 0xCC);
    internal static readonly Color GroupLabelColor = Color.FromRgb(0x5F, 0x63, 0x68);
    internal static readonly Color HoverColor = Color.FromRgb(0xBE, 0xE6, 0xFD);
    internal static readonly Color HoverBorderColor = Color.FromRgb(0xC8, 0xCC, 0xD0);
    internal static readonly Color CheckedColor = Color.FromRgb(0xCC, 0xEA, 0xF2);
    internal static readonly Color TabHoverColor = Color.FromRgb(0xE6, 0xF6, 0xFA);
    internal static readonly Color TabStripColor = Color.FromRgb(0xF5, 0xF6, 0xF7);
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
                var state = stateful.GetState();
                toggle.IsChecked = state.IsChecked;
                toggle.IsEnabled = state.IsEnabled;
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
        return new Border
        {
            Background = SurfaceBrush,
            Padding = new Thickness(0, 2, 0, 0),
            Child = panel,
        };
    }

    /// <summary>Builds a single <see cref="TabItem"/> for a tab (header + content), tagged with the tab id.</summary>
    private static TabItem BuildTabItem(RibbonTab tab, IRibbonCommandRegistry? registry) => new()
    {
        Header = tab.Header,
        Content = BuildTabContent(tab, registry),
        Tag = tab.Id,
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

        var initialTabs = contextSource is null
            ? (IReadOnlyList<RibbonTab>)definition.VisibleTabs.ToArray()
            : RibbonContextResolver.Resolve(definition, contextSource.Current);

        foreach (var tab in initialTabs)
            tabControl.Items.Add(BuildTabItem(tab, registry));

        if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = 0;

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
        var desired = RibbonContextResolver.Resolve(definition, contextSource.Current);
        var selectedId = (tabControl.SelectedItem as TabItem)?.Tag as string;

        // Remove tabs no longer desired.
        var desiredIds = new HashSet<string>(desired.Select(t => t.Id), StringComparer.Ordinal);
        for (var i = tabControl.Items.Count - 1; i >= 0; i--)
        {
            if (tabControl.Items[i] is TabItem item && item.Tag is string id && !desiredIds.Contains(id))
                tabControl.Items.RemoveAt(i);
        }

        // Insert missing tabs at their resolved (declaration-order) index.
        for (var i = 0; i < desired.Count; i++)
        {
            var tab = desired[i];
            var existingIndex = IndexOfTab(tabControl, tab.Id);
            if (existingIndex < 0)
                tabControl.Items.Insert(Math.Min(i, tabControl.Items.Count), BuildTabItem(tab, registry));
        }

        // Preserve selection if still visible; otherwise select the first tab.
        var restoreIndex = selectedId is null ? -1 : IndexOfTab(tabControl, selectedId);
        if (restoreIndex >= 0)
            tabControl.SelectedIndex = restoreIndex;
        else if (tabControl.Items.Count > 0)
            tabControl.SelectedIndex = 0;
    }

    private static int IndexOfTab(TabControl tabControl, string tabId)
    {
        for (var i = 0; i < tabControl.Items.Count; i++)
            if (tabControl.Items[i] is TabItem item && item.Tag is string id && string.Equals(id, tabId, StringComparison.Ordinal))
                return i;
        return -1;
    }

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
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 3)),
                new Setter(TemplatedControl.FontSizeProperty, 12d),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
                // Avalonia Fluent default tab height is ~48px vs WPF's compact header row; constrain it.
                new Setter(Layoutable.MinHeightProperty, 0d),
                new Setter(Layoutable.HeightProperty, 24d),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 0, 10, 0)),
                new Setter(Layoutable.MarginProperty, new Thickness(0, 0, 1, 0)),
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
                // WPF selected tab: white body + accent bottom border, near-black label (no green text).
                new Setter(TemplatedControl.BackgroundProperty, SurfaceBrush),
                new Setter(TemplatedControl.ForegroundProperty, TabTextBrush),
                new Setter(TemplatedControl.BorderBrushProperty, AccentBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
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
            Setters = { new Setter(TextBlock.ForegroundProperty, TabTextBrush) },
        };

        // ── Buttons: flat, transparent idle; light hover tint + subtle border on pointer-over. ──
        var buttonBase = new Style(x => x.OfType<Button>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
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
                new Setter(TemplatedControl.PaddingProperty, new Thickness(6, 0, 18, 0)),
            },
        };

        var disabledButtons = new Style(x => x.OfType<Button>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45d) },
        };
        var disabledToggles = new Style(x => x.OfType<ToggleButton>().Class(":disabled"))
        {
            Setters = { new Setter(Visual.OpacityProperty, 0.45d) },
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
        tabControl.Styles.Add(tabTextForeground);
        tabControl.Styles.Add(buttonBase);
        tabControl.Styles.Add(buttonHover);
        tabControl.Styles.Add(toggleBase);
        tabControl.Styles.Add(toggleHover);
        tabControl.Styles.Add(toggleChecked);
        tabControl.Styles.Add(comboBase);
        tabControl.Styles.Add(disabledButtons);
        tabControl.Styles.Add(disabledToggles);
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
            Height = SmallRowHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 1),
            Tag = check.CommandId.Value,
        };
        box.IsCheckedChanged += (_, _) => Execute(check.CommandId, registry);
        ApplyEnablement(box, check, registry);
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
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.WrapWithOverflow,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            MaxWidth = 64,
        });

        // Large split/dropdown affordance: a centered chevron on its OWN line under the label, so the
        // hero button reads as visually split (icon + label = primary, the chevron band = open the menu) —
        // matching the WPF hero split-button layout. The flyout is still attached to the whole button by
        // WireControl, so the primary click opens the menu as before; only the visual changes.
        if (HasMenu(control))
        {
            stack.Children.Add(new TextBlock
            {
                Text = MenuChevron,
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        // WPF RibbonLargeButton: compact hero column, Padding 3,2.
        var button = NewButtonLike(control);
        button.Width = 68;
        button.Height = 72;
        button.Padding = new Thickness(3, 2);
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
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 2, 0),
        });
        if (HasMenu(control))
            content.Children.Add(Chevron());

        // WPF RibbonBtn: Height 22, MinWidth 84, left-aligned content, Padding 4,2.
        var button = NewButtonLike(control);
        button.Height = SmallRowHeight;
        button.MinWidth = 84;
        button.Padding = new Thickness(4, 2);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
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

        // WPF RibbonIconButton / RibbonIconToggleButton: Width 24 (34 with menu), Height 22, Padding 2.
        var button = NewButtonLike(control);
        button.Width = hasMenu ? 34 : 24;
        button.Height = SmallRowHeight;
        button.Padding = new Thickness(2);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
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
        // Pass the command id so the icon builder can resolve the per-command SVG glyph (the SAME file
        // the WPF host loads); it falls back to the kind glyph when no SVG matches.
        var icon = AvaloniaRibbonIcons.Build(
            control.Icon?.Kind ?? RibbonCommandIconKind.Generic,
            size,
            control.CommandId.Value);
        icon.HorizontalAlignment = h;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return icon;
    }

    private static TextBlock Chevron() => new()
    {
        Text = MenuChevron,
        FontSize = 9,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(1, 0, 1, 0),
    };

    private static ContentControl NewButtonLike(RibbonControl control)
    {
        if (control is RibbonToggleButton or RibbonCheckBox)
            return new ToggleButton { Tag = control.CommandId.Value };

        return new Button { Tag = control.CommandId.Value };
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

        ApplyEnablement(element, control, registry);
    }

    private static void ApplyEnablement(Control element, RibbonControl control, IRibbonCommandRegistry? registry)
    {
        // No registry => preview/design mode: leave controls enabled so the layout renders fully.
        // With a registry, an unregistered command id renders disabled (never throws).
        if (registry is null)
            return;
        if (string.IsNullOrEmpty(control.CommandId.Value))
            return;
        if (!registry.TryGet(control.CommandId, out var cmd))
        {
            element.IsEnabled = false;
            return;
        }
        // Stateful commands expose IsEnabled in their state (e.g. Draw-tab commands disabled by
        // default when no stylus/pen context is active). Respect that at build time.
        element.IsEnabled = cmd is not IRibbonStatefulCommand stateful || stateful.GetState().IsEnabled;
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
        item.IsEnabled = registry.TryGet(commandId, out _);
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
                Text = "v",
                FontSize = 9,
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
