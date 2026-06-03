using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void RulesListView_DoubleClickOnRowOpensEditRule()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("_listView.MouseDoubleClick += ListView_MouseDoubleClick");
        source.Should().Contain("private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)");
        source.Should().Contain("EditRule_Click(sender, e);");
        source.Should().Contain("e.Handled = true;");
    }

    [Fact]
    public void RulesListView_EnterKeyOpensEditRuleAndDeleteKeyDeletesSelectedRule()
    {
        var source = ReadManageConditionalFormatsDialogSource();

        source.Should().Contain("_listView.KeyDown += ListView_KeyDown");
        source.Should().Contain("private void ListView_KeyDown");
        source.Should().Contain("Key.Enter");
        source.Should().Contain("Key.Delete");
    }
}
