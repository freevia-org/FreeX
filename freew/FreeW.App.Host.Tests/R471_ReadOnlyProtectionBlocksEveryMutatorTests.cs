using System.IO;
using System.Reflection;
using System.Text;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// r471, WPF side. The same census as the Avalonia suite, because the same defect was present here
/// and in a more pointed form: <c>InsertTableOfContents</c> and <c>InsertBibliography</c> already
/// refused under Restrict Editing while their <c>Refresh</c> counterparts did not - one path
/// guarded, its sibling left, inside a single file. <c>InsertPageNumberAtCaret</c> was unguarded
/// too, and is not exposed on the Avalonia surface at all, so only running the census on BOTH
/// toolkits found it.
/// </summary>
public sealed class R471_ReadOnlyProtectionBlocksEveryMutatorTests
{
    private static DocumentView CreateView()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Hello"));

        var view = new DocumentView();
        view.LoadModel(document);
        return view;
    }

    private static string Fingerprint(DocumentView view)
    {
        var sb = new StringBuilder();
        sb.Append(view.Model.Blocks.Count);
        foreach (var block in view.Model.Blocks)
            sb.Append('|').Append(block is Paragraph p ? p.PlainText : block.GetType().Name);
        return sb.ToString();
    }

    private static List<MethodInfo> ZeroArgumentMutators() =>
        typeof(DocumentView)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetParameters().Length == 0
                        && m.ReturnType == typeof(void)
                        && !m.IsSpecialName
                        && !m.Name.Contains("Protection", StringComparison.Ordinal)
                        && !m.Name.Contains("MarkedAsFinal", StringComparison.Ordinal))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

    private static (int attempted, List<string> mutated) Census(bool protect)
    {
        var mutated = new List<string>();
        var attempted = 0;

        foreach (var method in ZeroArgumentMutators())
        {
            var view = CreateView();
            if (protect)
                view.SetProtection(ProtectionMode.ReadOnly);

            var before = Fingerprint(view);
            attempted++;

            try
            {
                method.Invoke(view, null);
            }
            catch
            {
                continue;
            }

            if (!string.Equals(before, Fingerprint(view), StringComparison.Ordinal))
                mutated.Add(method.Name);
        }

        return (attempted, mutated);
    }

    [StaFact]
    public void NoPublicMutatorChangesAReadOnlyProtectedDocument()
    {
        var (attempted, mutated) = Census(protect: true);

        attempted.Should().BeGreaterThan(50, "the census must actually be exercising the surface");
        mutated.Should().BeEmpty(
            "a read-only document must not be rewritten by any command, matching Word's Restrict Editing");
    }

    [StaFact]
    public void TheCensusWouldNoticeAnUnguardedMutator()
    {
        // Instrument check: unprotected, these same methods do change the document.
        var (_, mutated) = Census(protect: false);

        mutated.Should().NotBeEmpty(
            "if nothing mutates even unprotected, the census is inert and its clean result is meaningless");
    }
}
