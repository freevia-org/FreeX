# FreeW visual harness

Capture the shell with `--output <directory>` and optional `--widths` / `--height`.

The durable Unicode body-text regression lane uses real Avalonia.Skia drawing in
its own process (`UseHeadlessDrawing=false`):

```sh
pwsh -NoProfile -File tools/Invoke-Throttled.ps1 -Kind Test -- dotnet run --project freew/tools/FreeW.ShellVisualHarness.Avalonia --configuration Release -- --verify-text-spans --output artifacts/freew-body-text-spans
```

It checks actual body glyph runs against independently constructed whole-string
styled text layouts, raw UTF-16 validity and source identity, legal pointer offsets,
narrow wraps, caret advance, selected text, repeated formatting/buffer lifetime,
and complete logical PDF text primitives. Font fallback is compared using the
same installed fonts; no particular OS emoji font or glyph count is required.
It exits nonzero on behavioral failures and saves actual/reference PNGs and JSON.
Explicit LF/CRLF reference cases preserve source-length line controls through an
`ITextSource`. The shared PDF backend's complex shaping is a separate concern.

The lane covers ordinary LTR body paragraphs containing a multi-unit text element,
including hyperlink/comment marks whose semantic boundaries are legal grapheme
stops. Fields, objects, revisions, RTL, tabs, float wrapping, automatic hyphenation,
and authored style or semantic changes inside one grapheme retain separate paths.

Uniform tracking is also checked with positive and condensed advances, including zero-advance noncomposed marks and clamping. Its independent untracked reference checks glyph choices and ink positions within each source text element separately from added advance. Mixed tracking and authored or actual multi-element ligature clusters retain the separate legacy policy.

Soft-metadata cases exercise public hyperlink/comment edits and imported split
source runs, default and explicit link colours, actual underline fragments,
active/resolved/hidden comment tint, adjacent distinct link targets and ScreenTips,
and linked tracking. The new metadata fixtures author exact 40-point line spacing;
the original 40 controls keep their existing metrics. PDF annotation expectations
come from authored semantic intervals, independent whole-layout bounds checked
against legal reference character hits, and model margins/indent/line spacing.
Only each line's intersecting source interval is queried. These adapter checks do
not establish complex PDF font shaping or fidelity against Microsoft Word.
