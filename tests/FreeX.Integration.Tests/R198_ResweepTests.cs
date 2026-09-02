using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r198, second saturation pass: four questions that earlier rounds had already swept the whole repo
/// with were asked again, and three of them found one more instance apiece.
/// </summary>
public sealed class R198_ResweepTests
{
    // ── "text sliced by UTF-16 char, and the result is STORED" ────────────────────────────────
    // Flash Fill derives name abbreviations with a first-initial helper that took value[0]. For a
    // name beginning outside the BMP that is a lone high surrogate, and FlashFillCommand writes the
    // prediction into Cell.Value for every filled row -- so the workbook permanently stored a
    // codepoint with no glyph instead of the initial the user asked for.

    // The two abbreviation patterns cover both first-initial helpers: "J. Smith" goes through
    // FlashFillService.GetFirstInitial, "JS" through FlashFillTextPrimitives.GetUpperInitial.
    [Theory]
    [InlineData("J. Smith", "M. Jones")]
    [InlineData("JS", "MJ")]
    public void FlashFill_AbbreviatingAnAstralName_StoresNoLoneSurrogate(
        string firstExpected, string secondExpected)
    {
        // Two worked examples teach the pattern; the row left to fill is the astral one.
        var filled = FlashFillService.Fill(
            [("John Smith", firstExpected), ("Mary Jones", secondExpected)],
            ["\U0001F600lex Kim"]);

        filled.Should().NotBeNull("the pattern is one Flash Fill recognizes");
        HasLoneSurrogate(filled![0]).Should().BeFalse(
            "Flash Fill writes its prediction straight into the cell; got '{0}'", filled[0]);
        filled[0].Should().StartWith("\U0001F600", "the whole leading character is the initial");
    }

    // ── "a serializer that handles a SUBSET of its model type" ────────────────────────────────
    // SparklineDto mirrored every SparklineModel property but DateAxisRange, so saving to the native
    // .fxl format silently reverted a sparkline group's Date Axis Type to even spacing.

    [Fact]
    public void NativeRoundTrip_KeepsTheSparklineDateAxis()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(1));

        var dateAxis = new GridRange(new CellAddress(sid, 1, 3), new CellAddress(sid, 5, 3));
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1)),
            Location = new CellAddress(sid, 1, 2),
            DateAxisRange = dateAxis,
        });

        var reopened = RoundTrip(workbook);

        // Compared in A1 form: the reopened workbook mints its own SheetId.
        reopened.Sheets[0].Sparklines.Should().ContainSingle()
            .Which.DateAxisRange.Should().NotBeNull()
            .And.Subject.ToString().Should().Be(dateAxis.ToString());
    }

    [Fact]
    public void NativeRoundTrip_LeavesASparklineWithoutADateAxisAlone()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new NumberValue(1));
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 5, 1)),
            Location = new CellAddress(sid, 1, 2),
        });

        RoundTrip(workbook).Sheets[0].Sparklines.Should().ContainSingle()
            .Which.DateAxisRange.Should().BeNull();
    }

    // The same lens, same file, from the previous round's finding: ConditionalFormatDto carried 55 of
    // the model's 62 properties. The seven it missed flatten a theme-linked colour stop to literal RGB
    // (after which it stops tracking a theme change) and lose the data bar's negative-value choices.

    [Fact]
    public void NativeRoundTrip_KeepsTheThemeLinkedColorStopsAndDataBarChoices()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1)),
            RuleType = CfRuleType.DataBar,
            MinColorSource = new CfColorStopSource(4, 0.4),
            MidColorSource = new CfColorStopSource(5),
            MaxColorSource = new CfColorStopSource(6, -0.25),
            DataBarColorSource = new CfColorStopSource(7, 0.5),
            DataBarNegativeFillSameAsPositive = true,
            DataBarNegativeBorderSameAsPositive = true,
            DataBarDirection = "rightToLeft",
        });

        var reopened = RoundTrip(workbook).Sheets[0].ConditionalFormats.Should().ContainSingle().Subject;

        reopened.MinColorSource.Should().Be(new CfColorStopSource(4, 0.4));
        reopened.MidColorSource.Should().Be(new CfColorStopSource(5));
        reopened.MaxColorSource.Should().Be(new CfColorStopSource(6, -0.25));
        reopened.DataBarColorSource.Should().Be(new CfColorStopSource(7, 0.5));
        reopened.DataBarNegativeFillSameAsPositive.Should().BeTrue();
        reopened.DataBarNegativeBorderSameAsPositive.Should().BeTrue();
        reopened.DataBarDirection.Should().Be("rightToLeft");
    }

    [Fact]
    public void NativeRoundTrip_LeavesAPlainConditionalFormatAlone()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var sid = sheet.Id;
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 4, 1)),
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>5",
        });

        var reopened = RoundTrip(workbook).Sheets[0].ConditionalFormats.Should().ContainSingle().Subject;

        reopened.MinColorSource.Should().BeNull();
        reopened.DataBarColorSource.Should().BeNull();
        reopened.DataBarNegativeFillSameAsPositive.Should().BeFalse();
        reopened.DataBarDirection.Should().BeNull();
        reopened.FormulaText.Should().Be("A1>5");
    }

    // ── "a command that changed nothing still pushes an undo entry" ───────────────────────────
    // CommandBus pushes unless the outcome says IsNoOp, and UndoRedoStack.Push clears redo -- so a
    // Sort over blank cells, or Bring Forward on an already-frontmost object, silently threw away
    // whatever the user could have redone.

    [Fact]
    public void Sort_OverAnUnpopulatedRange_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SortCommand(
                sheet.Id,
                new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 20, 4)),
                sortByColOffset: 0,
                ascending: true)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue("nothing was populated, so nothing moved");
    }

    [Fact]
    public void Sort_OverAnInvertedRange_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();

        new SortCommand(
                sheet.Id,
                new GridRange(new CellAddress(sheet.Id, 9, 4), new CellAddress(sheet.Id, 2, 1)),
                sortByColOffset: 0,
                ascending: true)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void Sort_ThatActuallyReorders_DoesNotReportNoOp()
    {
        // The control: a real sort must still push its undo entry.
        var (sheet, ctx) = Fixture();
        var sid = sheet.Id;
        sheet.SetCell(new CellAddress(sid, 1, 1), new TextValue("b"));
        sheet.SetCell(new CellAddress(sid, 2, 1), new TextValue("a"));

        var outcome = new SortCommand(
                sid,
                new GridRange(new CellAddress(sid, 1, 1), new CellAddress(sid, 2, 1)),
                sortByColOffset: 0,
                ascending: true)
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MovingAnObjectPastTheEndOfTheZOrder_ReportsNoOp(bool forward)
    {
        var (sheet, ctx) = Fixture();
        var back = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var top = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Pictures.Add(back);
        sheet.Pictures.Add(top);

        // Bring the frontmost forward, or send the backmost backward: both fall off the end.
        var subject = forward ? top : back;
        var outcome = new MoveSelectionPaneObjectCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, subject.Id, forward)
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.IsNoOp.Should().BeTrue("the z-order was untouched, so redo must survive");
        sheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void MovingAnObjectThatCanMove_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var back = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var top = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        sheet.Pictures.Add(back);
        sheet.Pictures.Add(top);

        new MoveSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, back.Id, forward: true)
            .Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static Workbook RoundTrip(Workbook workbook)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
                continue;
            }

            if (char.IsLowSurrogate(text[i]))
                return true;
        }

        return false;
    }
}
