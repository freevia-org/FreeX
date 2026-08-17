using System.Runtime.CompilerServices;

// Lets FreeP.App.Host.Tests (which already pulls this assembly in transitively through
// FreeP.App.Host's ProjectReference) exercise WindowsNativeRecordingCaptureEngine's internal
// test-seam constructor -- the real WinRT camera calls cannot be made to hang deterministically in
// CI, so tests substitute a controllable delayed operation while still driving the real
// BeginCapture/CompleteCapture entry points.
[assembly: InternalsVisibleTo("FreeP.App.Host.Tests")]
