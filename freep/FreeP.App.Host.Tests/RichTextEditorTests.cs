using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.App.Rendering.Wpf;
using FreeP.Core.Model;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;
using ModelTableCell = FreeP.Core.Model.TableCell;
using ModelTableRow  = FreeP.Core.Model.TableRow;
using ModelHyperlink = FreeP.Core.Model.Hyperlink;
using WpfParagraph  = System.Windows.Documents.Paragraph;
using WpfRun        = System.Windows.Documents.Run;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Wave 10A: tests for per-run rich-text in-canvas editing.
///
/// Coverage:
///  1. TextBodyFlowDocumentConverter round-trip (pure logic, no live RichTextBox needed).
///  2. Applying bold to a sub-range produces correctly split runs on commit.
///  3. Host StaFact: InCanvasTextEditor activates over a multi-run shape without throwing.
///  4. SetShapeTextBodyCommand applies and reverts correctly.
/// </summary>
public sealed class RichTextEditorTests
{
    [StaFact]
    public void Converter_RendersListMarkersWithoutAddingThemToLogicalText()
    {
        var body = new TextBody();
        body.Paragraphs.Add(new ModelParagraph
        {
            BulletKind = BulletKind.Char,
            BulletChar = "•",
            Runs = { new ModelRun { Text = "Alpha" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            Runs = { new ModelRun { Text = "Beta" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            Runs = { new ModelRun { Text = "Gamma" } },
        });

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var paragraphs = document.Blocks.OfType<WpfParagraph>().ToArray();

        paragraphs.Select(paragraph =>
                paragraph.Inlines.OfType<InlineUIContainer>().Single().Child)
            .OfType<TextBlock>()
            .Select(marker => marker.Text)
            .Should()
            .Equal("• ", "1. ", "2. ");

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, body);
        InCanvasTextEditPlanner.ExtractPlainText(restored)
            .Should()
            .Be("Alpha\nBeta\nGamma");
        restored.Paragraphs.Select(paragraph => paragraph.BulletKind)
            .Should()
            .Equal(BulletKind.Char, BulletKind.Auto, BulletKind.Auto);
        restored.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Text)
            .Should()
            .Equal("Alpha", "Beta", "Gamma");
    }

    [StaFact]
    public void Converter_InheritsListStyleMarkersButHonorsExplicitSuppression()
    {
        var styles = new TextStyleLevels
        {
            [0] = new TextStyleLevel
            {
                BulletKind = BulletKind.Char,
                BulletChar = "§",
            },
            [1] = new TextStyleLevel
            {
                BulletKind = BulletKind.Auto,
                AutoNumType = AutoNumType.RomanUcPeriod,
            },
        };
        var body = new TextBody { LstStyle = styles };
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = "Inherited char" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            BulletSuppressed = true,
            Runs = { new ModelRun { Text = "Suppressed" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            Level = 1,
            Runs = { new ModelRun { Text = "Inherited number" } },
        });

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var markers = document.Blocks.OfType<WpfParagraph>()
            .SelectMany(paragraph => paragraph.Inlines.OfType<InlineUIContainer>())
            .Select(container => ((TextBlock)container.Child).Text)
            .ToArray();

        markers.Should().Equal("§ ", "I. ");
        document.Blocks.OfType<WpfParagraph>().ElementAt(1).Inlines
            .OfType<InlineUIContainer>().Should().BeEmpty();
        InCanvasTextEditPlanner.ExtractPlainText(
                TextBodyFlowDocumentConverter.FromFlowDocument(document, body))
            .Should().Be("Inherited char\nSuppressed\nInherited number");
    }

    [StaFact]
    public void Converter_InheritsListStyleParagraphLayoutButHonorsLocalOverrides()
    {
        var body = new TextBody
        {
            DefaultParaAlign = TextAlign.Left,
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel
                {
                    Align = TextAlign.Right,
                    MarginLeftEmu = 914400,
                    IndentEmu = -228600,
                },
            },
        };
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = "Inherited layout" } },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            Align = TextAlign.Center,
            MarginLeftEmu = 0,
            IndentEmu = 0,
            Runs = { new ModelRun { Text = "Local layout" } },
        });

        var paragraphs = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12)
            .Blocks.OfType<WpfParagraph>().ToArray();

        paragraphs[0].TextAlignment.Should().Be(TextAlignment.Right);
        paragraphs[0].Margin.Left.Should().BeApproximately(96, 0.01);
        paragraphs[0].TextIndent.Should().BeApproximately(-24, 0.01);
        paragraphs[1].TextAlignment.Should().Be(TextAlignment.Center);
        paragraphs[1].Margin.Left.Should().BeApproximately(0, 0.01);
        paragraphs[1].TextIndent.Should().BeApproximately(0, 0.01);
    }

    [StaFact]
    public void Converter_InheritsRunDefaultsAtParagraphScopeWithoutBakingThemIntoRuns()
    {
        var inheritedColor = new ThemeAwareColor(new SrgbColor(0x22, 0x66, 0xAA), alpha: 200);
        var body = new TextBody
        {
            LstStyle = new TextStyleLevels
            {
                [0] = new TextStyleLevel
                {
                    FontSizePt = 20,
                    Bold = true,
                    Italic = true,
                    LatinFont = "Arial",
                    Color = inheritedColor,
                },
            },
        };
        body.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = "Inherited run defaults" } },
        });

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var paragraph = document.Blocks.OfType<WpfParagraph>().Single();
        paragraph.FontSize.Should().BeApproximately(20 * 96 / 72.0, 0.01);
        paragraph.FontWeight.Should().Be(FontWeights.Bold);
        paragraph.FontStyle.Should().Be(FontStyles.Italic);
        paragraph.FontFamily.Source.Should().Be("Arial");
        ((SolidColorBrush)paragraph.Foreground).Color
            .Should().Be(System.Windows.Media.Color.FromArgb(200, 0x22, 0x66, 0xAA));

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, body);
        var run = restored.Paragraphs.Single().Runs.Single();
        run.FontSizePt.Should().BeNull();
        run.BoldSet.Should().BeFalse();
        run.ItalicSet.Should().BeFalse();
        run.Color.Should().BeNull();
    }

    [StaFact]
    public void WpfEnterSplit_PreservesListMetadataOnBothResultParagraphs()
    {
        var original = new TextBody();
        original.Paragraphs.Add(new ModelParagraph
        {
            Level = 2,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.AlphaLcParenBoth,
            AutoNumStartAt = 3,
            AutoNumStartAtSpecified = true,
            Runs = { new ModelRun { Text = "AlphaBeta" } },
        });

        // This is the FlowDocument state produced by WPF after Enter splits one
        // list paragraph into two paragraphs. The list marker is paragraph metadata,
        // not editable text, so the original body is the authority for the style.
        var edited = new FlowDocument();
        edited.Blocks.Add(new WpfParagraph { Inlines = { new WpfRun("Alpha") } });
        edited.Blocks.Add(new WpfParagraph { Inlines = { new WpfRun("Beta") } });

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(edited, original);

        restored.Paragraphs.Should().HaveCount(2);
        restored.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Level == 2
            && paragraph.BulletKind == BulletKind.Auto
            && paragraph.AutoNumType == AutoNumType.AlphaLcParenBoth
            && paragraph.AutoNumStartAt == 3);
        restored.Paragraphs[0].AutoNumStartAtSpecified.Should().BeTrue();
        restored.Paragraphs[1].AutoNumStartAtSpecified.Should().BeFalse();
        ComposeText(restored).Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("(c)", "(d)");
    }

    [StaFact]
    public void WpfExplicitRestartAfterNonList_UsesSharedMarkerContinuation()
    {
        var original = new TextBody();
        var first = ModelParagraph("First", BulletKind.Auto, 0, 0, 0, null);
        first.AutoNumType = AutoNumType.ArabicPeriod;
        first.AutoNumStartAt = 4;
        first.AutoNumStartAtSpecified = true;
        original.Paragraphs.Add(first);
        original.Paragraphs.Add(ModelParagraph("Plain", BulletKind.None, 0, 0, 0, null));
        var restart = ModelParagraph("Restart", BulletKind.Auto, 0, 0, 0, null);
        restart.AutoNumType = AutoNumType.ArabicPeriod;
        restart.AutoNumStartAt = 7;
        restart.AutoNumStartAtSpecified = true;
        original.Paragraphs.Add(restart);
        var after = ModelParagraph("After", BulletKind.Auto, 0, 0, 0, null);
        after.AutoNumType = AutoNumType.ArabicPeriod;
        original.Paragraphs.Add(after);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("First", "Plain", "Restart", "After"),
            original);

        ComposeText(restored).Paragraphs.Select(paragraph => paragraph.BulletText)
            .Should().Equal("4.", string.Empty, "7.", "8.");
    }

    [StaFact]
    public void WpfTableCellRichEditor_RoundTripsMixedRunsAndKeepsSelectionCaretOnRichText()
    {
        var original = new TextBody();
        original.Paragraphs.Add(new ModelParagraph
        {
            Align = TextAlign.Center,
            Runs =
            {
                new ModelRun { Text = "Bold", Bold = true, BoldSet = true },
                new ModelRun { Text = " italic", Italic = true, ItalicSet = true },
            },
        });
        original.Paragraphs.Add(new ModelParagraph
        {
            Runs = { new ModelRun { Text = "Second", Underline = true } },
        });

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(original, fallbackFontSizePt: 13);
        var editor = new RichTextBox(document)
        {
            Width = 320,
            Height = 120,
            IsUndoEnabled = false,
        };
        editor.Measure(new System.Windows.Size(320, 120));
        editor.Arrange(new Rect(0, 0, 320, 120));

        var firstRun = document.Blocks.OfType<WpfParagraph>().First().Inlines
            .OfType<WpfRun>().First();
        var secondRun = document.Blocks.OfType<WpfParagraph>().First().Inlines
            .OfType<WpfRun>().Skip(1).First();
        var selectionStart = firstRun.ContentStart.GetPositionAtOffset(1)!;
        var selectionEnd = secondRun.ContentStart.GetPositionAtOffset(3)!;
        editor.Selection.Select(selectionStart, selectionEnd);

        editor.Selection.Text.Should().Be("old it");
        editor.Selection.Start.GetCharacterRect(LogicalDirection.Forward).Height
            .Should().BeGreaterThan(0);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, original);
        restored.Paragraphs.Should().HaveCount(2);
        restored.Paragraphs[0].Runs.Select(run => (run.Text, run.Bold, run.Italic))
            .Should().Equal(("Bold", true, false), (" italic", false, true));
        restored.Paragraphs[1].Runs.Single().Underline.Should().BeTrue();
    }

    [StaFact]
    public void WpfInlineTableEditor_PreservesNestedCellTableWhenTextIsUnchanged()
    {
        var inner = new TableShape();
        inner.ColumnWidthsEmu.Add(457200);
        inner.Rows.Add(new ModelTableRow
        {
            HeightEmu = 228600,
            Cells =
            {
                new ModelTableCell
                {
                    TextBody = new TextBody
                    {
                        Paragraphs =
                        {
                            new ModelParagraph { Runs = { new ModelRun { Text = "Nested" } } },
                        },
                    },
                },
            },
        });

        var outer = new TableShape();
        outer.ColumnWidthsEmu.Add(457200);
        outer.Rows.Add(new ModelTableRow
        {
            HeightEmu = 228600,
            Cells =
            {
                new ModelTableCell
                {
                    TextBody = new TextBody
                    {
                        Paragraphs =
                        {
                            new ModelParagraph
                            {
                                Runs =
                                {
                                    new ModelRun
                                    {
                                        Text = "\uFFFC",
                                        InlineTable = new InlineTableInfo { Table = inner },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        });

        var body = new TextBody
        {
            Paragraphs =
            {
                new ModelParagraph
                {
                    Runs =
                    {
                        new ModelRun { Text = "Before " },
                        new ModelRun { Text = "\uFFFC", InlineTable = new InlineTableInfo { Table = outer } },
                        new ModelRun { Text = " After" },
                    },
                },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, body);
        var restoredTable = restored.Paragraphs[0].Runs[1].InlineTable;

        restoredTable.Should().NotBeNull();
        restoredTable!.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0]
            .InlineTable!.Table.Rows[0].Cells[0].TextBody!.Paragraphs[0].Runs[0].Text
            .Should().Be("Nested");
    }

    [StaFact]
    public void WpfInlineTableEditor_ConsumesRowHorizontalAlignment()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([457200, 457200]);
        table.Rows.Add(new ModelTableRow
        {
            HorizontalAlignment = TableRowHorizontalAlignment.Center,
            Cells = { new ModelTableCell { TextBody = new TextBody
            {
                Paragraphs = { new ModelParagraph { Runs = { new ModelRun { Text = "Centered" } } } },
            } } },
        });

        var body = new TextBody
        {
            Paragraphs =
            {
                new ModelParagraph
                {
                    Runs = { new ModelRun { Text = "\uFFFC", InlineTable = new InlineTableInfo { Table = table } } },
                },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var grid = document.Blocks.OfType<WpfParagraph>().Single()
            .Inlines.OfType<InlineUIContainer>().Single().Child.Should().BeOfType<Grid>().Subject;

        grid.HorizontalAlignment.Should().Be(HorizontalAlignment.Center);
        grid.Children.OfType<TextBox>().Single().RenderTransform
            .Should().BeOfType<TranslateTransform>().Which.X.Should().Be(24);
    }

    [StaFact]
    public void WpfInlineTableEditor_TabMovesAcrossCellsAndAppendsMatchingRow()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([457200, 457200]);
        table.Rows.Add(new ModelTableRow
        {
            HeightEmu = 304800,
            Cells =
            {
                new ModelTableCell { TextBody = new TextBody() },
                new ModelTableCell { TextBody = new TextBody() },
            },
        });
        var info = new InlineTableInfo { Table = table };
        var body = new TextBody
        {
            Paragraphs =
            {
                new ModelParagraph
                {
                    Runs = { new ModelRun { Text = "\uFFFC", InlineTable = info } },
                },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var grid = document.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<InlineUIContainer>().Single().Child.Should().BeOfType<Grid>().Subject;
        var originalCells = grid.Children.OfType<TextBox>().ToList();
        var editorInfo = grid.Tag.Should().BeOfType<InlineTableInfo>().Subject;

        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, originalCells[0], backwards: false).Should().BeTrue();
        editorInfo.Table.Rows.Should().HaveCount(1);

        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, originalCells[1], backwards: false).Should().BeTrue();
        editorInfo.Table.Rows.Should().HaveCount(2);
        grid.RowDefinitions.Should().HaveCount(2);

        var newRowCells = grid.Children.OfType<TextBox>().Skip(2).ToList();
        newRowCells.Should().HaveCount(2);
        newRowCells.Select(cell => cell.Text).Should().AllBeEquivalentTo(string.Empty);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, body);
        restored.Paragraphs[0].Runs.Single().InlineTable!.Table.Rows.Should().HaveCount(2);
    }

    [StaFact]
    public void WpfInlineTableEditor_ShiftTabAtFirstCellStaysInsideTable()
    {
        var table = new TableShape();
        table.ColumnWidthsEmu.Add(457200);
        table.Rows.Add(new ModelTableRow
        {
            Cells = { new ModelTableCell { TextBody = new TextBody() } },
        });
        var info = new InlineTableInfo { Table = table };
        var body = new TextBody
        {
            Paragraphs =
            {
                new ModelParagraph
                {
                    Runs = { new ModelRun { Text = "\uFFFC", InlineTable = info } },
                },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var grid = document.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<InlineUIContainer>().Single().Child.Should().BeOfType<Grid>().Subject;
        var cell = grid.Children.OfType<TextBox>().Single();
        var editorInfo = grid.Tag.Should().BeOfType<InlineTableInfo>().Subject;

        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, cell, backwards: true).Should().BeTrue();
        editorInfo.Table.Rows.Should().HaveCount(1);
        grid.Children.OfType<TextBox>().Should().ContainSingle();
    }

    [StaFact]
    public void WpfInlineTableEditor_SkipsMergedCellsAndCommitsCompactSourceCell()
    {
        TextBody Body(string text) => new()
        {
            Paragraphs =
            {
                new ModelParagraph { Runs = { new ModelRun { Text = text } } },
            },
        };

        var table = new TableShape();
        table.ColumnWidthsEmu.AddRange([457200, 457200, 457200]);
        table.Rows.Add(new ModelTableRow
        {
            Cells =
            {
                new ModelTableCell { GridSpan = 2, RowSpan = 2, TextBody = Body("Anchor") },
                new ModelTableCell { HMerge = true },
                new ModelTableCell { TextBody = Body("Top right") },
            },
        });
        table.Rows.Add(new ModelTableRow
        {
            Cells =
            {
                new ModelTableCell { VMerge = true },
                new ModelTableCell { VMerge = true },
                new ModelTableCell { TextBody = Body("Bottom right") },
            },
        });
        var info = new InlineTableInfo { Table = table };
        var body = new TextBody
        {
            Paragraphs =
            {
                new ModelParagraph
                {
                    Runs = { new ModelRun { Text = "\uFFFC", InlineTable = info } },
                },
            },
        };

        var document = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var grid = document.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<InlineUIContainer>().Single().Child.Should().BeOfType<Grid>().Subject;
        var cells = grid.Children.OfType<TextBox>().ToList();

        cells.Should().HaveCount(3);
        cells.Select(cell => (Grid.GetRow(cell), Grid.GetColumn(cell)))
            .Should().Equal((0, 0), (0, 2), (1, 2));

        cells[2].Text = "Edited bottom right";
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(document, body);
        InCanvasTextEditPlanner.ExtractPlainText(
                restored.Paragraphs.Single().Runs.Single().InlineTable!.Table.Rows[0].Cells[2].TextBody)
            .Should().Be("Top right");
        InCanvasTextEditPlanner.ExtractPlainText(
                restored.Paragraphs.Single().Runs.Single().InlineTable!.Table.Rows[1].Cells[2].TextBody)
            .Should().Be("Edited bottom right");

        var editorInfo = grid.Tag.Should().BeOfType<InlineTableInfo>().Subject;
        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, cells[2], backwards: true).Should().BeTrue();
        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, cells[0], backwards: true).Should().BeTrue();
        TextBodyFlowDocumentConverter.TryNavigateInlineTableCell(
            grid, editorInfo, cells[2], backwards: false).Should().BeTrue();
        editorInfo.Table.Rows.Should().HaveCount(3);
        grid.Children.OfType<TextBox>().Should().HaveCount(6);
        grid.Children.OfType<TextBox>().Last().Text.Should().BeEmpty();
    }

    [StaFact]
    public void WpfSplitFirstParagraph_UsesTextLineageForFollowingMetadata()
    {
        var original = DistinctParagraphBody();
        var edited = FlowDocumentFor("A1", "A2", "B", "C");

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(edited, original);

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfSplitMiddleParagraph_UsesTextLineageForTrailingMetadata()
    {
        var original = DistinctParagraphBody();
        var edited = FlowDocumentFor("A", "B1", "B2", "C");

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(edited, original);

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfDuplicateParagraphTexts_ConsumeDistinctOrderedSources()
    {
        var original = new TextBody();
        original.Paragraphs.Add(ModelParagraph("foo", BulletKind.Char, 1, 10, 100, "*"));
        original.Paragraphs.Add(ModelParagraph("foo", BulletKind.Auto, 2, 20, 200, null));

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("foo", "foo"),
            original);

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Auto, 2, 20, 200);
    }

    [StaFact]
    public void WpfEmptySplitParagraph_UsesTheSourceBeforeTheAnchor()
    {
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("", "A", "B", "C"),
            DistinctParagraphBody());

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfRewrittenSplitBeforeTrailingAnchors_UsesUnmatchedSourceLineage()
    {
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("X", "Y", "B", "C"),
            DistinctParagraphBody());

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfAnchorlessRewrite_EqualCount_UsesOrderedSourceLineage()
    {
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("X", "Y", "Z"),
            DistinctParagraphBody());

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfAnchorlessRewriteWithSplit_AssignsSurplusToLeadingLineage()
    {
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("X", "Y", "Z", "W"),
            DistinctParagraphBody());

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 2, BulletKind.Auto, 2, 20, 200);
        AssertMetadata(restored.Paragraphs, 3, BulletKind.None, 3, 30, 300);
    }

    [StaFact]
    public void WpfAnchorlessJoin_RetainsOrderedLeadingLineage()
    {
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            FlowDocumentFor("X", "Y"),
            DistinctParagraphBody());

        AssertMetadata(restored.Paragraphs, 0, BulletKind.Char, 1, 10, 100);
        AssertMetadata(restored.Paragraphs, 1, BulletKind.Auto, 2, 20, 200);
    }

    [StaFact]
    public void WpfAuthority_UsesSharedRichEditorFallbackTypography()
    {
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(
            new TextBody(),
            InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt);

        doc.FontFamily.Source.Should().Be(InCanvasRichTextEditorDefaults.FallbackFontFamily);
        doc.FontSize.Should().BeApproximately(
            InCanvasRichTextEditorDefaults.TableCellFallbackFontSizePt * 96.0 / 72.0,
            0.01);
    }

    [StaFact]
    public void WpfAuthority_UsesSharedBodyWrapPolicy()
    {
        var wrappedBody = new TextBody { Wrap = true };
        var wrapped = TextBodyFlowDocumentConverter.ToFlowDocument(wrappedBody);
        var unwrapped = TextBodyFlowDocumentConverter.ToFlowDocument(
            new TextBody { Wrap = false });

        wrapped.PageWidth.Should().BeNaN();
        wrapped.ColumnWidth.Should().BeNaN();
        unwrapped.PageWidth.Should().Be(100_000);
        unwrapped.ColumnWidth.Should().Be(100_000);

        TextBodyFlowDocumentConverter.FromFlowDocument(wrapped, wrappedBody)
            .Wrap.Should().BeTrue();
        TextBodyFlowDocumentConverter.FromFlowDocument(unwrapped, new TextBody { Wrap = false })
            .Wrap.Should().BeFalse();
    }

    [StaFact]
    public void WpfAuthority_RendersAlignmentAndMixedRuns_WithDisplayOnlyBulletMarkers()
    {
        var body = MakeVisualEvidenceBody();
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 11);
        doc.PageWidth = 416;
        doc.ColumnWidth = 416;

        var paragraphs = doc.Blocks.OfType<WpfParagraph>().ToArray();
        paragraphs.Should().HaveCount(2);
        paragraphs[0].TextAlignment.Should().Be(TextAlignment.Left);
        paragraphs[1].TextAlignment.Should().Be(TextAlignment.Center);
        paragraphs.Select(paragraph =>
                paragraph.Inlines.OfType<InlineUIContainer>().Single().Child)
            .OfType<TextBlock>()
            .Select(marker => marker.Text)
            .Should()
            .Equal("• ", "1. ");

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        InCanvasTextEditPlanner.ExtractPlainText(restored)
            .Should().Be(InCanvasTextEditPlanner.ExtractPlainText(body),
                "bullet metadata must not enter editable model text");
        restored.Paragraphs[0].BulletKind.Should().Be(BulletKind.Char);
        restored.Paragraphs[1].BulletKind.Should().Be(BulletKind.Auto);
    }

    [StaFact]
    public void Converter_RunHyperlinks_RoundTripExternalAndInternalTargets()
    {
        var body = new TextBody();
        var paragraph = new ModelParagraph { Align = TextAlign.Left };
        paragraph.Runs.Add(new ModelRun
        {
            Text = "web",
            Hyperlink = new ModelHyperlink
            {
                Url = "https://example.test/path",
                Tooltip = "Open web",
            },
        });
        paragraph.Runs.Add(new ModelRun
        {
            Text = "slide",
            Hyperlink = new ModelHyperlink
            {
                TargetSlideId = "slide-2",
                Tooltip = "Go slide",
            },
        });
        body.Paragraphs.Add(paragraph);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(
            TextBodyFlowDocumentConverter.ToFlowDocument(body),
            body);

        restored.Paragraphs[0].Runs.Should().HaveCount(2);
        restored.Paragraphs[0].Runs[0].Hyperlink.Should().BeEquivalentTo(
            body.Paragraphs[0].Runs[0].Hyperlink);
        restored.Paragraphs[0].Runs[1].Hyperlink.Should().BeEquivalentTo(
            body.Paragraphs[0].Runs[1].Hyperlink);
    }

    [StaFact]
    public void WpfAuthority_RendersAndRoundTripsSuperscriptAndSubscriptRuns()
    {
        var body = new TextBody();
        var paragraph = new ModelParagraph { Align = TextAlign.Left };
        paragraph.Runs.Add(new ModelRun { Text = "x", BaselineOffset = 30000 });
        paragraph.Runs.Add(new ModelRun { Text = "2", BaselineOffset = -25000 });
        paragraph.Runs.Add(new ModelRun { Text = " + y" });
        body.Paragraphs.Add(paragraph);

        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var runs = doc.Blocks.OfType<WpfParagraph>().Single().Inlines
            .OfType<WpfRun>()
            .ToArray();

        runs[0].BaselineAlignment.Should().Be(BaselineAlignment.Superscript);
        runs[1].BaselineAlignment.Should().Be(BaselineAlignment.Subscript);
        runs[2].BaselineAlignment.Should().Be(BaselineAlignment.Baseline);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        restored.Paragraphs[0].Runs.Select(run => run.BaselineOffset)
            .Should().Equal(30000, -25000, null);
    }

    [StaFact]
    public void WpfAuthority_NewBaselineEditsUseCanonicalSignFallbacks()
    {
        var doc = new FlowDocument();
        var paragraph = new WpfParagraph();
        paragraph.Inlines.Add(new WpfRun("up") { BaselineAlignment = BaselineAlignment.Superscript });
        paragraph.Inlines.Add(new WpfRun("down") { BaselineAlignment = BaselineAlignment.Subscript });
        paragraph.Inlines.Add(new WpfRun("normal"));
        doc.Blocks.Add(paragraph);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);
        restored.Paragraphs[0].Runs.Select(run => run.BaselineOffset)
            .Should().Equal(10000, -10000, null);
    }

    [StaFact]
    public void WpfAuthority_ProducesNonblankPairedSelectionCaretAndParagraphEvidence()
    {
        var body = MakeVisualEvidenceBody();
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 11);
        doc.PageWidth = 416;
        doc.ColumnWidth = 416;
        var box = new RichTextBox(doc)
        {
            Width = 420,
            Height = 180,
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1.5),
            IsInactiveSelectionHighlightEnabled = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        var window = new Window
        {
            Width = 420,
            Height = 180,
            Content = box,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = -10_000,
            Top = -10_000,
        };
        window.Show();
        window.Activate();
        box.Focus();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var runs = doc.Blocks.OfType<WpfParagraph>()
            .SelectMany(paragraph => TextBodyFlowDocumentConverter.EnumerateLeafInlines(paragraph.Inlines))
            .OfType<WpfRun>()
            .ToArray();
        runs.Should().HaveCount(3);
        var selectionStart = runs[0].ContentStart.GetPositionAtOffset(2)!;
        var selectionEnd = runs[1].ContentStart.GetPositionAtOffset(5)!;
        box.Selection.Select(selectionStart, selectionEnd);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        Rect selectionStartRect = selectionStart.GetCharacterRect(LogicalDirection.Forward);
        Rect selectionEndRect = selectionEnd.GetCharacterRect(LogicalDirection.Forward);
        Rect largeRunCaret = runs[1].ContentStart.GetPositionAtOffset(1)!
            .GetCharacterRect(LogicalDirection.Forward);
        selectionEndRect.X.Should().BeGreaterThan(selectionStartRect.X);
        largeRunCaret.Height.Should().BeGreaterThan(25,
            "WPF authority caret geometry follows the 28pt run");

        try
        {
            string path = SaveEvidence(box, "wpf-rich-editor-selection.png");
            new FileInfo(path).Length.Should().BeGreaterThan(1_000);

            box.Selection.Select(
                runs[1].ContentStart.GetPositionAtOffset(1)!,
                runs[1].ContentStart.GetPositionAtOffset(1)!);
            box.Focus();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            string caretPath = SaveEvidence(box, "wpf-rich-editor-caret.png");
            new FileInfo(caretPath).Length.Should().BeGreaterThan(1_000);
        }
        finally
        {
            window.Close();
        }
    }

    // ─── TextBody → FlowDocument → TextBody round-trips ──────────────────────────

    [StaFact]
    public void Converter_TwoRunBody_RoundTrips_BoldAndColor()
    {
        // Arrange: TextBody with two runs, one bold+red, one plain.
        var body = MakeTwoRunBody();

        // Act: ToFlowDocument then FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: paragraph count preserved.
        restored.Paragraphs.Should().HaveCount(1);

        var para = restored.Paragraphs[0];
        para.Runs.Should().HaveCount(2);

        // Run 0: bold red "Hello".
        var r0 = para.Runs[0];
        r0.Text.Should().Be("Hello");
        r0.Bold.Should().BeTrue("run 0 is bold");
        r0.Color.Should().NotBeNull("run 0 has an explicit color");
        r0.Color!.Resolved.R.Should().BeGreaterThan(200, "red channel is high (was 0xFF)");

        // Run 1: plain " world".
        var r1 = para.Runs[1];
        r1.Text.Should().Be(" world");
        r1.Bold.Should().BeFalse("run 1 is not bold");
    }

    [StaFact]
    public void Converter_TwoRunBody_RoundTrips_FontSizePreserved()
    {
        var body = MakeTwoRunBody();
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r0 = restored.Paragraphs[0].Runs[0];
        r0.FontSizePt.Should().NotBeNull();
        r0.FontSizePt!.Value.Should().BeApproximately(24.0, 0.1, "first run was 24pt");
    }

    [StaFact]
    public void Converter_MultiParagraph_PreservesAlignmentAndRunCount()
    {
        // Two paragraphs: first left-aligned (2 runs), second center-aligned (1 run).
        var body = new TextBody { Wrap = true };

        var p0 = new ModelParagraph { Align = TextAlign.Left };
        p0.Runs.Add(new ModelRun { Text = "Left ", Bold = true });
        p0.Runs.Add(new ModelRun { Text = "para" });
        body.Paragraphs.Add(p0);

        var p1 = new ModelParagraph { Align = TextAlign.Center };
        p1.Runs.Add(new ModelRun { Text = "Centered", Italic = true });
        body.Paragraphs.Add(p1);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        restored.Paragraphs.Should().HaveCount(2);

        restored.Paragraphs[0].Align.Should().Be(TextAlign.Left);
        restored.Paragraphs[0].Runs.Should().HaveCount(2);
        restored.Paragraphs[0].Runs[0].Bold.Should().BeTrue();
        restored.Paragraphs[0].Runs[1].Bold.Should().BeFalse();

        restored.Paragraphs[1].Align.Should().Be(TextAlign.Center);
        restored.Paragraphs[1].Runs[0].Italic.Should().BeTrue();
    }

    [StaFact]
    public void Converter_EmptyBody_ReturnsOneParagraphOneEmptyRun()
    {
        var body     = new TextBody();
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs[0].Text.Should().BeEmpty();
    }

    [StaFact]
    public void Converter_NullBody_ReturnsOneParagraphOneEmptyRun()
    {
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(null);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs[0].Text.Should().BeEmpty();
    }

    // ─── Bold applied to a sub-range splits runs correctly on commit ──────────

    [StaFact]
    public void BoldSubRangeApplication_SplitsRuns_OnDocumentRead()
    {
        // Simulate: original body has one run "Hello world".
        // After editing the user bold-selects "world". The RichTextBox splits the run.
        // We verify FromFlowDocument reads the resulting multi-run paragraph correctly.

        var doc = new FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        // Normal run.
        var wr1 = new System.Windows.Documents.Run("Hello ") { FontWeight = FontWeights.Normal };
        // Bold run (simulates the user bolding "world").
        var wr2 = new System.Windows.Documents.Run("world") { FontWeight = FontWeights.Bold };
        wp.Inlines.Add(wr1);
        wp.Inlines.Add(wr2);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        restored.Paragraphs.Should().HaveCount(1);
        restored.Paragraphs[0].Runs.Should().HaveCount(2, "two WPF runs produce two model runs");

        restored.Paragraphs[0].Runs[0].Text.Should().Be("Hello ");
        restored.Paragraphs[0].Runs[0].Bold.Should().BeFalse();

        restored.Paragraphs[0].Runs[1].Text.Should().Be("world");
        restored.Paragraphs[0].Runs[1].Bold.Should().BeTrue();
    }

    // ─── SetShapeTextBodyCommand apply/revert ─────────────────────────────────

    [Fact]
    public void SetShapeTextBodyCommand_Apply_Revert_RestoresOriginalBody()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var original = MakeTwoRunBody();
        var shape = new SlideShape
        {
            Id          = 1,
            ExtentCxEmu = 914400L,
            ExtentCyEmu = 914400L,
            TextBody    = original,
        };
        slide.Shapes.Add(shape);

        // New body: single run plain text.
        var newBody = new TextBody { Wrap = true };
        var np = new ModelParagraph();
        np.Runs.Add(new ModelRun { Text = "Replaced" });
        newBody.Paragraphs.Add(np);

        var bus    = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);

        bus.Execute(new SetShapeTextBodyCommand(0, 1, newBody));

        shape.TextBody!.Paragraphs.Should().HaveCount(1);
        shape.TextBody.Paragraphs[0].Runs[0].Text.Should().Be("Replaced");
        editor.CanUndo.Should().BeTrue();

        editor.Undo();

        shape.TextBody!.Paragraphs.Should().HaveCount(1, "original had 1 paragraph");
        shape.TextBody.Paragraphs[0].Runs.Should().HaveCount(2, "original had 2 runs");
        shape.TextBody.Paragraphs[0].Runs[0].Bold.Should().BeTrue("run 0 was bold");
    }

    // ─── InCanvasTextEditor activates over multi-run shape without throwing ──

    [StaFact]
    public void InCanvasTextEditor_ActivateOverMultiRunShape_DoesNotThrow()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        // Set a valid transform on the canvas (simulates a rendered state).
        // We set Presentation so CurrentTransform is computed during render;
        // but since we haven't rendered, CurrentTransform is Identity which is fine.
        canvas.Presentation = p;
        canvas.Slide = slide;

        var act = () => canvas.TextEditor!.Activate(shape.Id);
        act.Should().NotThrow("Activate over a multi-run shape should succeed");

        canvas.TextEditor!.IsActive.Should().BeTrue();
        canvas.TextEditor.ActiveShapeId.Should().Be(shape.Id);
        canvas.TextEditor.TrySelectTextRange(1, 7).Should().BeTrue();
        canvas.TextEditor.SelectedText.Should().Be("ello w");

        var box = overlay.Children
            .OfType<System.Windows.Controls.RichTextBox>()
            .Single();
        box.Width.Should().BeApproximately(288, 0.1);
        box.Height.Should().BeApproximately(144, 0.1);
    }

    // ─── Round 133 remediation: in-place Copy/Cut surfaces OS-clipboard write failures ──
    //
    // WpfRichTextClipboardAdapter.TryCopy/TryCut is the single OS-clipboard write call shared by
    // both InCanvasTextEditor (shape text, OnRichBoxPreviewKeyDown) and InCanvasTableCellEditor
    // (table-cell text, OnCellTextBoxPreviewKeyDown). Before this fix, a failed
    // Clipboard.SetDataObject call was swallowed -- TryCopy/TryCut just returned false with
    // nothing for the caller to inspect, so the user believed the in-place copy succeeded and
    // later pasted stale content. SetDataObjectForTests forces the write to fail deterministically
    // without touching (and potentially leaving locked) the real OS clipboard on this shared
    // interactive machine.

    [StaFact]
    public void WpfRichTextClipboardAdapter_TryCopy_ClipboardWriteFailure_ReportsErrorMessage()
    {
        var body = MakeTwoRunBody();
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var box = new RichTextBox(doc);
        box.SelectAll();

        WpfRichTextClipboardAdapter.SetDataObjectForTests =
            _ => throw new System.Runtime.InteropServices.COMException("clipboard locked");
        try
        {
            var result = WpfRichTextClipboardAdapter.TryCopy(box, body, out var error);

            result.Should().BeFalse();
            error.Should().Be("clipboard locked");
        }
        finally
        {
            WpfRichTextClipboardAdapter.SetDataObjectForTests = null;
        }
    }

    [StaFact]
    public void WpfRichTextClipboardAdapter_TryCut_ClipboardWriteFailure_ReportsErrorAndPreservesText()
    {
        var body = MakeTwoRunBody();
        var doc = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var box = new RichTextBox(doc);
        box.SelectAll();
        var originalText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;

        WpfRichTextClipboardAdapter.SetDataObjectForTests =
            _ => throw new System.Runtime.InteropServices.COMException("clipboard locked");
        try
        {
            var result = WpfRichTextClipboardAdapter.TryCut(box, body, out var error);

            result.Should().BeFalse();
            error.Should().Be("clipboard locked");
            new TextRange(doc.ContentStart, doc.ContentEnd).Text.Should().Be(
                originalText,
                "a failed cut must not delete the selection -- the user would lose the text with no copy to paste back");
        }
        finally
        {
            WpfRichTextClipboardAdapter.SetDataObjectForTests = null;
        }
    }

    [StaFact]
    public void InCanvasTextEditor_RotatedShape_TransformsOverlayAndPersistsTypedText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            RotationDeg = 30,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(shape.Id);

        var box = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        var transform = box.RenderTransform.Should().BeOfType<TransformGroup>().Subject;
        transform.Children.OfType<RotateTransform>().Should().ContainSingle()
            .Which.Angle.Should().BeApproximately(30, 0.001);
        transform.Children.OfType<RotateTransform>().Single().CenterX.Should().BeApproximately(144, 0.1);
        transform.Children.OfType<RotateTransform>().Single().CenterY.Should().BeApproximately(72, 0.1);

        canvas.TextEditor.TrySelectTextRange(0, 5).Should().BeTrue();
        canvas.TextEditor.SelectedText.Should().Be("Hello");
        box.Selection.Text = "Edited";
        canvas.TextEditor.Commit();

        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Edited world");
        shape.RotationDeg.Should().BeApproximately(30, 0.001);
        editor.CanUndo.Should().BeTrue();
    }

    [StaFact]
    public void InCanvasTextEditor_RotatedShape_CancelDoesNotCommitOnLostFocus()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            RotationDeg = 30,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(shape.Id);
        var box = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        box.Selection.Text = "Discarded";
        canvas.TextEditor.Cancel();

        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");
        canvas.ActiveTextEditShapeId.Should().BeNull();
        editor.CanUndo.Should().BeFalse();
    }

    [StaFact]
    public void InCanvasTextEditor_NestedChild_UsesSharedPathPlacementAndCommitCancel()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var child = new SlideShape
        {
            Id = 11,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            RotationDeg = 22,
            FlipV = true,
            TextBody = MakeTwoRunBody(),
        };
        var group = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        group.Children.Add(child);
        slide.Shapes.Add(group);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(child.Id);
        canvas.TextEditor.IsActive.Should().BeTrue();
        var box = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        box.RenderTransform.Should().BeOfType<TransformGroup>();
        box.Selection.Text = "Nested edited";
        canvas.TextEditor.Commit();
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Nested edited");

        editor.Undo();
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Hello world");

        canvas.TextEditor.Activate(child.Id);
        overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single().Selection.Text = "Discarded";
        canvas.TextEditor.Cancel();
        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody).Should().Be("Hello world");
    }

    [StaFact]
    public void InCanvasTextEditor_NestedChild_FormatsCrossParagraphSelectionThroughSharedPlanner()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var child = new SlideShape
        {
            Id = 12,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            TextBody = MakeMultiParagraphRichBody(),
        };
        var inner = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        inner.Children.Add(child);
        var outer = new SlideShape { Id = 9, Kind = SlideShapeKind.Group };
        outer.Children.Add(inner);
        slide.Shapes.Add(outer);

        var originalBounds = (child.OffsetXEmu, child.OffsetYEmu, child.ExtentCxEmu, child.ExtentCyEmu);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(child.Id);
        canvas.TextEditor.TrySelectTextRange(2, 10).Should().BeTrue();
        canvas.TextEditor.ApplyBold();
        canvas.TextEditor.ApplyItalic();
        canvas.TextEditor.ApplyUnderline();
        canvas.TextEditor.ApplyFont("Consolas");
        canvas.TextEditor.ApplyFontSize(20);
        canvas.TextEditor.ApplyColor(new ThemeAwareColor(new SrgbColor(0x22, 0x66, 0xAA)));
        canvas.TextEditor.Commit();

        var edited = child.TextBody!;
        InCanvasTextEditPlanner.ExtractPlainText(edited).Should().Be("Alpha Beta\nGamma Delta");
        edited.Paragraphs.SelectMany(p => p.Runs).Should().Contain(run =>
            run.Text.Contains("pha", StringComparison.Ordinal) &&
            run.Bold && run.Italic && run.Underline &&
            run.FontFamily == "Consolas" && run.FontSizePt == 20 &&
            run.Color != null && run.Color.Resolved == new SrgbColor(0x22, 0x66, 0xAA));
        (child.OffsetXEmu, child.OffsetYEmu, child.ExtentCxEmu, child.ExtentCyEmu)
            .Should().Be(originalBounds);

        editor.Undo();
        child.TextBody!.Paragraphs.SelectMany(p => p.Runs).Should().NotContain(run =>
            run.FontFamily == "Consolas" || run.FontSizePt == 20 || run.Underline);
        editor.Redo();
        child.TextBody!.Paragraphs.SelectMany(p => p.Runs).Should().Contain(run =>
            run.FontFamily == "Consolas" && run.FontSizePt == 20 && run.Underline);
        (child.OffsetXEmu, child.OffsetYEmu, child.ExtentCxEmu, child.ExtentCyEmu)
            .Should().Be(originalBounds);

        using var package = new MemoryStream();
        PptxPackageWriter.Write(presentation, package);
        package.Position = 0;
        var reopened = PptxPackageReader.Read(package);
        var reopenedChild = FreeP.App.Compositor.ShapeHitTester.FindShape(reopened.Slides[0], child.Id);
        reopenedChild.Should().NotBeNull();
        reopenedChild!.TextBody!.Paragraphs.SelectMany(p => p.Runs).Should().Contain(run =>
            run.FontFamily == "Consolas" && run.FontSizePt == 20 && run.Underline);
    }

    [StaFact]
    public void InCanvasTextEditor_NestedChild_SelectsLogicalRangeAcrossParagraphBoundary()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var child = new SlideShape
        {
            Id = 12,
            OffsetXEmu = 914400,
            OffsetYEmu = 457200,
            ExtentCxEmu = 1828800,
            ExtentCyEmu = 914400,
            RotationDeg = 22,
            FlipV = true,
            TextBody = MakeMultiParagraphRichBody(),
        };
        var inner = new SlideShape { Id = 10, Kind = SlideShapeKind.Group };
        inner.Children.Add(child);
        var outer = new SlideShape { Id = 9, Kind = SlideShapeKind.Group };
        outer.Children.Add(inner);
        slide.Shapes.Add(outer);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas { Presentation = presentation, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(child.Id);
        canvas.TextEditor.TrySelectTextRange(3, 13).Should().BeTrue();
        canvas.TextEditor.SelectedText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Should().Be("ha Beta\nGa");

        var box = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        box.Selection.Text = "X";
        canvas.TextEditor.Commit();

        InCanvasTextEditPlanner.ExtractPlainText(child.TextBody)
            .Should().Be("AlpXmma Delta");
        child.RotationDeg.Should().BeApproximately(22, 0.001);
        child.FlipV.Should().BeTrue();
    }

    [StaFact]
    public void InCanvasTextEditor_ActiveShapeSuppression_FollowsEditorLifecycle()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(p, new PresentationCommandBus(p));
        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);

        canvas.TextEditor!.Activate(shape.Id);
        canvas.ActiveTextEditShapeId.Should().Be(shape.Id);

        canvas.TextEditor.Cancel();
        canvas.ActiveTextEditShapeId.Should().BeNull();

        canvas.TextEditor.Activate(shape.Id);
        canvas.TextEditor.Commit();
        canvas.ActiveTextEditShapeId.Should().BeNull();

        canvas.TextEditor.Activate(shape.Id);
        canvas.TextEditor.Dispose();
        canvas.ActiveTextEditShapeId.Should().BeNull();
    }

    [StaFact]
    public void InCanvasTextEditor_CurrentSlideChange_CommitsTextAndClearsSuppression()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        p.Slides.Add(new Slide());
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(p, new PresentationCommandBus(p));
        var canvas = new SlideCanvas { Presentation = p, Slide = slide };
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.TextEditor!.Activate(shape.Id);

        var box = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        box.SelectAll();
        box.Selection.Text = "Committed before slide change";

        editor.SelectSlide(1);

        canvas.ActiveTextEditShapeId.Should().BeNull();
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody)
            .Should().Be("Committed before slide change");
        editor.CanUndo.Should().BeTrue();
    }

    [StaFact]
    public void InCanvasTextEditor_Commit_IssuesSetShapeTextBodyCommand()
    {
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var body = MakeTwoRunBody();
        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = body,
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.IsActive.Should().BeTrue();

        // Commit with an unchanged document should succeed without throwing.
        var act = () => canvas.TextEditor.Commit();
        act.Should().NotThrow();

        canvas.TextEditor.IsActive.Should().BeFalse("editor closes after commit");

        // An unchanged document produces no undo entry (CanUndo is still false at session start).
        editor.CanUndo.Should().BeFalse(
            "no command should be issued when the text body is unchanged");
    }

    // ─── Y1: inherited FontFamily/FontSizePt null must NOT be baked ─────────────

    [StaFact]
    public void InCanvasTextEditor_ApplyBoldToSelection_PreservesMixedRunsOnCommit()
    {
        var p = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var body = MakeTwoRunBody();
        body.Paragraphs[0].Runs[0].Bold = false;
        body.Paragraphs[0].Runs[0].BoldSet = false;
        body.Paragraphs[0].Runs[1].Italic = true;
        body.Paragraphs[0].Runs[1].ItalicSet = true;

        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = body,
        };
        slide.Shapes.Add(shape);

        var bus = new PresentationCommandBus(p);
        var editor = new EditingSession(p, bus);
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.ApplyBold();
        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue("formatting the active rich text selection should issue a command");
        var runs = shape.TextBody!.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);
        runs[0].Text.Should().Be("Hello");
        runs[1].Text.Should().Be(" world");
        runs.Should().OnlyContain(r => r.Bold);
        runs[1].Italic.Should().BeTrue("existing mixed-run italic formatting should survive");
    }

    [StaFact]
    public void InCanvasTextEditor_ShapeParagraphPreset_CommitsListMetadataAsOneEdit()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = presentation;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.TryApplyActiveShapeParagraphListPreset(
            TableCellListPresetCatalog.BulletSquare).Should().BeTrue();
        canvas.TextEditor.TryApplyActiveShapeParagraphIndent().Should().BeTrue();
        canvas.TextEditor.Commit();

        var paragraph = shape.TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Char);
        paragraph.BulletChar.Should().Be("▪");
        paragraph.BulletSuppressed.Should().BeFalse();
        paragraph.Level.Should().Be(1);
        editor.CanUndo.Should().BeTrue();
    }

    [StaFact]
    public void InCanvasTextEditor_ShapeParagraphNumberingToggle_UsesActiveShapeSelection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = presentation;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.TryApplyActiveShapeParagraphNumberingToggle().Should().BeTrue();
        canvas.TextEditor.Commit();

        var paragraph = shape.TextBody!.Paragraphs[0];
        paragraph.BulletKind.Should().Be(BulletKind.Auto);
        paragraph.AutoNumType.Should().Be(AutoNumType.ArabicPeriod);
        paragraph.BulletSuppressed.Should().BeFalse();
    }

    [StaFact]
    public void InCanvasTextEditor_TextAndFormattingStayLocalAndCommitAsOneUndoStep()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var shape = new SlideShape
        {
            Id = 1,
            OffsetXEmu = 0,
            OffsetYEmu = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody = MakeTwoRunBody(),
        };
        slide.Shapes.Add(shape);

        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var canvas = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = presentation;
        canvas.Slide = slide;

        canvas.TextEditor!.Activate(shape.Id);
        var richTextBox = overlay.Children.OfType<System.Windows.Controls.RichTextBox>().Single();
        richTextBox.SelectAll();
        richTextBox.Selection.Text = "One committed edit";
        richTextBox.SelectAll();
        canvas.TextEditor.ApplyBold();

        editor.CanUndo.Should().BeFalse("the active WPF rich edit is still local");
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");

        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue();
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("One committed edit");
        shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.Bold);

        editor.Undo();
        editor.CanUndo.Should().BeFalse("text and formatting must share one model command");
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody).Should().Be("Hello world");
        shape.TextBody!.Paragraphs[0].Runs.Should().HaveCount(2);
    }

    [StaFact]
    public void Converter_InheritedFontFamilyAndSize_RoundTrip_StillNull()
    {
        // Arrange: run with FontFamily=null and FontSizePt=null (inherit from placeholder).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Inherited",
            FontFamily = null,    // must stay null after round-trip
            FontSizePt = null,    // must stay null after round-trip
        });
        body.Paragraphs.Add(para);

        // Act: round-trip via ToFlowDocument → FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: inherited values must NOT have been baked in.
        var r = restored.Paragraphs[0].Runs[0];
        r.Text.Should().Be("Inherited");
        r.FontFamily.Should().BeNull("FontFamily=null must survive round-trip (not baked to 'Calibri')");
        r.FontSizePt.Should().BeNull("FontSizePt=null must survive round-trip (not baked to 14pt)");
    }

    // ─── Y2: inherited/scheme Color null must NOT be baked; SchemeColor ref must survive ──

    [StaFact]
    public void Converter_InheritedColor_RoundTrip_StillNull()
    {
        // Arrange: run with Color=null (inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "NoColor", Color = null });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r = restored.Paragraphs[0].Runs[0];
        r.Color.Should().BeNull("Color=null (inherit) must survive round-trip, not be baked to sRGB");
    }

    [StaFact]
    public void Converter_SchemeColor_RoundTrip_PreservesRef()
    {
        // Arrange: run with a SchemeColor (accent1) — the "theme slot" case.
        var schemeRef = new SchemeColorRef { Slot = ThemeColorSlot.Accent1, LumMod = 0.8, LumOff = 0.0 };
        var themeColor = new ThemeAwareColor(new SrgbColor(0x44, 0x72, 0xC4), schemeRef);

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "Themed", Color = themeColor });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 14);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var r = restored.Paragraphs[0].Runs[0];
        r.Color.Should().NotBeNull();
        r.Color!.SchemeColor.Should().NotBeNull(
            "SchemeColor ref must survive the round-trip (not be replaced by a plain sRGB)");
        r.Color.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1);
        r.Color.SchemeColor.LumMod.Should().BeApproximately(0.8, 1e-9);
    }

    // ─── Y1+Y2: no-op edit (convert and convert back) leaves TextBodiesEqual true ──

    [StaFact]
    public void Converter_NoOpEdit_InheritedRun_BodyUnchanged()
    {
        // Arrange: a body with a run that has all-null formatting (fully inherited).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Placeholder text",
            FontFamily = null,
            FontSizePt = null,
            Color      = null,
        });
        body.Paragraphs.Add(para);

        // Act: simulate a no-op edit (convert to doc, convert back with original body as reference).
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 18);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        // Assert: both runs have the same (null) inherited fields — bodies are "equal".
        // We verify the fields directly (TextBodiesEqual is private).
        var orig = body.Paragraphs[0].Runs[0];
        var rest = restored.Paragraphs[0].Runs[0];
        rest.Text.Should().Be(orig.Text);
        rest.FontFamily.Should().BeNull("FontFamily must stay null after no-op edit");
        rest.FontSizePt.Should().BeNull("FontSizePt must stay null after no-op edit");
        rest.Color.Should().BeNull("Color must stay null after no-op edit");
        rest.Bold.Should().Be(orig.Bold);
        rest.Italic.Should().Be(orig.Italic);
    }

    // ─── Y3: color-only change IS detected by TextBodiesEqual (via Commit path) ─

    [StaFact]
    public void InCanvasTextEditor_Commit_ColorOnlyChange_IssuesCommand()
    {
        // Arrange: shape with one red run.
        var p     = Presentation.CreateEmpty();
        var slide = p.Slides[0];
        slide.Shapes.Clear();

        var redColor  = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00));
        var blueColor = new ThemeAwareColor(new SrgbColor(0x00, 0x00, 0xFF));

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Red",
            FontFamily = "Calibri",
            FontSizePt = 14,
            Color      = redColor,
        });
        body.Paragraphs.Add(para);

        var shape = new SlideShape
        {
            Id          = 1,
            OffsetXEmu  = 0,
            OffsetYEmu  = 0,
            ExtentCxEmu = 2743200L,
            ExtentCyEmu = 1371600L,
            TextBody    = body,
        };
        slide.Shapes.Add(shape);

        var bus     = new PresentationCommandBus(p);
        var editor  = new EditingSession(p, bus);
        var canvas  = new SlideCanvas();
        var overlay = new System.Windows.Controls.Canvas();
        canvas.AttachEditing(editor, overlay);
        canvas.Presentation = p;
        canvas.Slide        = slide;

        // Activate the editor so we can call ApplyColor on the selection.
        canvas.TextEditor!.Activate(shape.Id);
        canvas.TextEditor.IsActive.Should().BeTrue();

        // Apply a blue color to the selection (changes the run's color).
        canvas.TextEditor.ApplyColor(blueColor);

        // Commit — this should issue a command because color changed.
        canvas.TextEditor.Commit();

        editor.CanUndo.Should().BeTrue(
            "a color-only change must be detected and issue an undo-able command");
    }

    // ─── Y4: SemiBold weight NOT coerced to Bold ───────────────────────────────

    [StaFact]
    public void Converter_SemiBoldRun_NotCoercedToBold()
    {
        // Build a FlowDocument with a SemiBold run manually (simulating WPF producing it).
        var doc = new FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        var wr  = new System.Windows.Documents.Run("SemiBold text")
        {
            FontWeight = FontWeights.SemiBold
        };
        wp.Inlines.Add(wr);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc);

        var r = restored.Paragraphs[0].Runs[0];
        r.Bold.Should().BeFalse("SemiBold must NOT be coerced to Bold=true");
    }

    // ─── Y5: soft-break "\n" runs survive round-trip as LineBreak ─────────────

    [StaFact]
    public void Converter_SoftBreakRun_RoundTrips_AsLineBreak()
    {
        // Arrange: a body with a soft line-break run (Text=="\n").
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph();
        para.Runs.Add(new ModelRun { Text = "Line 1" });
        para.Runs.Add(new ModelRun { Text = "\n" });    // soft break
        para.Runs.Add(new ModelRun { Text = "Line 2" });
        body.Paragraphs.Add(para);

        // Act: to FlowDocument and back.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "three model runs (text + soft-break + text)");
        runs[0].Text.Should().Be("Line 1");
        runs[1].Text.Should().Be("\n", "soft break must survive as '\\n' text in the model");
        runs[2].Text.Should().Be("Line 2");
        restored.Paragraphs.Should().ContainSingle(
            "a soft LineBreak stays inside its source paragraph");
        InCanvasTextEditPlanner.ExtractPlainText(restored)
            .Should().Be("Line 1\nLine 2");
    }

    // ─── Z2: offset-based original-run matching (no cross-contamination after edits) ─

    /// <summary>
    /// Z2 (a): original [A(scheme accent1), B(scheme accent2)], NO edit.
    /// Both scheme colors must be preserved (existing Y2 behavior verified with offset matching).
    /// </summary>
    [StaFact]
    public void Z2_UnEditedTwoSchemeColorRuns_BothSchemeColorsPreserved()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "no edit → same run count");

        runs[0].Color.Should().NotBeNull("A still has a color");
        var colorA = runs[0].Color!;
        colorA.SchemeColor.Should().NotBeNull("A's scheme color must be preserved");
        colorA.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (a): run A must retain accent1, not contaminate with accent2");

        runs[1].Color.Should().NotBeNull("B still has a color");
        var colorB = runs[1].Color!;
        colorB.SchemeColor.Should().NotBeNull("B's scheme color must be preserved");
        colorB.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (a): run B must retain accent2");
    }

    /// <summary>
    /// Z2 (b): a character typed in the MIDDLE of run A splits it into two inlines (A1, A2).
    /// Run B follows.  Offset matching must give A1 and A2 both accent1, and B accent2.
    /// No cross-contamination (old ordinal bug: A2 would get accent2, B would lose its color).
    /// </summary>
    [StaFact]
    public void Z2_TypingMidRunA_HalvesKeepAccent1_BKeepsAccent2()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(3 chars, accent1) + B(3 chars, accent2).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate user typed one char in the middle of A: FlowDocument has [A1, A2, B].
        // A1 = "AA" (offset 0..1), A2 = "xA" (offset 2..3 in original = still within A), B = "BBB".
        // We build a FlowDocument manually (no RichTextBox required — the converter is pure).
        var doc = new System.Windows.Documents.FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();

        // Brush colour matching accent1 (so WpfInlineToModelRun recognises it as unchanged).
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var accent2Brush = new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31));

        var wrA1 = new WpfRun("AA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrA2 = new WpfRun("xA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrB  = new WpfRun("BBB") { Foreground = accent2Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        wp.Inlines.Add(wrA1);
        wp.Inlines.Add(wrA2);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "A split into two inlines + B");

        // A1 (offset 0) is inside original A [0,3) → accent1.
        runs[0].Color.Should().NotBeNull();
        runs[0].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (b): A1 starts at offset 0, inside original A → accent1");

        // A2 ("xA") is in the DISTURBED MIDDLE under prefix/suffix matching:
        // prefix=0 (text "AA" ≠ original "AAA"), suffix=1 (BBB=BBB).
        // The AA1 fail-safe rule: middle runs NEVER carry a scheme color ref from an original run.
        // If A2 has a locally-set Foreground brush, WpfInlineToModelRun synthesizes a plain sRGB
        // (no SchemeColor) — that is acceptable (the sRGB is correct, only the scheme ref is lost).
        // The WRONG outcome would be A2 getting accent1's SchemeColor ref from the wrong original.
        var a2SchemeSlot = runs[1].Color?.SchemeColor?.Slot;
        a2SchemeSlot.Should().BeNull(
            "Z2 (b): A2 is in the disturbed middle → must NOT carry any scheme color ref " +
            "(fail-safe: never contaminate with wrong original run's scheme ref)");

        // B (suffix match: text "BBB" == original B "BBB") → accent2.
        runs[2].Color.Should().NotBeNull();
        runs[2].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (b): B is in the unchanged suffix → accent2 preserved");
    }

    /// <summary>
    /// Z2 (c): a soft break (LineBreak = "\n") inserted between A and B.
    /// LineBreak counts as 1 char in offset accounting.
    /// B's scheme color must not shift due to the break consuming one offset slot.
    /// </summary>
    [StaFact]
    public void Z2_SoftBreakBetweenRuns_DoesNotShiftBColor()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(3 chars, accent1) + softbreak(1 char, no color) + B(3 chars, accent2).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "\n" }); // soft break, no color
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate round-trip through FlowDocument (no edit).
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3, "A + softbreak + B");

        runs[0].Text.Should().Be("AAA");
        runs[0].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (c): A's color must be accent1 even when a soft-break follows");

        runs[1].Text.Should().Be("\n", "soft break preserved");
        runs[1].Color.Should().BeNull("soft break run has no color (null)");

        runs[2].Text.Should().Be("BBB");
        runs[2].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent2,
            "Z2 (c): B's color must be accent2 — soft-break offset shift must not misalign it");
    }

    /// <summary>
    /// Z2 (d): brand-new text appended beyond the original text length.
    /// The new run has no corresponding original run → must inherit (Color=null), not carry a wrong color.
    /// </summary>
    [StaFact]
    public void Z2_NewTextBeyondOriginalLength_InheritsNull_NotWrongColor()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: single run A(3 chars, accent1).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // Simulate user appended "NEW" after A: FlowDocument has [A, NEW].
        // NEW has no foreground set (inherited) — it's new text beyond original length.
        var doc = new System.Windows.Documents.FlowDocument();
        var wp  = new System.Windows.Documents.Paragraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var wrA   = new WpfRun("AAA") { Foreground = accent1Brush, FontFamily = new FontFamily("Calibri"), FontSize = 12 * (96.0/72.0) };
        var wrNew = new WpfRun("NEW"); // no foreground set — inherits
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrNew);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // A: offset 0, inside original A [0,3) → should preserve accent1.
        runs[0].Color?.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "Z2 (d): original A run still gets accent1");

        // NEW: offset 3, beyond original length (3..3) → no matching original run → null (inherit).
        runs[1].Color.Should().BeNull(
            "Z2 (d): new text beyond original length must inherit (null), not carry accent1 or any color");
    }

    // ─── AA1: prefix/suffix fail-safe (delete-shift, insert-shift, suffix-preserve, append) ─

    /// <summary>
    /// AA1 (a) — DELETE-SHIFT: the core AA1 bug case.
    /// Original: A[0,5)(accent1, locally-set Foreground), B[5,10)(Color=null, inherits).
    /// User deletes 3 chars from A: edited inlines become A'("AB", 2 chars) + B("FGHIJ", 5 chars).
    /// Under prefix/suffix: A'.Text="AB" ≠ orig A.Text="ABCDE" → prefix=0.
    ///                       B.Text="FGHIJ" == orig B.Text="FGHIJ" → suffix=1.
    /// B is in the suffix, matched to original B whose Color=null → reconstructed B.Color must be NULL.
    /// </summary>
    [StaFact]
    public void AA1_DeleteShift_RunB_ColorIsNull_NotAccent1()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(5 chars, accent1, local Foreground) + B(5 chars, Color=null inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "ABCDE", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "FGHIJ", Color = null });   // Color=null: INHERIT
        body.Paragraphs.Add(para);

        // Build post-edit FlowDocument: A'("AB", locally-set accent1 brush) + B("FGHIJ", no foreground).
        // This mirrors what the RichTextBox produces after the user deletes 3 chars from A.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA = new WpfRun("AB") { Foreground = accent1Brush };      // locally set
        var wrB = new WpfRun("FGHIJ");                                   // no local foreground (inherit)
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;

        runs.Should().HaveCount(2);

        // A' is in the DISTURBED MIDDLE (text "AB" ≠ orig "ABCDE" → prefix=0).
        // Its local Foreground matches accent1 but the prefix check failed → it still gets
        // the locally-set sRGB (synthesized, not the scheme ref). That is acceptable degradation.
        // (We don't assert its exact Color here — the key assertion is about B.)

        // B MUST be null (inherit), NOT accent1.
        // Suffix match: B.Text "FGHIJ" == orig B.Text "FGHIJ" → matches orig B (Color=null).
        runs[1].Color.Should().BeNull(
            "AA1 (a): B's Color=null (inherit) must survive — must NOT be contaminated with A's accent1 " +
            "even though 3 chars were deleted from A shifting B's character offset");
    }

    /// <summary>
    /// AA1 (b) — INSERT-SHIFT: inserting chars before B shifts B's offset but its text stays the same.
    /// B should still inherit (null), not get A's color.
    /// </summary>
    [StaFact]
    public void AA1_InsertShift_RunB_ColorIsNull_NotAccent1()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(3 chars, accent1) + B(3 chars, Color=null inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = null });
        body.Paragraphs.Add(para);

        // Post-edit: user inserted "XX" inside A → A is now "XAAXAA" or "AAX" or similar.
        // The important thing: A has different text, B has the SAME text "BBB".
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA = new WpfRun("AAAXX") { Foreground = accent1Brush };   // A now longer, locally set
        var wrB = new WpfRun("BBB");                                     // B unchanged, no local foreground
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // A: text "AAAXX" ≠ orig "AAA" → prefix=0, A is in middle → null or synthesized sRGB.
        // B: suffix match "BBB"=="BBB" → matched to orig B (Color=null).
        runs[1].Color.Should().BeNull(
            "AA1 (b): B Color=null must survive an insert in A — B is in the suffix, matched to orig B");
    }

    /// <summary>
    /// AA1 (c) — TRAILING-UNCHANGED: edit run A, leave B and C untouched at the end.
    /// B and C must retain their scheme colors (suffix match).
    /// </summary>
    [StaFact]
    public void AA1_TrailingUnchangedRuns_KeepSchemeColors()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        // Original: A(accent1, "AAA") + B(accent2, "BBB") + C(null, "CCC").
        // C has no color (inherit).
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color });
        para.Runs.Add(new ModelRun { Text = "CCC", Color = null });
        body.Paragraphs.Add(para);

        // Simulate user edited A (now "ZZZ"), B and C unchanged.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));
        var accent2Brush = new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31));

        var wrA = new WpfRun("ZZZ") { Foreground = accent1Brush };
        var wrB = new WpfRun("BBB") { Foreground = accent2Brush };
        var wrC = new WpfRun("CCC");                                   // no local foreground
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrB);
        wp.Inlines.Add(wrC);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(3);

        // A: "ZZZ" ≠ orig "AAA" → prefix=0, A in middle → no scheme ref (acceptable).
        // Suffix: C "CCC"=="CCC" (suffixLen≥1), then B "BBB"=="BBB" (suffixLen≥2) → suffixLen=2.
        // B matched to orig B (accent2), C matched to orig C (null).

        runs[1].Color.Should().NotBeNull("B is in the suffix, matched to orig B (accent2)");
        runs[1].Color!.SchemeColor.Should().NotBeNull("B's scheme ref must be preserved");
        runs[1].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "AA1 (c): B is trailing-unchanged → must keep accent2");

        runs[2].Color.Should().BeNull(
            "AA1 (c): C is trailing-unchanged with Color=null → must stay null");
    }

    /// <summary>
    /// AA1 (d) — APPEND-NEW-TEXT: appending a new run at the end must NOT carry the last original run's color.
    /// </summary>
    [StaFact]
    public void AA1_AppendNewRun_NewRunInheritsNull()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });

        // Original: A(accent1, "Hello").
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "Hello", Color = accent1Color });
        body.Paragraphs.Add(para);

        // Post-edit: A unchanged, NEW appended.
        // A Foreground locally set (accent1 brush), NEW has no foreground.
        var doc = new FlowDocument();
        var wp  = new WpfParagraph();
        var accent1Brush = new SolidColorBrush(Color.FromRgb(0x44, 0x72, 0xC4));

        var wrA   = new WpfRun("Hello") { Foreground = accent1Brush };
        var wrNew = new WpfRun(" world!");   // new text, no foreground set
        wp.Inlines.Add(wrA);
        wp.Inlines.Add(wrNew);
        doc.Blocks.Add(wp);

        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);
        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2);

        // Prefix: "Hello"=="Hello" → prefixLen=1. A matched to orig A (accent1).
        runs[0].Color.Should().NotBeNull("A is unchanged (prefix match) → accent1 preserved");
        runs[0].Color!.SchemeColor?.Slot.Should().Be(ThemeColorSlot.Accent1,
            "AA1 (d): A's scheme ref must be preserved when it's in the unchanged prefix");

        // NEW: prefixLen=1, suffixLen=0 (only 1 orig run, already consumed by prefix).
        // NEW is in the middle → null.
        runs[1].Color.Should().BeNull(
            "AA1 (d): newly appended run has no original counterpart → must inherit (null), not carry accent1");
    }

    /// <summary>
    /// AA1 (e) — NO-EDIT full prefix: original [A(accent1), B(accent2)], no edit at all.
    /// Both scheme refs must be preserved via prefix match (all runs match → full prefix, suffix=0).
    /// </summary>
    [StaFact]
    public void AA1_NoEdit_TwoSchemeRuns_BothPreservedViaPrefix()
    {
        var accent1Color = new ThemeAwareColor(
            new SrgbColor(0x44, 0x72, 0xC4),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent1 });
        var accent2Color = new ThemeAwareColor(
            new SrgbColor(0xED, 0x7D, 0x31),
            new SchemeColorRef { Slot = ThemeColorSlot.Accent2 });

        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun { Text = "AAA", Color = accent1Color, FontFamily = "Calibri", FontSizePt = 12 });
        para.Runs.Add(new ModelRun { Text = "BBB", Color = accent2Color, FontFamily = "Calibri", FontSizePt = 12 });
        body.Paragraphs.Add(para);

        // No edit: round-trip via ToFlowDocument / FromFlowDocument.
        var doc      = TextBodyFlowDocumentConverter.ToFlowDocument(body, fallbackFontSizePt: 12);
        var restored = TextBodyFlowDocumentConverter.FromFlowDocument(doc, body);

        var runs = restored.Paragraphs[0].Runs;
        runs.Should().HaveCount(2, "no edit → same run count");

        runs[0].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent1,
            "AA1 (e): A in unchanged prefix → accent1 preserved");
        runs[1].Color!.SchemeColor!.Slot.Should().Be(ThemeColorSlot.Accent2,
            "AA1 (e): B in unchanged prefix → accent2 preserved");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a TextBody with a single paragraph and two runs:
    /// - Run 0: "Hello", Bold=true, Color=Red(#FF0000), FontSize=24pt
    /// - Run 1: " world", Bold=false, no color, FontSize=12pt
    /// </summary>
    private static ResolvedTextLayout ComposeText(TextBody body)
    {
        var presentation = FreeP.Core.Model.Presentation.CreateEmpty();
        presentation.Slides[0].Shapes.Clear();
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 1,
            Kind = SlideShapeKind.AutoShape,
            AutoShapeKind = DrawingShapeKind.Rectangle,
            OffsetXEmu = 457200,
            OffsetYEmu = 274320,
            ExtentCxEmu = 4572000,
            ExtentCyEmu = 3000000,
            TextBody = body,
        });

        return SlideCompositor.Compose(presentation, presentation.Slides[0])
            .OfType<DrawOp.Shape>()
            .Single()
            .Text!;
    }

    private static TextBody DistinctParagraphBody()
    {
        var body = new TextBody();
        body.Paragraphs.Add(ModelParagraph("A", BulletKind.Char, 1, 10, 100, "*"));
        body.Paragraphs.Add(ModelParagraph("B", BulletKind.Auto, 2, 20, 200, null));
        body.Paragraphs.Add(ModelParagraph("C", BulletKind.None, 3, 30, 300, null));
        return body;
    }

    private static ModelParagraph ModelParagraph(
        string text,
        BulletKind bulletKind,
        int level,
        double spaceBefore,
        double spaceAfter,
        string? bulletChar)
    {
        var paragraph = new ModelParagraph
        {
            Level = level,
            BulletKind = bulletKind,
            BulletChar = bulletChar,
            AutoNumType = AutoNumType.AlphaLcPeriod,
            AutoNumStartAt = level + 1,
            SpaceBeforePt = spaceBefore,
            SpaceAfterPt = spaceAfter,
            Runs = { new ModelRun { Text = text } },
        };
        paragraph.TabStops.Add(new TabStop { PositionEmu = level * 100L });
        return paragraph;
    }

    private static FlowDocument FlowDocumentFor(params string[] paragraphs)
    {
        var document = new FlowDocument();
        foreach (var text in paragraphs)
            document.Blocks.Add(new WpfParagraph { Inlines = { new WpfRun(text) } });
        return document;
    }

    private static void AssertMetadata(
        IReadOnlyList<ModelParagraph> paragraphs,
        int index,
        BulletKind bulletKind,
        int level,
        double spaceBefore,
        double spaceAfter)
    {
        var paragraph = paragraphs[index];
        paragraph.BulletKind.Should().Be(bulletKind);
        paragraph.Level.Should().Be(level);
        paragraph.SpaceBeforePt.Should().Be(spaceBefore);
        paragraph.SpaceAfterPt.Should().Be(spaceAfter);
        paragraph.AutoNumStartAt.Should().Be(level + 1);
        paragraph.TabStops.Should().ContainSingle(stop => stop.PositionEmu == level * 100L);
    }

    private static TextBody MakeTwoRunBody()
    {
        var body = new TextBody { Wrap = true };
        var para = new ModelParagraph { Align = TextAlign.Left };
        para.Runs.Add(new ModelRun
        {
            Text       = "Hello",
            Bold       = true,
            FontFamily = "Calibri",
            FontSizePt = 24,
            Color      = new ThemeAwareColor(new SrgbColor(0xFF, 0x00, 0x00)),
        });
        para.Runs.Add(new ModelRun
        {
            Text       = " world",
            Bold       = false,
            FontFamily = "Calibri",
            FontSizePt = 12,
        });
        body.Paragraphs.Add(para);
        return body;
    }

    private static TextBody MakeMultiParagraphRichBody()
    {
        var body = new TextBody { Wrap = true };
        var first = new ModelParagraph { Align = TextAlign.Left };
        first.Runs.Add(new ModelRun { Text = "Alpha", FontFamily = "Calibri", FontSizePt = 12 });
        first.Runs.Add(new ModelRun { Text = " Beta", FontFamily = "Calibri", FontSizePt = 14, Italic = true, ItalicSet = true });
        var second = new ModelParagraph { Align = TextAlign.Left };
        second.Runs.Add(new ModelRun { Text = "Gamma", FontFamily = "Arial", FontSizePt = 16 });
        second.Runs.Add(new ModelRun { Text = " Delta", FontFamily = "Arial", FontSizePt = 18, Bold = true, BoldSet = true });
        body.Paragraphs.Add(first);
        body.Paragraphs.Add(second);
        return body;
    }

    private static TextBody MakeVisualEvidenceBody()
    {
        var body = new TextBody { DefaultParaAlign = TextAlign.Left };
        body.Paragraphs.Add(new ModelParagraph
        {
            Align = TextAlign.Left,
            BulletKind = BulletKind.Char,
            BulletChar = "\u2022",
            Runs =
            {
                new ModelRun { Text = "Small text ", FontFamily = "Arial", FontSizePt = 11 },
                new ModelRun { Text = "LARGE TEXT", FontFamily = "Georgia", FontSizePt = 28, Bold = true },
            },
        });
        body.Paragraphs.Add(new ModelParagraph
        {
            Align = TextAlign.Center,
            BulletKind = BulletKind.Auto,
            AutoNumType = AutoNumType.ArabicPeriod,
            Runs = { new ModelRun { Text = "Centered numbered paragraph", FontFamily = "Calibri", FontSizePt = 16, Italic = true } },
        });
        return body;
    }

    private static string EvidencePath(string fileName)
    {
        string evidenceDirectory = Environment.GetEnvironmentVariable("FREEP_RICH_EDITOR_EVIDENCE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "FreeP.RichEditorVisualGeometryEvidence");
        Directory.CreateDirectory(evidenceDirectory);
        return Path.Combine(evidenceDirectory, fileName);
    }

    private static string SaveEvidence(FrameworkElement element, string fileName)
    {
        var bitmap = new RenderTargetBitmap(
            420,
            180,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);
        string path = EvidencePath(fileName);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return path;
    }
}
