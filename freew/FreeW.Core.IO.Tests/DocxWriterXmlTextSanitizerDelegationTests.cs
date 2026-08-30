using System.Reflection;

namespace FreeW.Core.IO.Tests;

public sealed class DocxWriterXmlTextSanitizerDelegationTests
{
    public static IEnumerable<object?[]> SanitizationCases()
    {
        yield return [null, string.Empty];
        yield return [string.Empty, string.Empty];
        yield return ["tab\tline\ncarriage\rreturn", "tab\tline\ncarriage\rreturn"];
        yield return ["before\0\u0001\u0008\u000B\u000C\u001Fafter", "beforeafter"];
        yield return ["before\uFFFE\uFFFFafter", "beforeafter"];
        yield return ["before" + char.ConvertFromUtf32(0x1F642) + "after", "before" + char.ConvertFromUtf32(0x1F642) + "after"];
    }

    [Theory]
    [MemberData(nameof(SanitizationCases))]
    public void PrivateWrapper_PreservesSharedSanitizerSemantics(string? input, string expected)
    {
        InvokeSanitizeXmlText(input).Should().Be(expected);
    }

    [Fact]
    public void PrivateWrapper_ReturnsTheOriginalCleanStringInstance()
    {
        var clean = new string("ordinary text".ToCharArray());

        var result = InvokeSanitizeXmlText(clean);

        ReferenceEquals(result, clean).Should().BeTrue(
            "the shared sanitizer's allocation-free clean-text fast path must remain observable through the wrapper");
    }

    [Fact]
    public void PrivateWrapper_DropsLoneSurrogatesCreatedAtExecutionTime()
    {
        var loneHigh = "before" + new string((char)0xD800, 1) + "after";
        var loneLow = "before" + new string((char)0xDC00, 1) + "after";

        InvokeSanitizeXmlText(loneHigh).Should().Be("beforeafter");
        InvokeSanitizeXmlText(loneLow).Should().Be("beforeafter");
    }

    [Fact]
    public void PrivateWrapper_RemainsABlockBodiedSharedHelperDelegation()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.Core.IO",
            "DocxWriter.cs"));

        const string expected =
            "    private static string SanitizeXmlText(string? text)\n" +
            "    {\n" +
            "        return XmlTextSanitizer.Sanitize(text);\n" +
            "    }";

        source.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain(expected)
            .And.NotContain("static bool IsXmlIllegal(char c, string s, ref int i)")
            .And.NotContain("static bool IsXml10IllegalChar(char c)");
    }

    private static string InvokeSanitizeXmlText(string? text)
    {
        var method = typeof(DocxWriter).GetMethod(
            "SanitizeXmlText",
            BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull("the private compatibility wrapper is a guarded source contract");

        return (string)method!.Invoke(null, [text])!;
    }
}
