# Wave 153 Avalonia ribbon state-refresh parity

## Concrete production divergence

The WPF renderer binds each rendered command control to the production `IRibbonStateStore`. A later
`StateChanged` write updates enablement, checked state, or combo value, and a toggle publishes its new
checked state before WPF raises the command handler. Avalonia previously read
`IRibbonStatefulCommand.GetState()` while constructing controls and relied on host-specific visual-tree
scans for later refreshes; the shared store was not owned or passed by any Avalonia production host.

## Implementation

Avalonia now accepts the same optional store on `BuildRibbon` and `BuildTabContent`. Each rendered tab
content root subscribes to the store, applies only explicitly written states so command-derived initial
state is preserved, and synchronizes toggle changes into the store before the command click callback.
The existing stateful-command refresh helper can also publish current production command state into the
store.

FreeX, FreeW, and FreeP Avalonia hosts now each own a `RibbonStateStore`, pass it into the shared ribbon
renderer, and pass it through their existing ribbon-state refresh hooks. This makes the store a real
production lifecycle dependency rather than a test-only renderer option.

## Evidence

- `AvaloniaRibbonHostStateStoreTests.ProductionHostBuild_BindsLiveStateAndPublishesToggleBeforeCommand`
  builds the FreeX production ribbon host, observes a live store update changing the rendered toggle's
  checked/enabled state, and verifies the command observes the new checked value before execution.
- `RibbonStateStoreTests` covers explicit-state presence alongside existing setter/event behavior.
- WPF authority remains `RibbonWpfRenderer.BindControlToStore` and its toggle click ordering.
