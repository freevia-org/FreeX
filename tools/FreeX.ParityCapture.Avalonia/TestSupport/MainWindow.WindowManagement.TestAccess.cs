using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal MainWindow CreateSharedViewForTest() =>
        new(
            App.StartupArguments,
            _session.CreateSiblingView(InitialViewportHeight, InitialViewportWidth),
            _optionsRuntimeSession);

}
