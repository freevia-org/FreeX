# Shared cross-app architecture audit at c050dd2f59

## Scope and ownership

This audit covers the common application frame, renderer-neutral startup and workflow contracts,
cross-app helpers, composition roots, shared test/validation support, and repository project wiring.
Product-specific FreeX renderer files are excluded. Product edits are limited to portable
presentation/service wrappers needed to consume shared contracts.

## Implemented shared ownership

- `ApplicationFrameDescriptor` in `Free.Shared.AppServices` owns the common title specification and
  application-data status-label policy. FreeX, FreeW, and FreeP retain only product values and public
  wrapper APIs.
- `ApplicationStartupDescriptor<TTheme>` in `Free.Shared.Theme` owns the common product identity,
  diagnostics environment variable, theme environment variable, alternate-theme selector, and
  resource-prefix convention. `Free.Shared.Theme` depends on the lower-level
  `Free.Shared.AppServices` identity contract; the descriptor does not depend on Shell.
- Existing WPF/Avalonia startup runners, client-frame builders, file-open planners, validation
  routing, and shared test infrastructure already own the portable behavior in their areas. No
  additional abstraction was justified there.

## Base-catalog value classification

The neutral catalogs at `c050dd2f59` have 29 Shared/FreeX value overlaps, 5 Shared/FreeW overlaps,
and 20 Shared/FreeP overlaps. Four exact values occur in all three product catalogs but not Shared:

| Value | Product semantics | Classification |
| --- | --- | --- |
| `{0}: {1}` | FreeX chart-color descriptions and recent-file automation names; FreeW document-inspector category counts; FreeP print-option group choices | Keep product-owned. Matching placeholder shape is not a common workflow or presentation contract. French punctuation also comes from context-specific product keys. |
| `Close` | FreeX common dialog actions and automation text; FreeW dialog and bookmark actions; FreeP comment/SmartArt/common pane actions | Keep product-owned for this slice. A future extraction needs an explicit generic close-command contract, complete caller migration, and satellite comparison; adding an unused Shared key is not ownership. |
| `Print` | Window/dialog titles, command names, buttons, Backstage headings, tooltip text, and automation names | Keep product-owned. These are distinct title, command, action, and accessibility semantics that happen to share English text. |
| `Replace` | FreeX overwrite/proofing actions; FreeW thesaurus/find actions; FreeP ribbon replace command | Keep product-owned. The intents differ, and effective `fr-FR` behavior is not equal: FreeX/FreeW resolve `Remplacer`, while the representative FreeP ribbon key currently falls back to `Replace`. A Shared satellite would change product behavior. |

Value equality is therefore an audit signal, not an ownership contract. Extraction is appropriate
only when callers share the same semantic role and can consume one shared key/descriptor without
product-key adapters, renderer-wide churn, or satellite fallback changes.

## Remaining justified native scope

- Native hosts continue to construct windows, bind controls, choose platform dialogs, and expose
  automation roles. Those responsibilities are renderer-specific even when visible English text is
  equal.
- Product startup wrappers retain palette selection and any product workflow such as FreeW startup
  document opening. Shared descriptors own only the cross-app convention.
- Product localization keys remain native where they identify product commands or accessibility
  contexts. A later localization campaign can reconsider `Close` only as a semantic command contract
  with all product callers and satellites migrated together.
