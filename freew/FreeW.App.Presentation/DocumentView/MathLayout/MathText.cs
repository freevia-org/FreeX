using System.Text;

namespace FreeW.App.Presentation.DocumentView.MathLayout;

/// <summary>TeX-style atom classes; they decide the automatic spacing between neighbouring atoms.</summary>
public enum MathAtomClass
{
    Ord,
    Op,
    Bin,
    Rel,
    Open,
    Close,
    Punct,
    Inner
}

/// <summary>One classified piece of math text: a single operator, or a run of ordinary characters.</summary>
public readonly record struct MathTextAtom(string Text, MathAtomClass Class, bool IsSpace);

/// <summary>
/// Character handling for Word's math zone: letters become Unicode mathematical italic (what Cambria Math
/// draws for Word's default math run style), the ASCII hyphen becomes a true minus, and every character is
/// classified so operators get Word/TeX spacing.
/// </summary>
public static class MathText
{
    private const string BinaryOperators = "+−±∓×÷·∙∘∗∩∪⊕⊗⊖⊙∧∨⋅∖†‡⋆⊔⊓";
    private const string Relations = "=<>≤≥≠≈≡≅∼≃≪≫∝∈∉∋∌⊂⊃⊆⊇⊄⊅→←↔⇒⇐⇔↦∣∥⊥≜≝≔≐≡≢≮≯≰≱≺≻⪯⪰⊢⊨↑↓⟶⟵⟷⟹⟸⟺≦≧≲≳∴∵∷:";
    private const string Openers = "([{⟨⌊⌈〈〔⟦";
    private const string Closers = ")]}⟩⌋⌉〉〕⟧!";
    private const string Punctuation = ",;";

    /// <summary>Whether a code point is an n-ary/large operator (∑, ∏, ∫, ⋃, …).</summary>
    public static bool IsLargeOperator(int codePoint) =>
        codePoint is >= 0x220F and <= 0x2211
            or >= 0x222B and <= 0x2233
            or >= 0x22C0 and <= 0x22C3
            or >= 0x2A00 and <= 0x2A0C;

    /// <summary>Whether a code point is an integral (whose limits Word sets as scripts by default).</summary>
    public static bool IsIntegral(int codePoint) =>
        codePoint is >= 0x222B and <= 0x2233 or 0x2A0C;

    /// <summary>Whether a code point is in one of the mathematical-italic ranges <see cref="ToMathItalic"/> produces.</summary>
    public static bool IsMathItalicCodePoint(int codePoint) =>
        codePoint is >= 0x1D434 and <= 0x1D467 or 0x210E or >= 0x1D6A4 and <= 0x1D6A5 or >= 0x1D6E2 and <= 0x1D71B;

    /// <summary>Maps a code point to the glyph Word draws for it in an italic (default) math run.</summary>
    public static int ToMathItalic(int codePoint) => codePoint switch
    {
        'h' => 0x210E,
        >= 'a' and <= 'z' => 0x1D44E + (codePoint - 'a'),
        >= 'A' and <= 'Z' => 0x1D434 + (codePoint - 'A'),
        >= 0x03B1 and <= 0x03C9 => 0x1D6FC + (codePoint - 0x03B1),
        0x0131 => 0x1D6A4,
        0x0237 => 0x1D6A5,
        _ => codePoint
    };

    /// <summary>Normalises a math-zone character: ASCII hyphen to minus, asterisk to ∗, apostrophe to prime.</summary>
    public static int NormalizeOperator(int codePoint) => codePoint switch
    {
        '-' => 0x2212,
        '*' => 0x2217,
        '\'' => 0x2032,
        _ => codePoint
    };

    /// <summary>The atom class of a single (normalised) code point.</summary>
    public static MathAtomClass Classify(int codePoint)
    {
        if (codePoint > 0xFFFF)
            return MathAtomClass.Ord;
        var c = (char)codePoint;
        if (BinaryOperators.Contains(c))
            return MathAtomClass.Bin;
        if (Relations.Contains(c))
            return MathAtomClass.Rel;
        if (Openers.Contains(c))
            return MathAtomClass.Open;
        if (Closers.Contains(c))
            return MathAtomClass.Close;
        if (Punctuation.Contains(c))
            return MathAtomClass.Punct;
        if (IsLargeOperator(codePoint))
            return MathAtomClass.Op;
        return MathAtomClass.Ord;
    }

    /// <summary>
    /// Splits math text into atoms: spaces, single operators/punctuation/fences, and maximal runs of
    /// ordinary characters. Letters are mapped to mathematical italic unless <paramref name="upright"/>.
    /// </summary>
    public static IReadOnlyList<MathTextAtom> Atomize(string text, bool upright)
    {
        var atoms = new List<MathTextAtom>();
        if (string.IsNullOrEmpty(text))
            return atoms;

        var ordinary = new StringBuilder();
        void FlushOrdinary()
        {
            if (ordinary.Length == 0)
                return;
            atoms.Add(new MathTextAtom(ordinary.ToString(), MathAtomClass.Ord, IsSpace: false));
            ordinary.Clear();
        }

        foreach (var rune in text.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (codePoint is ' ' or ' ' or '\t')
            {
                FlushOrdinary();
                atoms.Add(new MathTextAtom(" ", MathAtomClass.Ord, IsSpace: true));
                continue;
            }

            // Zero-width layout controls (function application, invisible times/separator) draw nothing.
            if (codePoint is 0x2061 or 0x2062 or 0x2063 or 0x2064 or 0x200B)
                continue;

            codePoint = NormalizeOperator(codePoint);
            var atomClass = Classify(codePoint);
            if (!upright && atomClass == MathAtomClass.Ord)
                codePoint = ToMathItalic(codePoint);

            if (atomClass == MathAtomClass.Ord)
            {
                ordinary.Append(char.ConvertFromUtf32(codePoint));
                continue;
            }

            FlushOrdinary();
            atoms.Add(new MathTextAtom(char.ConvertFromUtf32(codePoint), atomClass, IsSpace: false));
        }

        FlushOrdinary();
        return atoms;
    }

    /// <summary>
    /// Automatic space (in mu, 1/18 em) between two adjacent atoms, following TeX's inter-atom table that
    /// Word's math layout also uses: thin (3), medium (4) and thick (5) spaces, the medium/thick ones (and
    /// most thin ones) suppressed in script styles.
    /// </summary>
    public static int SpacingMu(MathAtomClass left, MathAtomClass right, bool scriptStyle)
    {
        // Rows: left class; columns: right class (Ord, Op, Bin, Rel, Open, Close, Punct, Inner).
        // Positive = always; negative = only outside script styles.
        ReadOnlySpan<int> table =
        [
            0, 3, -4, -5, 0, 0, 0, -3,
            3, 3, 0, -5, 0, 0, 0, -3,
            -4, -4, 0, 0, -4, 0, 0, -4,
            -5, -5, 0, 0, -5, 0, 0, -5,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 3, -4, -5, 0, 0, 0, -3,
            -3, -3, 0, -3, -3, -3, -3, -3,
            -3, 3, -4, -5, -3, 0, -3, -3
        ];
        var value = table[((int)left * 8) + (int)right];
        if (value < 0)
            return scriptStyle ? 0 : -value;
        return value;
    }
}
