Regression Workbook Bucket
==========================

This folder is reserved for minimal XLSX files that reproduce fixed open/save
bugs. Add a manifest row with `source_type` set to `regression` when a fixture
can be committed with redistribution approval, or keep private repro files in
`test-corpus/local-private/`.

Each regression row should name the fixed issue, the feature tags it exercises,
and the expected pass status so the corpus runner keeps covering that bug.
