using System.Text;

namespace FreeW.Core.IO;

/// <summary>Line-ending style emitted by the plain-text writer.</summary>
public enum EolStyle
{
    /// <summary>Windows line endings (<c>\r\n</c>) — the default, matching Notepad/Word.</summary>
    Crlf,

    /// <summary>Unix line endings (<c>\n</c>).</summary>
    Lf,

    /// <summary>Classic Mac line endings (<c>\r</c>).</summary>
    Cr,
}

/// <summary>
/// Per-save options for the plain-text adapter. Carried at the IO layer (constructor-injected into the
/// adapter), never on <see cref="FreeW.Core.Model.TextDocument"/>, so the model stays format-neutral.
/// </summary>
/// <param name="Encoding">Text encoding to write. Defaults to UTF-8 without a byte-order mark.</param>
/// <param name="Eol">Line-ending style. Defaults to <see cref="EolStyle.Crlf"/>.</param>
/// <param name="EmitBom">Whether to prepend a UTF-8 byte-order mark. Defaults to false.</param>
public sealed record TextSaveOptions(
    Encoding Encoding,
    EolStyle Eol = EolStyle.Crlf,
    bool EmitBom = false)
{
    /// <summary>UTF-8, no BOM, CRLF — FreeW's default plain-text save profile.</summary>
    public static TextSaveOptions Default { get; } =
        new(new UTF8Encoding(false));
}
