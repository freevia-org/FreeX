using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class AccessibleDocumentProjectionTests
{
    [Fact]
    public void ProjectedTextUsesExactAddressesAndRetainsGlobalSeparators()
    {
        var shared = new Paragraph("cached");
        var document = new TextDocument();
        document.Blocks.Add(shared);
        var table = Table.Create(1, 2);
        foreach (var cell in table.Rows[0].Cells)
        {
            cell.Paragraphs.Clear();
            cell.Paragraphs.Add(shared);
        }
        document.Blocks.Add(table);
        var visits = new List<AccessibleDocumentLocation>();
        string Project(AccessibleDocumentLocation address, Paragraph paragraph)
        {
            paragraph.Should().BeSameAs(shared);
            visits.Add(address);
            return address.Region == AccessibleDocumentRegion.Body
                ? "Body" : $"Cell{address.Column}";
        }

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(document,
            AccessibleDocumentLocation.TableCell(1, 0, 1, 0, 4),
            AccessibleDocumentLocation.TableCell(1, 0, 1, 0, 1), Project);

        snapshot.Text.Should().Be("Body\nCell0\tCell1");
        snapshot.CaretOffset.Should().Be("Body\nCell0\tCell".Length);
        snapshot.GetText(snapshot.Selection!).Should().Be("ell");
        visits.Should().Equal(AccessibleDocumentLocation.Body(0, 0),
            AccessibleDocumentLocation.TableCell(1, 0, 0, 0, 0),
            AccessibleDocumentLocation.TableCell(1, 0, 1, 0, 0));
        shared.PlainText.Should().Be("cached");
    }

    [Fact]
    public void DefaultProjectionKeepsModelTextAndOffsets()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.ComplexFieldRun(" PAGE ", "OLD", showCode: true));
        paragraph.Runs.Add(new Run("suffix"));
        document.Blocks.Add(paragraph);

        var snapshot = AccessibleDocumentSnapshotPlanner.Build(document, AccessibleDocumentLocation.Body(0, 4));

        snapshot.Text.Should().Be("OLDsuffix");
        snapshot.CaretOffset.Should().Be(4);
    }
}
