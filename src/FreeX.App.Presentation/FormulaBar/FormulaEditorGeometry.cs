namespace FreeX.App.Presentation.FormulaBar;

public readonly record struct FormulaEditorRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

public readonly record struct FormulaEditorThickness(double Left, double Top, double Right, double Bottom)
{
    public FormulaEditorThickness(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }
}
