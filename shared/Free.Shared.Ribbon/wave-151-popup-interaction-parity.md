# Wave 151 popup interaction parity

## Concrete divergence closed

Before this change, `AvaloniaContextMenuRenderer` only handled Escape at the root `ContextMenu`.
It did not attach the shared recursive keyboard behavior to menu items. In a nested menu, pressing
Left on the child therefore left the submenu open and did not restore the parent selection. The WPF
popup adapter already closes that submenu and restores its parent, so WPF is the behavior authority.

The Avalonia context-menu adapter now uses the neutral `RibbonPopupInteractionPlanner` for:

- nested Right-to-open and Escape/Left dismissal;
- Up/Down/Home/End traversal through enabled menu items;
- separator exclusion (separators are not menu-item siblings);
- disabled-item exclusion through `RibbonPopupFocusItem.CanReceiveFocus`;
- parent selection restoration when a nested submenu closes;
- root placement-target focus restoration when the context menu closes.

Both root first-item focus and nested child focus are posted at `DispatcherPriority.Input`, after
native popup attachment/realization. This matches the WPF adapter's deferred focus handoff and
prevents the toolkit's default menu interaction from winning the focus race.

The WPF adapter keeps its existing dismissal and focus timing and consumes the same planner for
traversal decisions. Toolkit event routing, popup creation, and actual command invocation remain
native adapter responsibilities.

## Evidence

- `AvaloniaContextMenuInteractionTests.ContextMenu_NestedLeftClosesSubmenuAndRestoresParentSelection`
  is the host-level regression: it would fail before this change because `IsSubMenuOpen` stayed true.
- `AvaloniaContextMenuInteractionTests.ContextMenu_DownSkipsDisabledItemsAndSeparators` proves the
  Avalonia adapter consumes the navigation event; the neutral planner test proves the selected target
  index is the next enabled item.
- `AvaloniaContextMenuInteractionTests.ContextMenu_OpenedFocusesFirstEnabledItem_AndRightDefersToRealizedChild`
  opens a real headless `ContextMenu` on a shown window, verifies `Opened` focuses the first enabled
  item after dispatcher work, then verifies Right focuses the realized child after the deferred pass.
- `RibbonCollapsedGroupPresentationPlannerTests.PopupInteractionPlanner_CentralizesNestedKeyDecisionsAndSkipsDisabledItems`
  covers Down, Right, Left, disabled items, and nested dismissal policy.
- `RibbonMenuItemPresentationPlannerTests.AvaloniaContextMenu_LeavesEmptyAndInvalidShortcutsUnwired`
  confirms empty and invalid shortcut text does not create an Avalonia gesture. No shortcut execution
  is claimed here: this slice only preserves display/parsing neutrality and popup keyboard behavior.

## Verification boundary

The focused Avalonia lane passed 18/18. Both WPF shared projects compiled successfully. The focused
WPF suite passed 16/17; its one failing assertion is the known pre-existing
`DropdownPopup_NestedMenuUsesSharedChromeAndRightLeftNavigation` `IsKeyboardFocusWithin` failure
already recorded in the Wave 150 evidence, not a new build or compilation failure from this slice.
