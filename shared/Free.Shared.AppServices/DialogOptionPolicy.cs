namespace Free.Shared.AppServices;

public static class DialogOptionPolicy
{
    public static int IndexOf<T>(IReadOnlyList<T> values, T value)
    {
        ArgumentNullException.ThrowIfNull(values);

        for (var i = 0; i < values.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(values[i], value))
                return i;
        }

        return -1;
    }

    public static T ValueAtOrDefault<T>(IReadOnlyList<T> values, int index)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("At least one option is required.", nameof(values));

        return values[Math.Clamp(index, 0, values.Count - 1)];
    }
}
