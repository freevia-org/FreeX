using System.Windows.Markup;

namespace FreeX.App.Host;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrWhiteSpace(Key) ? string.Empty : UiText.Get(Key);
}
