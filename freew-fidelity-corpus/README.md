# FreeW real-world DOCX fidelity corpus (on-demand)

This corpus backs the **on-demand** FreeW DOCX fidelity work. It is intentionally shaped like
FreeX's `fidelity-corpus`: the catalogue and downloader are committed, while third-party DOCX
binaries are downloaded into an ignored `files/` folder and are never redistributed from this repo.

The seed corpus focuses on Microsoft Word / WordprocessingML features FreeW needs to learn to open,
preserve, render, and eventually edit:

- rich run and paragraph formatting
- styles and numbering
- tables, nested tables, and table-contained notes
- comments and tracked insert/delete revisions
- footnotes and endnotes
- multi-section headers and footers
- images, drawings, charts, embedded documents, and attachments
- sizeable stress documents with mixed package parts

The initial seed uses Apache POI's `test-data/document` and `test-data/integration` DOCX fixtures
under Apache-2.0. Each file is referenced by a direct raw GitHub URL in `manifest.csv`.

## What is committed vs. downloaded

- **Committed:** `manifest.csv` and `tools/Fetch-FreeWFidelityCorpus.ps1`.
- **Not committed:** DOCX binaries in `freew-fidelity-corpus/files/` and future run output under
  `freew-fidelity-corpus/runs/`.

## Getting the files

```powershell
pwsh tools/Fetch-FreeWFidelityCorpus.ps1
pwsh tools/Fetch-FreeWFidelityCorpus.ps1 -Force
```

## Manifest schema

`id,file,source,license,retrieved_on,url,feature_tags,notes`

- `license` is required for every downloaded row and must be permissive or public-domain.
- `feature_tags` is space-separated. Prefer concrete WordprocessingML features such as
  `headers-footers`, `comments`, `tracked-changes`, `footnotes`, `endnotes`, `numbering`,
  `tables`, `images`, `drawings`, `charts`, `embedded-objects`, `attachments`, `styles`,
  and `stress`.

## Local/private additions

For truly messy real-world documents that cannot be redistributed, drop files into
`freew-fidelity-corpus/files/` and add manifest rows with `source=local` and
`url=local://<file>`. The downloader skips local rows but future FreeW fidelity tooling can still
run them from the ignored folder.
