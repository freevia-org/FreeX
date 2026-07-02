namespace FreeW.App.Presentation.Ribbon;

public sealed record ProofingLanguageChoice(string Tag, string Label);

public static class ProofingLanguageCatalog
{
    public static readonly IReadOnlyList<ProofingLanguageChoice> CommonLanguages =
    [
        new("en-US", "English (United States)"),
        new("en-GB", "English (United Kingdom)"),
        new("en-AU", "English (Australia)"),
        new("fr-FR", "French (France)"),
        new("fr-CA", "French (Canada)"),
        new("de-DE", "German (Germany)"),
        new("es-ES", "Spanish (Spain)"),
        new("es-MX", "Spanish (Mexico)"),
        new("it-IT", "Italian (Italy)"),
        new("pt-BR", "Portuguese (Brazil)"),
        new("pt-PT", "Portuguese (Portugal)"),
        new("nl-NL", "Dutch (Netherlands)"),
        new("pl-PL", "Polish (Poland)"),
        new("ru-RU", "Russian (Russia)"),
        new("ja-JP", "Japanese (Japan)"),
        new("zh-CN", "Chinese Simplified (China)"),
        new("zh-TW", "Chinese Traditional (Taiwan)"),
        new("ko-KR", "Korean (Korea)"),
        new("ar-SA", "Arabic (Saudi Arabia)"),
    ];

    public static string? NormalizeTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
}
