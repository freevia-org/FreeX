namespace FreeX.App.Presentation.Filtering;

public interface IAutoFilterMenuTextProvider
{
    string Get(string resourceKey);

    string Format(string resourceKey, string value);
}
