using System.Buffers.Binary;
using FreeW.App.Presentation.DocumentView.MathLayout;

namespace FreeW.App.Presentation.Tests;

/// <summary>
/// The toolkit-neutral OpenType MATH reader, against a synthetic in-memory font (portable: no system fonts).
/// </summary>
public sealed class OpenTypeMathTableTests
{
    [Fact]
    public void ReadsConstantsVariantsAndAssemblies()
    {
        var table = OpenTypeMathTable.TryLoad(BuildFont());

        table.Should().NotBeNull();
        table!.UnitsPerEm.Should().Be(1000);
        table.Constants.ScriptPercentScaleDown.Should().Be(70);
        table.Constants.ScriptScriptPercentScaleDown.Should().Be(50);
        table.Constants.DelimitedSubFormulaMinHeight.Should().BeApproximately(1.3, 1e-9);
        table.Constants.DisplayOperatorMinHeight.Should().BeApproximately(1.4, 1e-9);
        table.Constants.MathLeading.Should().BeApproximately(0.010, 1e-9);
        table.Constants.AxisHeight.Should().BeApproximately(0.020, 1e-9);
        table.Constants.RadicalKernAfterDegree.Should().BeApproximately(-0.5, 1e-9);
        table.Constants.RadicalDegreeBottomRaisePercent.Should().Be(60);
        table.MinConnectorOverlap.Should().BeApproximately(0.02, 1e-9);

        table.Variants(5, vertical: true).Should().Equal(new MathGlyphVariant(5, 1.0), new MathGlyphVariant(6, 1.5));
        table.Variants(5, vertical: false).Should().BeEmpty();
        table.Variants(9, vertical: true).Should().BeEmpty();

        var assembly = table.Assembly(5, vertical: true);
        assembly.Should().NotBeNull();
        assembly!.ItalicCorrection.Should().BeApproximately(0.03, 1e-9);
        assembly.Parts.Should().Equal(
            new MathGlyphPart(7, 0, 0.1, 0.5, IsExtender: false),
            new MathGlyphPart(8, 0.1, 0.1, 0.4, IsExtender: true));
    }

    [Fact]
    public void CambriaMathConstants_MatchTheFontsTable()
    {
        var constants = MathFontConstants.CambriaMath;

        constants.ScriptPercentScaleDown.Should().Be(73);
        constants.ScriptScriptPercentScaleDown.Should().Be(60);
        constants.AxisHeight.Should().BeApproximately(585 / 2048.0, 1e-9);
        constants.FractionRuleThickness.Should().BeApproximately(133 / 2048.0, 1e-9);
        constants.RadicalKernAfterDegree.Should().BeApproximately(-640 / 2048.0, 1e-9);
        constants.RadicalDegreeBottomRaisePercent.Should().Be(65);
    }

    [Fact]
    public void ReturnsNullForDataThatIsNotAMathFont()
    {
        OpenTypeMathTable.TryLoad([]).Should().BeNull();
        OpenTypeMathTable.TryLoad(new byte[64]).Should().BeNull();
        OpenTypeMathTable.TryLoad(BuildFont()[..40]).Should().BeNull();
    }

    private static byte[] BuildFont()
    {
        // MATH table: header, constants, variants (one vertical construction with an assembly).
        var math = new List<byte>();
        void U16(List<byte> target, int value) => target.AddRange([(byte)(value >> 8), (byte)value]);

        const int constantsOffset = 10;
        const int constantsLength = 4 + 4 + (51 * 4) + 2;
        const int variantsOffset = constantsOffset + constantsLength;
        U16(math, 1); U16(math, 0);
        U16(math, constantsOffset); U16(math, 0); U16(math, variantsOffset);
        U16(math, 70); U16(math, 50); U16(math, 1300); U16(math, 1400);
        for (var i = 0; i < 51; i++)
        {
            U16(math, i == 50 ? unchecked((ushort)-500) : (i + 1) * 10);
            U16(math, 0);
        }

        U16(math, 60);

        // MathVariants at variantsOffset.
        var variants = new List<byte>();
        const int coverageOffset = 12;
        const int constructionOffset = coverageOffset + 6;
        U16(variants, 20); U16(variants, coverageOffset); U16(variants, 0); U16(variants, 1); U16(variants, 0); U16(variants, constructionOffset);
        U16(variants, 1); U16(variants, 1); U16(variants, 5);
        const int assemblyOffset = 4 + 8;
        U16(variants, assemblyOffset); U16(variants, 2);
        U16(variants, 5); U16(variants, 1000);
        U16(variants, 6); U16(variants, 1500);
        U16(variants, 30); U16(variants, 0); U16(variants, 2);
        U16(variants, 7); U16(variants, 0); U16(variants, 100); U16(variants, 500); U16(variants, 0);
        U16(variants, 8); U16(variants, 100); U16(variants, 100); U16(variants, 400); U16(variants, 1);
        math.AddRange(variants);

        var head = new byte[54];
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);

        const int directoryLength = 12 + (2 * 16);
        var font = new byte[directoryLength + head.Length + math.Count];
        BinaryPrimitives.WriteUInt32BigEndian(font, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 2);
        WriteRecord(font, 12, "head", directoryLength, head.Length);
        WriteRecord(font, 28, "MATH", directoryLength + head.Length, math.Count);
        head.CopyTo(font, directoryLength);
        math.CopyTo(font, directoryLength + head.Length);
        return font;
    }

    private static void WriteRecord(byte[] font, int at, string tag, int offset, int length)
    {
        for (var i = 0; i < 4; i++)
            font[at + i] = (byte)tag[i];
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(at + 8), (uint)offset);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(at + 12), (uint)length);
    }
}
