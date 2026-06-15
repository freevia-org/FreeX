namespace FreeX.Ribbon;

/// <summary>Fluent, type-safe construction of a <see cref="RibbonDefinition"/>.</summary>
public sealed class RibbonDefinitionBuilder
{
    private readonly List<RibbonTab> _tabs = new();

    public RibbonDefinitionBuilder Tab(string id, string header, string? keyTip, Action<RibbonTabBuilder> configure)
        => AddTab(id, header, keyTip, context: null, configure);

    public RibbonDefinitionBuilder ContextualTab(string id, string header, RibbonTabContext context, Action<RibbonTabBuilder> configure)
        => AddTab(id, header, keyTip: null, context, configure);

    private RibbonDefinitionBuilder AddTab(string id, string header, string? keyTip, RibbonTabContext? context, Action<RibbonTabBuilder> configure)
    {
        var builder = new RibbonTabBuilder(id, header, keyTip, context);
        configure(builder);
        _tabs.Add(builder.Build());
        return this;
    }

    public RibbonDefinition Build() => new(_tabs.ToArray());
}

public sealed class RibbonTabBuilder
{
    private readonly string _id;
    private readonly string _header;
    private readonly string? _keyTip;
    private readonly RibbonTabContext? _context;
    private readonly List<RibbonGroup> _groups = new();

    internal RibbonTabBuilder(string id, string header, string? keyTip, RibbonTabContext? context)
    {
        _id = id;
        _header = header;
        _keyTip = keyTip;
        _context = context;
    }

    public RibbonTabBuilder Group(string id, string header, string? keyTip, int priority, Action<RibbonGroupBuilder> configure)
    {
        var builder = new RibbonGroupBuilder(id, header, keyTip, priority);
        configure(builder);
        _groups.Add(builder.Build());
        return this;
    }

    internal RibbonTab Build() => new(_id, _header, _keyTip, _context, _groups.ToArray());
}

public sealed class RibbonGroupBuilder
{
    private readonly string _id;
    private readonly string _header;
    private readonly string? _keyTip;
    private readonly int _priority;
    private readonly List<RibbonControl> _controls = new();
    private RibbonGroupSizing _sizing = RibbonGroupSizing.Default;

    internal RibbonGroupBuilder(string id, string header, string? keyTip, int priority)
    {
        _id = id;
        _header = header;
        _keyTip = keyTip;
        _priority = priority;
    }

    public RibbonGroupBuilder Button(string commandId, string label, Func<RibbonButton, RibbonButton>? configure = null)
        => Add(new RibbonButton(commandId, label), configure);

    public RibbonGroupBuilder Toggle(string commandId, string label, Func<RibbonToggleButton, RibbonToggleButton>? configure = null)
        => Add(new RibbonToggleButton(commandId, label), configure);

    public RibbonGroupBuilder CheckBox(string commandId, string label, Func<RibbonCheckBox, RibbonCheckBox>? configure = null)
        => Add(new RibbonCheckBox(commandId, label), configure);

    public RibbonGroupBuilder ComboBox(string commandId, string label, Func<RibbonComboBox, RibbonComboBox>? configure = null)
        => Add(new RibbonComboBox(commandId, label), configure);

    public RibbonGroupBuilder SplitButton(string commandId, string label, RibbonMenu menu, Func<RibbonSplitButton, RibbonSplitButton>? configure = null)
        => Add(new RibbonSplitButton(commandId, label, menu), configure);

    public RibbonGroupBuilder Dropdown(string commandId, string label, RibbonMenu menu, Func<RibbonDropdown, RibbonDropdown>? configure = null)
        => Add(new RibbonDropdown(commandId, label, menu), configure);

    public RibbonGroupBuilder Gallery(string commandId, string label, Func<RibbonGallery, RibbonGallery>? configure = null)
        => Add(new RibbonGallery(commandId, label), configure);

    public RibbonGroupBuilder Separator()
    {
        _controls.Add(new RibbonSeparator());
        return this;
    }

    public RibbonGroupBuilder RowBreak()
    {
        _controls.Add(new RibbonRowBreak());
        return this;
    }

    /// <summary>A large "hero" button: big icon, label below, optional dropdown (with optional menu contents).</summary>
    public RibbonGroupBuilder Large(string commandId, string label, RibbonCommandIconKind icon, string? keyTip = null, bool dropdown = false, Action<RibbonMenuBuilder>? menu = null)
        => AddSized(RibbonCommandLayoutKind.Large, commandId, label, icon, keyTip, dropdown, menu);

    /// <summary>An icon-only button (no label), optionally a dropdown (with optional menu contents).</summary>
    public RibbonGroupBuilder Icon(string commandId, string label, RibbonCommandIconKind icon, string? keyTip = null, bool dropdown = false, Action<RibbonMenuBuilder>? menu = null)
        => AddSized(RibbonCommandLayoutKind.Small, commandId, label, icon, keyTip, dropdown, menu);

    /// <summary>A medium button: small icon with a label to the right, optional dropdown (with optional menu contents).</summary>
    public RibbonGroupBuilder Medium(string commandId, string label, RibbonCommandIconKind icon, string? keyTip = null, bool dropdown = false, Action<RibbonMenuBuilder>? menu = null)
        => AddSized(RibbonCommandLayoutKind.Medium, commandId, label, icon, keyTip, dropdown, menu);

    /// <summary>An icon-only toggle button (no label).</summary>
    public RibbonGroupBuilder IconToggle(string commandId, string label, RibbonCommandIconKind icon, string? keyTip = null)
    {
        _controls.Add(new RibbonToggleButton(commandId, label) with
        {
            PreferredLayout = RibbonCommandLayoutKind.Small,
            Icon = new RibbonCommandIcon(icon),
            KeyTip = keyTip
        });
        return this;
    }

    private RibbonGroupBuilder AddSized(RibbonCommandLayoutKind layout, string commandId, string label, RibbonCommandIconKind icon, string? keyTip, bool dropdown, Action<RibbonMenuBuilder>? menu)
    {
        RibbonMenu? builtMenu = null;
        if (menu is not null)
        {
            var mb = new RibbonMenuBuilder();
            menu(mb);
            builtMenu = mb.Build();
        }

        RibbonControl control = (dropdown || builtMenu is not null)
            ? new RibbonDropdown(commandId, label, builtMenu ?? RibbonMenu.Empty)
            : new RibbonButton(commandId, label);

        _controls.Add(control with
        {
            PreferredLayout = layout,
            Icon = new RibbonCommandIcon(icon),
            KeyTip = keyTip
        });
        return this;
    }

    public RibbonGroupBuilder Sizing(RibbonGroupSizing sizing)
    {
        _sizing = sizing;
        return this;
    }

    private RibbonGroupBuilder Add<T>(T control, Func<T, T>? configure) where T : RibbonControl
    {
        _controls.Add(configure is null ? control : configure(control));
        return this;
    }

    internal RibbonGroup Build() => new(_id, _header, _keyTip, _priority, _controls.ToArray(), _sizing);
}

/// <summary>Fluent construction of a dropdown's menu contents.</summary>
public sealed class RibbonMenuBuilder
{
    private readonly List<RibbonMenuItem> _items = new();

    public RibbonMenuBuilder Item(string commandId, string header, string? keyTip = null, string? gesture = null)
    {
        _items.Add(new RibbonMenuItem(header, new RibbonCommandId(commandId), keyTip, gesture));
        return this;
    }

    public RibbonMenuBuilder Separator()
    {
        _items.Add(RibbonMenuItem.Separator());
        return this;
    }

    internal RibbonMenu Build() => new(_items.ToArray());
}
