# Ribbon Popup Placement Parity

The shared popup contract keeps root menus below/above their anchor and flips nested menus from right to left when the active work area cannot fit the preferred side. WPF applies the shared DIP-space planner through the native `MenuItem` template's `PART_Popup`; Avalonia applies the equivalent native popup placement API through a scoped `MenuItem` template style using `Right` with `FlipX|SlideY`. Both hosts retain their toolkit monitor and DPI conversion, while the shared keyboard contract continues to own Right, Left, and Escape focus transitions.

WPF does not expose a public submenu-popup placement property. A third-party WPF theme that removes or renames `PART_Popup` cannot receive the shared callback and will retain that theme's native submenu placement. The inbox template is covered by the focused host tests.
