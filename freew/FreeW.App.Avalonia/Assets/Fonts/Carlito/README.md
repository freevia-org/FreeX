# Carlito document fallback assets

The four TTF files are unmodified released Google Fonts binaries pinned to commit
`06bfe0d62a98375cb09c942fa18e68bd32cff057`, directory `ofl/carlito`.
`UPSTREAM.json` records the source URLs, byte counts, upstream Git blob IDs and
SHA-256 content hashes. All binary downloads were checked against their upstream
Git blob IDs before inclusion. The license text has only trailing whitespace
removed; its upstream and bundled hashes are both recorded.

Carlito is metric-compatible with Calibri according to its
[upstream project](https://github.com/googlefonts/carlito). FreeW uses it only as a
render-time fallback after installed Calibri; it does not rename document fonts,
install system fonts, replace UI fonts, or assert compatibility with Aptos or
Cambria. Include all four faces to retain real bold and italic styles.

Copyright 2013 The Carlito Project Authors (https://github.com/googlefonts/carlito),
with Reserved Font Name "Carlito". Distributed under the SIL Open Font License 1.1;
see `OFL.txt`, the offline third-party license text and
`docs/legal/licenses/Carlito-OFL-1.1.txt`.
