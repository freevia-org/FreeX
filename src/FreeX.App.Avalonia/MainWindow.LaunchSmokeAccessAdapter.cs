using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    internal LaunchSmokeAccessAdapter CreateLaunchSmokeAccessAdapter() => new(this);

    internal sealed class LaunchSmokeAccessAdapter
    {
        private readonly MainWindow _owner;

        internal LaunchSmokeAccessAdapter(MainWindow owner) => _owner = owner;

        internal void StartWhenOpened(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ((Window)_owner).Opened += async (_, _) => await operation();
        }

        internal MacOsLaunchSmokeSnapshot CreateSnapshot() => _owner.CreateLaunchSmokeSnapshot();

        internal Task<MacOsLaunchSmokeDialogSnapshot> CaptureDialogEvidenceAsync() =>
            _owner.CaptureLaunchSmokeDialogEvidenceAsync();

        internal Task TryPasteClipboardImageAsync() => _owner.TryPasteLaunchSmokeClipboardImageAsync();

        internal MacOsLaunchSmokeLiveCommandKeySnapshot BeginLiveCommandKeyProbe() =>
            _owner.BeginLaunchSmokeLiveCommandKeyProbe();

        internal MacOsLaunchSmokeLiveCommandKeySnapshot CreateLiveCommandKeySnapshot() =>
            _owner.CreateLaunchSmokeLiveCommandKeySnapshot();

        internal bool HasNativeMenuItemGesture(
            string fieldName,
            Key expectedKey,
            KeyModifiers expectedModifiers) =>
            typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?
                .GetValue(_owner) is NativeMenuItem { Gesture: { } gesture } &&
            gesture.Key == expectedKey &&
            gesture.KeyModifiers == expectedModifiers;

        internal static bool HasMethods(params string[] methodNames) =>
            methodNames.All(methodName =>
                typeof(MainWindow).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic) is not null);
    }
}
