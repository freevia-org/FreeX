using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Source-contract guard closing the class of bug fixed in round 162 (the Mark Citation TA field's
/// <c>instrText</c> bypassed <c>SanitizeXmlText</c> because <c>CitationInstruction</c> is a hand-written
/// composer that the August WordArt sweep, commit e3efcd6ae1, could not find by searching for callers of
/// the sanitizer or of the generic field-instruction builder). Every place <see cref="DocxWriter"/>
/// constructs a <c>w:t</c>/<c>w:delText</c>/<c>w:instrText</c> element or a <c>w:instr</c> attribute must
/// route its value through <c>SanitizeXmlText</c> first, because <c>XDocument.Save</c> throws
/// <c>ArgumentException</c> -- aborting the whole save -- on an XML-1.0-illegal character (a C0 control
/// code or a lone surrogate) anywhere in a text node OR an attribute value, not just in ordinary run text.
///
/// Rather than trusting a helper method's name (the citation bug was invisible to exactly that search),
/// this test scans the actual constructor call at every such site in the DocxWriter.cs source and asserts
/// the value argument passes through <c>SanitizeXmlText(...)</c>, with one explicit, narrow allowlist
/// entry for the single site that is provably safe without it (a fixed set of literal field keywords).
/// A future hand-written composer -- for a brand-new field kind, or a second call site for an existing
/// one -- fails this test the moment it bypasses the sanitizer, instead of waiting for the next audit
/// round to notice it by inspection.
/// </summary>
public class DocxWriterInstructionSanitizationInvariantTests
{
    // The literal keyword set (" PAGE ", " DATE ", ...) built by FieldInstruction(RunFieldKind) is the one
    // w:instr site with no user-controlled data: it can only ever be one of a handful of hardcoded
    // switch-expression literals, so it needs no sanitizer. Allowlisted by its exact call-site text so an
    // unrelated future edit to this line trips the test and forces a fresh look, rather than silently
    // riding on a stale exemption.
    private const string FieldKeywordSite = "(W + \"instr\", instruction)";

    // Workspace traversal belongs in TestWorkspaceFileLocator, not in a private walker per test file --
    // TestWorkspaceFileLocatorSourceGuardTests enforces that, and the first version of this file tripped it.
    private static string DocxWriterSource() =>
        File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.Core.IO",
            "DocxWriter.cs"));

    // Matches the start of every constructor call that names one of the XML-illegal-character-sensitive
    // element/attribute names as its first argument: new XElement(W + "t"/"delText"/"instrText", ...) or
    // new XAttribute(W + "instr", ...).
    private static readonly Regex SiteStart = new(
        @"new\s+X(?:Element|Attribute)\(\s*W\s*\+\s*""(?:instrText|instr|delText|t)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Extracts the full text of the constructor call starting at <paramref name="openParenIndex"/>
    /// (the '(' immediately after "new XElement"/"new XAttribute"), by counting balanced parentheses.
    /// String literals are skipped char-by-char so a ')' or '(' inside one (e.g. inside "#,##0.00") can't
    /// desync the count.
    /// </summary>
    private static string ExtractCall(string source, int openParenIndex)
    {
        var depth = 0;
        var i = openParenIndex;
        for (; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"')
            {
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\')
                        i++;
                    i++;
                }
                continue;
            }
            if (c == '(')
                depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0)
                    return source[openParenIndex..(i + 1)];
            }
        }
        throw new InvalidOperationException("Unbalanced parentheses scanning DocxWriter.cs from index " + openParenIndex);
    }

    /// <summary>
    /// Splits a constructor call's argument list (the text between its outer parentheses, exclusive) into
    /// its top-level, comma-separated arguments -- ignoring commas nested inside parens/brackets/braces or
    /// inside string literals, so e.g. <c>Foo(a, b)</c> as a whole argument isn't split at its inner comma.
    /// </summary>
    private static List<string> SplitTopLevelArguments(string argsText)
    {
        var args = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < argsText.Length; i++)
        {
            var c = argsText[i];
            if (c == '"')
            {
                i++;
                while (i < argsText.Length && argsText[i] != '"')
                {
                    if (argsText[i] == '\\')
                        i++;
                    i++;
                }
                continue;
            }
            if (c is '(' or '[' or '{')
                depth++;
            else if (c is ')' or ']' or '}')
                depth--;
            else if (c == ',' && depth == 0)
            {
                args.Add(argsText[start..i].Trim());
                start = i + 1;
            }
        }
        args.Add(argsText[start..].Trim());
        return args;
    }

    private static readonly Regex SimpleCallPattern = new(@"^([A-Za-z_][A-Za-z0-9_]*)\((.*)\)$", RegexOptions.Singleline);

    /// <summary>
    /// True when <paramref name="valueExpr"/> is, or is entirely produced by, an expression that calls
    /// <c>SanitizeXmlText</c>: either directly, or by delegating (possibly through a chain of private
    /// helper methods defined in <paramref name="source"/>) to one that does. Walks the callee chain
    /// rather than requiring "SanitizeXmlText(" to appear at the original call site, because the
    /// legitimate fix for the round-162 bug moved the sanitize call inside the instruction-builder
    /// methods themselves (one chokepoint protecting every caller) rather than wrapping every call site.
    /// </summary>
    private static bool IsSanitizedTransitively(string source, string valueExpr, HashSet<string> visiting)
    {
        valueExpr = valueExpr.Trim();
        if (valueExpr.Contains("SanitizeXmlText("))
            return true;

        var simple = SimpleCallPattern.Match(valueExpr);
        if (!simple.Success)
            return false;

        var methodName = simple.Groups[1].Value;
        if (!visiting.Add(methodName))
            return false; // recursive/cyclic reference -- treat as unproven rather than looping forever

        var signature = Regex.Match(
            source, $@"private\s+static\s+string\s+{Regex.Escape(methodName)}\s*\([^)]*\)\s*\{{");
        if (!signature.Success)
            return false;

        var braceOpen = source.IndexOf('{', signature.Index);
        var body = ExtractBracedBody(source, braceOpen);

        if (body.Contains("SanitizeXmlText("))
            return true;

        // The method doesn't sanitize directly -- see if every one of its return statements delegates to
        // something that does (covers a thin wrapper around another builder).
        var returns = Regex.Matches(body, @"return\s+(?<expr>[^;]+);");
        if (returns.Count == 0)
            return false;

        foreach (Match ret in returns)
        {
            if (!IsSanitizedTransitively(source, ret.Groups["expr"].Value, visiting))
                return false;
        }
        return true;
    }

    /// <summary>Extracts a brace-delimited method body (the '{' at <paramref name="openBraceIndex"/> through its match).</summary>
    private static string ExtractBracedBody(string source, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '"')
            {
                i++;
                while (i < source.Length && source[i] != '"')
                {
                    if (source[i] == '\\')
                        i++;
                    i++;
                }
                continue;
            }
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return source[(openBraceIndex + 1)..i];
            }
        }
        throw new InvalidOperationException("Unbalanced braces scanning DocxWriter.cs from index " + openBraceIndex);
    }

    private static List<string> FindUnsanitizedSites(string source)
    {
        var unsanitized = new List<string>();
        foreach (Match match in SiteStart.Matches(source))
        {
            var openParen = source.IndexOf('(', match.Index);
            var call = ExtractCall(source, openParen);
            if (call == FieldKeywordSite)
                continue;

            var args = SplitTopLevelArguments(call[1..^1]);
            var valueArg = args[^1];
            if (IsSanitizedTransitively(source, valueArg, []))
                continue;

            unsanitized.Add(call);
        }
        return unsanitized;
    }

    [Fact]
    public void EveryInstrTextAndInstrSite_RoutesThroughSanitizeXmlText()
    {
        var source = DocxWriterSource();

        var unsanitized = FindUnsanitizedSites(source);

        unsanitized.Should().BeEmpty(
            "every w:t/w:delText/w:instrText element and w:instr attribute built from data must call " +
            "SanitizeXmlText, or XDocument.Save throws ArgumentException and aborts the whole save on an " +
            "XML-1.0-illegal character; found unsanitized site(s):\n" + string.Join("\n---\n", unsanitized));
    }

    [Fact]
    public void TheKnownSafeAllowlistEntry_StillMatchesSourceExactly()
    {
        // Guards the allowlist itself: if this line ever changes (a new argument, different formatting,
        // or -- worse -- a second, less-safe site reusing the same shape), the constant above goes stale
        // and silently stops exempting anything, which would make the main test above fail loudly rather
        // than the allowlist quietly widening to cover something unsafe.
        DocxWriterSource().Should().Contain(FieldKeywordSite);
    }

    [Fact]
    public void FindUnsanitizedSites_DetectsAHandWrittenComposerThatBypassesTheSanitizer()
    {
        // Meta-test: proves the scanner itself would have caught the round-162 bug (and would catch the
        // next one), rather than only ever seeing a clean file and never having exercised its failure path.
        const string vulnerable = """
            private static string Broken(string userText)
            {
                return new XElement(W + "instrText",
                    new XAttribute(XNamespace.Xml + "space", "preserve"), " TA \\l \"" + userText + "\"").ToString();
            }
            """;

        FindUnsanitizedSites(vulnerable).Should().NotBeEmpty();
    }
}
