namespace FreeX.Core.Commands;

/// <summary>
/// The user's Excel "Custom Lists" (File > Options > Advanced > Edit Custom Lists), published by the
/// options layer so every fill-handle drag and Fill Series picks them up without each caller
/// threading them through. An explicit list passed to <see cref="AutofillCommand"/> still wins.
/// </summary>
public static class AutoFillCustomLists
{
    private static IReadOnlyList<IReadOnlyList<string>> _current = [];

    public static IReadOnlyList<IReadOnlyList<string>> Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? []);
    }
}
