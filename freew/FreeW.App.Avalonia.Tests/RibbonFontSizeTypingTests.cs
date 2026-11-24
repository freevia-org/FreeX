using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Free.Shared.AppServices;
using Free.Shared.Ribbon.Avalonia;
using FreeW.App.Avalonia;
using FreeW.App.Presentation.Options;
using FreeW.Core.Model;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace FreeW.App.Avalonia.Tests;

public sealed class RibbonFontSizeTypingTests(ITestOutputHelper output)
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData("24", 24d)]
    [InlineData("80", 80d)]
    public Task TypingFontSizeInTheRealShellCommitsOnceWithoutTypingIntoTheDocument(string value, double points) =>
        Session.Dispatch(() =>
        {
            var assemblyPath = typeof(AvaloniaRibbonRenderer).Assembly.Location;
            output.WriteLine($"Loaded ribbon: {assemblyPath}; SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath)))}");
            var window = new MainWindow([], new FreeWOptions(), new InMemoryApplicationOptionsStore<FreeWOptions>(),
                promptSaveChangesAsync: _ => Task.FromResult(SaveChangesPrompt.DontSave),
                suppressStartupRecoveryOffer: true) { Width = 1500, Height = 900 };
            try
            {
                window.Show();
                window.UpdateLayout();
                var editor = window.Editor;
                editor.InsertText("Sample");
                editor.SelectAll();
                // Settle the shell's adaptive ribbon layout before finding/focusing its input.
                // Headless input drains pending layout jobs before dispatching a character too.
                window.KeyTextInput(string.Empty);
                var combo = window.GetVisualDescendants().OfType<ComboBox>()
                    .Single(control => Equals(control.Tag, "freew.font-size"));
                var input = combo.GetVisualDescendants().OfType<TextBox>().Single();
                output.WriteLine($"Size control: {combo.GetType().FullName}; initial text {combo.Text}");
                input.Focus().Should().BeTrue();
                input.SelectAll();

                foreach (var character in value)
                {
                    window.KeyTextInput(character.ToString());
                    output.WriteLine($"After {character}: combo {combo.Text}; model {editor.Document.PlainText}; size {editor.GetCaretFormatting().Run.FontSizePt}; focused {window.FocusManager?.GetFocusedElement()?.GetType().FullName}");
                    editor.Document.PlainText.Should().Be("Sample");
                    editor.GetCaretFormatting().Run.FontSizePt.GetValueOrDefault(11).Should().Be(11);
                    input.IsFocused.Should().BeTrue("all font-size digits belong to the ribbon field");
                }

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                editor.IsFocused.Should().BeTrue();
                editor.Document.Blocks.Should().ContainSingle();
                editor.Document.PlainText.Should().Be("Sample");
                ((Paragraph)editor.Document.Blocks[0]).Runs
                    .Where(run => run.Text.Length > 0).Should().OnlyContain(run => run.Formatting.FontSizePt == points);

                window.KeyTextInput("x");
                editor.Document.PlainText.Should().Be("x");
                ((Paragraph)editor.Document.Blocks[0]).Runs
                    .Where(run => run.Text.Length > 0).Should().OnlyContain(run => run.Formatting.FontSizePt == points);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
}
