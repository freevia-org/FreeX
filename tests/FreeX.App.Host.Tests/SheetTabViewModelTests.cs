using System.Windows.Media;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

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
        var automationNameRaised = false;
        vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(SheetTabViewModel.Name);
        vm.PropertyChanged += (_, e) => automationNameRaised |= e.PropertyName == nameof(SheetTabViewModel.AutomationName);

        vm.Name = "Budget";

        raised.Should().BeTrue();
        automationNameRaised.Should().BeTrue();
        vm.AutomationName.Should().Be("Budget");
    }

    [Fact]
    public void AutomationName_AnnouncesProtectedSheetState()
    {
        var vm = new SheetTabViewModel(SheetId.New(), "Sheet1", null, isProtected: true);

        vm.AutomationName.Should().Be("Sheet1 (protected sheet)");
    }
}
