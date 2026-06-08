using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record WorkbookFindAllMatch(
    string Book,
    string Sheet,
    string Name,
    CellAddress Address,
    string Cell,
    string Value,
    string Formula)
{
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name)
            ? $"{Sheet}!{Cell}  {Value}"
            : $"{Sheet}!{Cell}  {Name}  {Value}";
}
