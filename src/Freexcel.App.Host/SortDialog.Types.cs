using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Freexcel.App.Host;

public sealed record SortColumnChoice(string Label, uint ColumnOffset);

public sealed record SortDirectionChoice(string Label, bool Ascending);

public sealed record SortOnChoice(string Label);

public sealed record SortColorChoice(string Label);

public sealed record SortDialogOptions(bool CaseSensitive = false, bool LeftToRight = false);

public sealed class SortDialogLevel : IEquatable<SortDialogLevel>, INotifyPropertyChanged
{
    private uint _columnOffset;
    private bool _ascending;
    private string _sortOn = "Cell Values";
    private string _targetColor = "";
    private IReadOnlyList<SortColorChoice> _colorChoices = [new SortColorChoice("")];

    public SortDialogLevel(uint columnOffset, bool ascending)
    {
        _columnOffset = columnOffset;
        _ascending = ascending;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public uint ColumnOffset
    {
        get => _columnOffset;
        set => SetField(ref _columnOffset, value);
    }

    public bool Ascending
    {
        get => _ascending;
        set => SetField(ref _ascending, value);
    }

    public string SortOn
    {
        get => _sortOn;
        set
        {
            if (SetField(ref _sortOn, value))
                OnPropertyChanged(nameof(OrderChoices));
        }
    }

    public string TargetColor
    {
        get => _targetColor;
        set => SetField(ref _targetColor, value);
    }

    public IReadOnlyList<SortDirectionChoice> OrderChoices => SortDialog.BuildOrderChoices(SortOn);

    public IReadOnlyList<SortColorChoice> ColorChoices => _colorChoices;

    public bool Equals(SortDialogLevel? other) =>
        other is not null &&
        ColumnOffset == other.ColumnOffset &&
        Ascending == other.Ascending &&
        string.Equals(SortOn, other.SortOn, StringComparison.Ordinal) &&
        string.Equals(TargetColor, other.TargetColor, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as SortDialogLevel);

    public override int GetHashCode() => HashCode.Combine(ColumnOffset, Ascending, SortOn, TargetColor.ToUpperInvariant());

    public override string ToString() => $"Column offset {ColumnOffset}, {(Ascending ? "Ascending" : "Descending")}";

    internal void SetColorChoices(IReadOnlyList<SortColorChoice> colorChoices)
    {
        _colorChoices = colorChoices.Count == 0 ? [new SortColorChoice("")] : colorChoices;
        if (!string.IsNullOrWhiteSpace(TargetColor) &&
            !_colorChoices.Any(choice => string.Equals(choice.Label, TargetColor, StringComparison.OrdinalIgnoreCase)))
            TargetColor = "";
        OnPropertyChanged(nameof(ColorChoices));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed partial class SortDialog
{
    private static readonly IReadOnlyList<SortDirectionChoice> DirectionChoices =
    [
        new("A to Z", true),
        new("Z to A", false)
    ];

    private static readonly IReadOnlyList<SortDirectionChoice> ColorDirectionChoices =
    [
        new("On Top", true),
        new("On Bottom", false)
    ];

    private static readonly IReadOnlyList<SortOnChoice> SortOnChoices =
    [
        new("Cell Values"),
        new("Cell Color"),
        new("Font Color")
    ];
}
