using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

[Collection("AutoFillCustomLists")]
public sealed class CustomListsTests
{
    [Fact]
    public void Normalize_TrimsDropsBlanksShortListsAndDuplicates()
    {
        var result = AppOptions.NormalizeCustomLists(
        [
            new[] { " Small ", "", "Medium", "Large" },
            new[] { "only" },
            new[] { "small", "medium", "large" },
            new[] { "Low", "High" },
        ]);

        Assert.Equal(2, result.Count);
        Assert.Equal(["Small", "Medium", "Large"], result[0]);
        Assert.Equal(["Low", "High"], result[1]);
    }

    [Fact]
    public void CopyFrom_DeepCopiesCustomLists()
    {
        var source = new AppOptions { CustomLists = [["A", "B"]] };
        var copy = new AppOptions();
        copy.CopyFrom(source);
        source.CustomLists[0].Add("C");

        Assert.Equal(["A", "B"], copy.CustomLists[0]);
    }

    [Fact]
    public void RuntimeSession_PublishesCustomListsToAutofill()
    {
        try
        {
            var options = new AppOptions { CustomLists = [["Small", "Medium", "Large"]] };
            var session = new FreeXOptionsRuntimeSession(options, () => options, _ => true);
            Assert.Single(AutoFillCustomLists.Current);

            var workbook = new Workbook("t");
            var sheet = workbook.AddSheet("S");
            var a1 = new CellAddress(sheet.Id, 1, 1);
            sheet.SetCell(a1, Cell.FromValue(new TextValue("Small")));
            var command = new AutofillCommand(
                sheet.Id,
                new GridRange(a1, a1),
                new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 3, 1)));
            var ctx = new Ctx(workbook);
            Assert.True(command.Apply(ctx).Success);

            Assert.Equal(new TextValue("Medium"), sheet.GetValue(new CellAddress(sheet.Id, 2, 1)));
            Assert.Equal(new TextValue("Large"), sheet.GetValue(new CellAddress(sheet.Id, 3, 1)));
            _ = session;
        }
        finally
        {
            AutoFillCustomLists.Current = [];
        }
    }

    private sealed class Ctx(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException();
    }
}
