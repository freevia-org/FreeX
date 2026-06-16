namespace Free.Shared.Ribbon;

/// <summary>Strongly-typed identifier binding a ribbon control to a command handler.</summary>
public readonly record struct RibbonCommandId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator RibbonCommandId(string value) => new(value);
}
