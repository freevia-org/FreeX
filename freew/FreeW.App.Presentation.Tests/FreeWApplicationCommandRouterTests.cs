using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWApplicationCommandRouterTests
{
    [Fact]
    public void ExecuteRoutesEveryApplicationCommandExactlyOnce()
    {
        var executed = new List<FreeWKeyboardCommand>();
        Action Track(FreeWKeyboardCommand command) => () => executed.Add(command);
        var router = new FreeWApplicationCommandRouter(new FreeWApplicationCommandActions(
            Track(FreeWKeyboardCommand.NewDocument),
            Track(FreeWKeyboardCommand.OpenDocument),
            Track(FreeWKeyboardCommand.SaveDocument),
            Track(FreeWKeyboardCommand.SaveDocumentAs),
            Track(FreeWKeyboardCommand.PrintDocument),
            Track(FreeWKeyboardCommand.Find),
            Track(FreeWKeyboardCommand.Replace),
            Track(FreeWKeyboardCommand.Cut),
            Track(FreeWKeyboardCommand.Copy),
            Track(FreeWKeyboardCommand.Paste),
            Track(FreeWKeyboardCommand.PasteTextOnly),
            Track(FreeWKeyboardCommand.SelectAll),
            Track(FreeWKeyboardCommand.Undo),
            Track(FreeWKeyboardCommand.Redo),
            Track(FreeWKeyboardCommand.RevealFormatting),
            Track(FreeWKeyboardCommand.Thesaurus),
            Track(FreeWKeyboardCommand.LockCurrentField),
            Track(FreeWKeyboardCommand.UnlockCurrentField),
            Track(FreeWKeyboardCommand.UnlinkCurrentField),
            Track(FreeWKeyboardCommand.ToggleCurrentFieldCode),
            Track(FreeWKeyboardCommand.ToggleFieldCodes),
            Track(FreeWKeyboardCommand.UpdateCurrentField)));

        foreach (var command in Enum.GetValues<FreeWKeyboardCommand>())
            router.Execute(command);

        executed.Should().Equal(Enum.GetValues<FreeWKeyboardCommand>());
    }

    [Fact]
    public void ExecuteRejectsUnknownCommand()
    {
        var router = new FreeWApplicationCommandRouter(new FreeWApplicationCommandActions(
            NoAction, NoAction, NoAction, NoAction, NoAction, NoAction,
            NoAction, NoAction, NoAction, NoAction, NoAction, NoAction,
            NoAction, NoAction, NoAction, NoAction, NoAction, NoAction,
            NoAction, NoAction, NoAction, NoAction));

        var act = () => router.Execute((FreeWKeyboardCommand)int.MaxValue);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void NoAction()
    {
    }
}
