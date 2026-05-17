# Freexcel XLSX Test Corpus

This folder is the workspace for the Sprint 2 XLSX fidelity corpus. The corpus measures whether Freexcel opens and re-saves supported workbook content without data loss, while separately tracking excluded or deferred Excel features that should produce user-facing warnings.

## Folder Policy

- `generated/` contains deterministic workbooks created from Freexcel tests or helper scripts.
- `public/` contains public workbooks only after redistribution rights are confirmed.
- `regressions/` contains minimal workbooks that reproduce fixed bugs.
- `local-private/` is for user-provided local samples and must not be committed.

## License Rules

Every committed workbook must have a manifest row with its source and redistribution status. Public samples require an explicit license or documented permission to redistribute in this repository. Private local files stay on this machine and should be listed only with anonymized notes unless the user approves more detail.

## Manifest

`manifest.csv` is the source of truth for corpus membership. Missing `local-private/` files should be skipped by automated runners instead of failing CI.
