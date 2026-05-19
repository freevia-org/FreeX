using System.Windows.Media;
using FluentAssertions;
using Freexcel.App.Host;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SheetTabViewModelTests
{
    [Fact]
    public void TabBrush_UsesTabColorWhenPresent()
    {
        var vm = new SheetTabViewModel(SheetId.New(), "Sheet1", new CellColor(12, 34, 56));

        var brush = vm.TabBrush.Should().BeOfType<SolidColorBrush>().Subject;
        brush.Color.Should().Be(Color.FromRgb(12, 34, 56));
    }

    [Fact]
    public void NameSetter_RaisesPropertyChanged()
    {
        var vm = new SheetTabViewModel(SheetId.New(), "Sheet1", null);
        var raised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(SheetTabViewModel.Name);

        vm.Name = "Budget";

        raised.Should().BeTrue();
    }
}
