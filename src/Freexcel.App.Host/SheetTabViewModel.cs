using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed class SheetTabViewModel(SheetId id, string name, CellColor? tabColor) : System.ComponentModel.INotifyPropertyChanged
{
    public SheetId Id { get; } = id;
    public CellColor? TabColor { get; } = tabColor;
    public System.Windows.Media.Brush TabBrush => TabColor is { } color
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B))
        : System.Windows.Media.Brushes.Transparent;

    private string _name = name;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
        }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    private bool _isGrouped;
    public bool IsGrouped
    {
        get => _isGrouped;
        set
        {
            _isGrouped = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsGrouped)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
