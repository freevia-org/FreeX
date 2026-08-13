namespace Free.Shared.Shell;

/// <summary>An app-owned embedded legal resource and its display title.</summary>
public sealed record LegalNoticeResource(string Title, string ResourceName);

/// <summary>A loaded legal document ready for a renderer-specific read-only surface.</summary>
public sealed record LegalNoticeDocument(string Title, string ResourceName, string Text);
