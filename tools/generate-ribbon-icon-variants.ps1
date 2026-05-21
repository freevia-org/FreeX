$ErrorActionPreference = 'Stop'

$iconDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Freexcel.App.Host/Resources/CommandIconsSvg'
$baseFiles = Get-ChildItem $iconDir -Filter '*.svg' | Where-Object { $_.BaseName -notmatch '-(small|large)$' }
$culture = [Globalization.CultureInfo]::InvariantCulture
$starGlyph = [char]0x2605
$sparkleGlyph = [char]0x2726
$sigmaGlyph = [char]0x03A3
$boltGlyph = [char]0x26A1
$checkGlyph = [char]0x2713

function S([double]$v, [int]$n) { [math]::Round($v * $n / 20.0, 2).ToString($culture) }
function Svg([int]$n, [string]$body) {
@"
<svg xmlns="http://www.w3.org/2000/svg" width="$n" height="$n" viewBox="0 0 $n $n">
  <defs>
    <style>
      .s{fill:none;stroke:#1f1f1f;stroke-linecap:square;stroke-linejoin:miter;vector-effect:non-scaling-stroke}.r{fill:none;stroke:#1f1f1f;stroke-linecap:round;stroke-linejoin:round;vector-effect:non-scaling-stroke}.g{fill:#217346}.g2{fill:#8fd19e}.b{fill:#5b9bd5}.o{fill:#f4b183}.y{fill:#ffd966}.rd{fill:#d83b01}.p{fill:#8064a2}.w{fill:#fff}.k{fill:#1f1f1f}.f{fill:#f7f7f7}.t{font-family:'Segoe UI',Arial,sans-serif;fill:#1f1f1f;font-weight:600;text-anchor:middle;dominant-baseline:central}
    </style>
  </defs>
$body
</svg>
"@
}

function L($x1, $y1, $x2, $y2, [int]$n = 20, $cls = 's', $sw = 1) {
    "  <path class=""$cls"" d=""M$(S $x1 $n) $(S $y1 $n) L$(S $x2 $n) $(S $y2 $n)"" stroke-width=""$(S $sw $n)""/>"
}

function Rct($x, $y, $w, $h, [int]$n = 20, $cls = 'f s', $sw = 1) {
    "  <rect class=""$cls"" x=""$(S $x $n)"" y=""$(S $y $n)"" width=""$(S $w $n)"" height=""$(S $h $n)"" stroke-width=""$(S $sw $n)""/>"
}

function Txt($text, $x, $y, $fs, [int]$n = 20, $extra = '') {
    "  <text class=""t"" x=""$(S $x $n)"" y=""$(S $y $n)"" font-size=""$(S $fs $n)"" $extra>$text</text>"
}

function Arrow($x1, $y1, $x2, $y2, [int]$n = 20, $color = 'g', $sw = 1) {
    $dx = $x2 - $x1; $dy = $y2 - $y1
    $len = [math]::Sqrt($dx * $dx + $dy * $dy); if ($len -eq 0) { $len = 1 }
    $ux = $dx / $len; $uy = $dy / $len; $px = -$uy; $py = $ux; $h = 2.4; $w = 1.5
    $p1 = "$(S ($x2 - $ux * $h + $px * $w) $n) $(S ($y2 - $uy * $h + $py * $w) $n)"
    $p2 = "$(S $x2 $n) $(S $y2 $n)"
    $p3 = "$(S ($x2 - $ux * $h - $px * $w) $n) $(S ($y2 - $uy * $h - $py * $w) $n)"
    (L $x1 $y1 $x2 $y2 $n 'r' $sw) + "`n  <polygon class=""$color"" points=""$p1 $p2 $p3""/>"
}

function Doc([int]$n) {
    "  <path class=""f s"" d=""M$(S 5 $n) $(S 2.5 $n) H$(S 13 $n) L$(S 16 $n) $(S 5.5 $n) V$(S 17 $n) H$(S 5 $n) Z M$(S 13 $n) $(S 2.5 $n) V$(S 5.5 $n) H$(S 16 $n)"" stroke-width=""$(S 1 $n)""/>"
}

function Grid([int]$n) {
    $a = @(Rct 3 3 14 14 $n 'f s' 1)
    foreach ($x in 6.5, 10, 13.5) { $a += L $x 3 $x 17 $n 's' .7 }
    foreach ($y in 6.5, 10, 13.5) { $a += L 3 $y 17 $y $n 's' .7 }
    $a -join "`n"
}

function Folder([int]$n) {
    "  <path class=""f s"" d=""M$(S 2.5 $n) $(S 6.2 $n) H$(S 7.2 $n) L$(S 8.9 $n) $(S 4.6 $n) H$(S 14 $n) V$(S 7.2 $n) H$(S 17.5 $n) L$(S 15.8 $n) $(S 16 $n) H$(S 2.5 $n) Z M$(S 2.5 $n) $(S 7.2 $n) H$(S 17.5 $n)"" stroke-width=""$(S 1 $n)""/>"
}

function Brush([int]$n) {
    "  <path class=""k"" d=""M$(S 5 $n) $(S 4 $n) H$(S 13 $n) C$(S 14.4 $n) $(S 4 $n) $(S 15.2 $n) $(S 5 $n) $(S 15.2 $n) $(S 6.4 $n) V$(S 8.2 $n) H$(S 5 $n) Z""/>`n  <rect class=""o"" x=""$(S 6 $n)"" y=""$(S 8.2 $n)"" width=""$(S 8.2 $n)"" height=""$(S 2.1 $n)""/>`n  <path class=""f s"" d=""M$(S 8.2 $n) $(S 10.3 $n) H$(S 12 $n) V$(S 17 $n) H$(S 8.2 $n) Z"" stroke-width=""$(S 1 $n)""/>"
}

function Chart($slug, [int]$n) {
    if ($slug -match 'pie|doughnut') { return "  <path class=""y"" d=""M$(S 10 $n) $(S 3 $n) A$(S 7 $n) $(S 7 $n) 0 1 1 $(S 5 $n) $(S 15 $n) L$(S 10 $n) $(S 10 $n) Z""/><path class=""b"" d=""M$(S 10 $n) $(S 3 $n) A$(S 7 $n) $(S 7 $n) 0 0 1 $(S 17 $n) $(S 10 $n) H$(S 10 $n) Z""/>`n  <circle class=""r"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 7 $n)"" stroke-width=""$(S 1 $n)""/>" }
    if ($slug -match 'line|spark') { return (L 3 15 7 11 $n 'r' 1) + "`n" + (L 7 11 11 13 $n 'r' 1) + "`n" + (L 11 13 17 5 $n 'r' 1) }
    if ($slug -match 'scatter|bubble') { return "  <circle class=""b"" cx=""$(S 6 $n)"" cy=""$(S 13 $n)"" r=""$(S 1.5 $n)""/><circle class=""g"" cx=""$(S 10 $n)"" cy=""$(S 8 $n)"" r=""$(S 2.1 $n)""/><circle class=""o"" cx=""$(S 15 $n)"" cy=""$(S 11 $n)"" r=""$(S 2.7 $n)""/>" }
    if ($slug -match 'area') { return "  <path class=""g2"" d=""M$(S 3 $n) $(S 16 $n) L$(S 7 $n) $(S 10 $n) L$(S 11 $n) $(S 13 $n) L$(S 17 $n) $(S 5 $n) V$(S 16 $n) Z""/>`n" + (L 3 16 7 10 $n 'r' 1) + "`n" + (L 7 10 11 13 $n 'r' 1) + "`n" + (L 11 13 17 5 $n 'r' 1) }
    if ($slug -match 'bar') { return "  <rect class=""b"" x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 10 $n)"" height=""$(S 2.4 $n)""/><rect class=""o"" x=""$(S 4 $n)"" y=""$(S 9 $n)"" width=""$(S 13 $n)"" height=""$(S 2.4 $n)""/><rect class=""g"" x=""$(S 4 $n)"" y=""$(S 13 $n)"" width=""$(S 7 $n)"" height=""$(S 2.4 $n)""/>" }
    "  <rect class=""b"" x=""$(S 4 $n)"" y=""$(S 9 $n)"" width=""$(S 2.5 $n)"" height=""$(S 7 $n)""/><rect class=""o"" x=""$(S 8.7 $n)"" y=""$(S 5 $n)"" width=""$(S 2.5 $n)"" height=""$(S 11 $n)""/><rect class=""g"" x=""$(S 13.4 $n)"" y=""$(S 7 $n)"" width=""$(S 2.5 $n)"" height=""$(S 9 $n)""/>`n" + (L 3 17 18 17 $n 's' 1)
}

function Gear([int]$n) {
    "  <circle class=""r"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 4.2 $n)"" stroke-width=""$(S 1.2 $n)""/>`n  <circle class=""k"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 1.3 $n)""/>`n" + (L 10 3 10 5 $n 'r' 1) + "`n" + (L 10 15 10 17 $n 'r' 1) + "`n" + (L 3 10 5 10 $n 'r' 1) + "`n" + (L 15 10 17 10 $n 'r' 1) + "`n" + (L 5.2 5.2 6.7 6.7 $n 'r' 1) + "`n" + (L 13.3 13.3 14.8 14.8 $n 'r' 1) + "`n" + (L 14.8 5.2 13.3 6.7 $n 'r' 1) + "`n" + (L 6.7 13.3 5.2 14.8 $n 'r' 1)
}

function WindowIcon([int]$n) {
    (Rct 4 5 8 7 $n) + "`n" + (Rct 8 8 8 7 $n)
}

function Eye([int]$n) {
    "  <path class=""r"" d=""M$(S 3 $n) $(S 10 $n) C$(S 6 $n) $(S 5.5 $n) $(S 14 $n) $(S 5.5 $n) $(S 17 $n) $(S 10 $n) C$(S 14 $n) $(S 14.5 $n) $(S 6 $n) $(S 14.5 $n) $(S 3 $n) $(S 10 $n) Z"" stroke-width=""$(S 1 $n)""/>`n  <circle class=""k"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 2 $n)""/>"
}

function Database([int]$n) {
    "  <ellipse class=""f s"" cx=""$(S 10 $n)"" cy=""$(S 5 $n)"" rx=""$(S 6 $n)"" ry=""$(S 2.2 $n)"" stroke-width=""$(S 1 $n)""/>`n  <path class=""f s"" d=""M$(S 4 $n) $(S 5 $n) V$(S 15 $n) C$(S 4 $n) $(S 18 $n) $(S 16 $n) $(S 18 $n) $(S 16 $n) $(S 15 $n) V$(S 5 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (L 4 10 16 10 $n 's' .8) + "`n" + (L 4 14 16 14 $n 's' .8)
}

function Funnel([int]$n) {
    "  <path class=""f s"" d=""M$(S 3 $n) $(S 4 $n) H$(S 17 $n) L$(S 11.5 $n) $(S 10.5 $n) V$(S 16 $n) L$(S 8.5 $n) $(S 17 $n) V$(S 10.5 $n) Z"" stroke-width=""$(S 1 $n)""/>"
}

function Alignment($slug, [int]$n) {
    $starts = @(4, 4, 4); $ends = @(16, 13, 16)
    if ($slug -match 'center') { $starts = @(4, 6, 4); $ends = @(16, 14, 16) }
    if ($slug -match 'right') { $starts = @(4, 7, 4); $ends = @(16, 16, 16) }
    if ($slug -match 'justify|distributed') { $starts = @(4, 4, 4); $ends = @(16, 16, 16) }
    (0..2 | ForEach-Object { L $starts[$_] @(6,10,14)[$_] $ends[$_] @(6,10,14)[$_] $n 's' 1 }) -join "`n"
}

function Body($slug, [int]$n) {
    switch -Regex ($slug) {
        'open' { return Folder $n }
        'save-as' { return (Doc $n) + "`n" + (Txt $starGlyph 15 5.5 5 $n) }
        'paste' { return "  <path class=""y s"" d=""M$(S 6 $n) $(S 5 $n) H$(S 14 $n) V$(S 17 $n) H$(S 6 $n) Z M$(S 8 $n) $(S 3 $n) H$(S 12 $n) V$(S 6 $n) H$(S 8 $n) Z"" stroke-width=""$(S 1 $n)""/>" }
        'cut' { return "  <circle class=""r"" cx=""$(S 6 $n)"" cy=""$(S 6 $n)"" r=""$(S 2 $n)"" stroke-width=""$(S 1 $n)""/><circle class=""r"" cx=""$(S 6 $n)"" cy=""$(S 14 $n)"" r=""$(S 2 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (L 8 8 15 15 $n 'r' 1) + "`n" + (L 8 12 15 5 $n 'r' 1) }
        'copy' { return (Rct 7 4 8 10 $n) + "`n" + (Rct 4 7 8 10 $n) }
        'format-painter' { return Brush $n }
        '^bold$' { return Txt 'B' 10 10 10 $n }
        '^italic$' { return Txt 'I' 10 10 10 $n 'font-family="Georgia,serif" font-style="italic"' }
        'underline' { return (Txt 'U' 10 9 9 $n) + "`n" + (L 6 15 14 15 $n 's' 1) }
        'strikethrough' { return (Txt 'S' 10 10 9 $n) + "`n" + (L 5 10 15 10 $n 's' 1) }
        'grow-font|increase-font-size' { return (Txt 'A' 8 11 11 $n) + "`n" + (Arrow 13 13 16 6 $n 'g' 1) }
        'shrink-font|decrease-font-size' { return (Txt 'A' 8 11 8 $n) + "`n" + (Arrow 16 6 13 13 $n 'g' 1) }
        'font-color' { return (Txt 'A' 10 8.8 10 $n) + "`n  <rect class=""rd"" x=""$(S 5 $n)"" y=""$(S 15 $n)"" width=""$(S 10 $n)"" height=""$(S 2 $n)""/>" }
        'fill-color|background-color' { return "  <rect class=""y"" x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 10 $n)""/>`n" + (Rct 4 5 12 10 $n 's' 1) + "`n" + (Txt 'A' 10 10 8 $n) }
        'align-left|center|align-right|distributed-justify' { return Alignment $slug $n }
        'increase-indent|decrease-indent' { $a = @((L 8 5 17 5 $n 's' 1), (L 8 10 17 10 $n 's' 1), (L 8 15 17 15 $n 's' 1)); if ($slug -match 'decrease') { $a += Arrow 6 10 3 10 $n 'g' 1 } else { $a += Arrow 3 10 6 10 $n 'g' 1 }; return $a -join "`n" }
        'wrap-text' { return (L 4 5 14 5 $n 's' 1) + "`n" + (L 4 9 14 9 $n 's' 1) + "`n" + (Arrow 14 9 9 14 $n 'b' 1) + "`n" + (L 9 14 16 14 $n 's' 1) }
        'merge-center' { return (Grid $n) + "`n" + (Arrow 6 10 14 10 $n 'g' 1) }
        'accounting|currency' { return Txt '$' 10 10 12 $n }
        'percent' { return Txt '%' 10 10 10 $n }
        'comma' { return Txt ',' 10 8.8 14 $n }
        'increase-decimal' { return (Txt '0.0' 8 7 4.5 $n) + "`n" + (Txt '0.00' 8 13 4.5 $n) + "`n" + (Arrow 13 10 17 10 $n 'b' 1) }
        'decrease-decimal' { return (Txt '0.00' 10 7 4.5 $n) + "`n" + (Txt '0.0' 10 13 4.5 $n) + "`n" + (Arrow 17 10 13 10 $n 'b' 1) }
        'orientation' { return "  <text class=""t"" x=""$(S 9 $n)"" y=""$(S 8 $n)"" font-size=""$(S 6 $n)"" transform=""rotate(-45 $(S 9 $n) $(S 8 $n))"">ab</text>`n" + (Arrow 5 15 15 5 $n 'b' 1) }
        '^themes$' { return "  <path class=""f s"" d=""M$(S 4.5 $n) $(S 2.5 $n) H$(S 13.3 $n) L$(S 16 $n) $(S 5.2 $n) V$(S 17.5 $n) H$(S 4.5 $n) Z"" stroke-width=""$(S 1 $n)""/>`n  <rect class=""p"" x=""$(S 7 $n)"" y=""$(S 6 $n)"" width=""$(S 6 $n)"" height=""$(S 6 $n)""/>`n  <rect class=""g2"" x=""$(S 7 $n)"" y=""$(S 12 $n)"" width=""$(S 3 $n)"" height=""$(S 3 $n)""/>`n  <rect class=""b"" x=""$(S 10 $n)"" y=""$(S 12 $n)"" width=""$(S 3 $n)"" height=""$(S 3 $n)""/>" }
        '^fonts$' { return (Txt 'Aa' 9.5 9.5 8.5 $n) + "`n" + (L 5 15 15 15 $n 's' .9) }
        '^effects$' { return (Txt $sparkleGlyph 10 9 11 $n) + "`n" + (Txt $sparkleGlyph 15 15 4.5 $n) }
        '^background$' { return "  <rect class=""f s"" x=""$(S 3.5 $n)"" y=""$(S 4.5 $n)"" width=""$(S 13 $n)"" height=""$(S 11 $n)"" stroke-width=""$(S 1 $n)""/>`n  <path class=""g2"" d=""M$(S 4.5 $n) $(S 14.5 $n) L$(S 8 $n) $(S 10 $n) L$(S 11 $n) $(S 13 $n) L$(S 14.5 $n) $(S 8.5 $n) L$(S 16.5 $n) $(S 14.5 $n) Z""/><circle class=""y"" cx=""$(S 12.5 $n)"" cy=""$(S 7.6 $n)"" r=""$(S 1.4 $n)""/>" }
        '^margins$' { return "  <path class=""f s"" d=""M$(S 5 $n) $(S 2.5 $n) H$(S 15 $n) V$(S 17.5 $n) H$(S 5 $n) Z"" stroke-width=""$(S 1 $n)""/>`n" + (Rct 7.2 5 5.6 10 $n 's' .7) + "`n" + (L 5 5 7.2 5 $n 'b' .9) + "`n" + (L 12.8 15 15 15 $n 'b' .9) }
        '^paper-size$|^size$' { return "  <path class=""f s"" d=""M$(S 6 $n) $(S 2.8 $n) H$(S 14 $n) V$(S 17.2 $n) H$(S 6 $n) Z"" stroke-width=""$(S 1 $n)""/>`n" + (Arrow 4.6 4 4.6 16 $n 'b' .9) + "`n" + (Arrow 15.4 16 15.4 4 $n 'b' .9) }
        '^print-area$' { return (Grid $n) + "`n  <rect class=""g2"" x=""$(S 6.5 $n)"" y=""$(S 6.5 $n)"" width=""$(S 7 $n)"" height=""$(S 7 $n)"" opacity=""0.55""/>`n" + (Rct 6.5 6.5 7 7 $n 's' 1.1) }
        '^scale$|^scale-to-fit$' { return (Doc $n) + "`n" + (Arrow 6.5 14 13.5 7 $n 'g' .95) + "`n" + (Arrow 13.5 7 16 4.5 $n 'b' .95) }
        '^breaks$' { return (Doc $n) + "`n" + (L 6 10 14 10 $n 'r' 1) + "`n  <path d=""M$(S 7 $n) $(S 8 $n) L$(S 8.5 $n) $(S 11.8 $n) M$(S 11.5 $n) $(S 8 $n) L$(S 13 $n) $(S 11.8 $n)"" fill=""none"" stroke=""#d83b01"" stroke-width=""$(S 1 $n)"" stroke-linecap=""round"" vector-effect=""non-scaling-stroke""/>" }
        '^print-titles$' { return (Grid $n) + "`n" + (L 3 6.5 17 6.5 $n 'g' 1.2) + "`n" + (L 6.5 3 6.5 17 $n 'b' 1.2) }
        '^header-footer$' { return (Doc $n) + "`n" + (L 6 5 14 5 $n 'b' 1) + "`n" + (L 6 15 14 15 $n 'g' 1) }
        '^page-setup$|^calculation-options$|^options$' { return Gear $n }
        '^print-gridlines$' { return (Grid $n) + "`n" + (Txt $checkGlyph 15 5 4 $n) }
        '^print-headings$' { return (Grid $n) + "`n" + (Txt 'A' 6 5 4 $n) + "`n" + (Txt '1' 4 8 4 $n) }
        '^pivottable$' { return (Grid $n) + "`n  <rect class=""b"" x=""$(S 3 $n)"" y=""$(S 3 $n)"" width=""$(S 14 $n)"" height=""$(S 3.5 $n)"" opacity="".55""/>`n  <rect class=""g2"" x=""$(S 3 $n)"" y=""$(S 6.5 $n)"" width=""$(S 3.5 $n)"" height=""$(S 10.5 $n)"" opacity="".65""/>" }
        '^table$' { return (Grid $n) + "`n  <rect class=""g2"" x=""$(S 3 $n)"" y=""$(S 3 $n)"" width=""$(S 14 $n)"" height=""$(S 3.5 $n)"" opacity="".7""/>" }
        '^show-detail$' { return (Grid $n) + "`n" + (Txt '+' 15 5 7 $n) }
        '^insert-function$' { return (Doc $n) + "`n" + (Txt 'fx' 10.5 10.8 7.2 $n) }
        '^autosum$' { return Txt $sigmaGlyph 10 10 13 $n }
        '^recently-used$' { return "  <circle class=""r"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 6 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (L 10 10 10 6 $n 'r' 1) + "`n" + (L 10 10 14 10 $n 'r' 1) }
        '^math-trig$' { return (Txt '√' 8 10 10 $n) + "`n" + (L 10 14 16 6 $n 's' 1) }
        '^text$' { return Txt 'T' 10 10 12 $n 'font-family="Georgia,serif"' }
        '^date-time$|^date-time-formats$' { return "  <rect class=""f s"" x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 11 $n)"" stroke-width=""$(S 1 $n)""/>`n  <rect class=""b"" x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 3 $n)""/>`n" + (Txt '7' 10 12 6 $n) }
        '^logical$' { return (Txt 'T' 7 8 6 $n) + "`n" + (Txt 'F' 13 13 6 $n) + "`n" + (L 5 11 15 11 $n 's' .8) }
        '^lookup-reference$' { return "  <circle class=""r"" cx=""$(S 8 $n)"" cy=""$(S 8 $n)"" r=""$(S 4 $n)"" stroke-width=""$(S 1.1 $n)""/>`n" + (L 11 11 16 16 $n 'r' 1.1) + "`n" + (Rct 11.5 4 5 5 $n 'f s' .7) }
        '^more-functions$' { return (Txt $sigmaGlyph 7 10 7 $n) + "`n" + (Txt '...' 13 10 6 $n) }
        '^define-name$|^use-in-formula$|^create-from-selection$' { return "  <path class=""f s"" d=""M$(S 4 $n) $(S 6 $n) H$(S 12 $n) L$(S 16 $n) $(S 10 $n) L$(S 12 $n) $(S 14 $n) H$(S 4 $n) Z"" stroke-width=""$(S 1 $n)""/>`n  <circle class=""b"" cx=""$(S 12 $n)"" cy=""$(S 10 $n)"" r=""$(S 1 $n)""/>" }
        '^name-manager$' { return (Doc $n) + "`n" + (Txt 'fx' 9 8 5.5 $n) + "`n" + (L 6 13 14 13 $n 'b' 1) }
        '^trace-precedents$' { return (Txt 'fx' 14 10 5.5 $n) + "`n" + (Arrow 4 10 10 10 $n 'b' 1) }
        '^trace-dependents$' { return (Txt 'fx' 6 10 5.5 $n) + "`n" + (Arrow 10 10 16 10 $n 'b' 1) }
        '^show-formulas$' { return (Txt '=' 6 8 7 $n) + "`n" + (Txt 'fx' 12 12 6 $n) }
        '^error-checking$' { return "  <path class=""y s"" d=""M$(S 10 $n) $(S 3 $n) L$(S 17 $n) $(S 16 $n) H$(S 3 $n) Z"" stroke-width=""$(S 1 $n)""/>`n" + (Txt '!' 10 11 8 $n) }
        '^watch-window$' { return (WindowIcon $n) + "`n" + (Txt 'fx' 10 10 5.5 $n) }
        '^remove-arrows$' { return (Arrow 5 10 15 10 $n 'b' 1) + "`n" + (L 5 5 15 15 $n 'r' 1.2) }
        '^evaluate-formula$' { return (Txt 'fx' 7 9 6 $n) + "`n" + (Arrow 10 14 16 8 $n 'g' 1) }
        '^calculate-now$' { return (Txt 'fx' 8 10 7 $n) + "`n" + (Txt $boltGlyph 14 10 8 $n) }
        '^calculate-sheet$' { return (Grid $n) + "`n" + (Txt $boltGlyph 10 10 8 $n) }
        '^calculation-options$' { return Gear $n }
        '^text-to-columns$' { return (Grid $n) + "`n" + (L 10 3 10 17 $n 'b' 1.4) + "`n" + (Arrow 8 10 5 10 $n 'g' 1) + "`n" + (Arrow 12 10 15 10 $n 'g' 1) }
        '^get-data$' { return (Database $n) + "`n" + (Arrow 14 15 17 12 $n 'g' .9) }
        '^refresh-all$' { return (Database $n) + "`n  <path class=""r"" d=""M$(S 14 $n) $(S 7 $n) A$(S 5 $n) $(S 5 $n) 0 1 0 $(S 15 $n) $(S 13 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (Arrow 13 5 16 7 $n 'g' .9) }
        '^clear-filter$' { return (Funnel $n) + "`n" + (L 6 6 15 15 $n 'r' 1.2) + "`n" + (L 15 6 6 15 $n 'r' 1.2) }
        '^advanced-filter$' { return (Funnel $n) + "`n  <circle class=""r"" cx=""$(S 14 $n)"" cy=""$(S 14 $n)"" r=""$(S 2.5 $n)"" stroke-width=""$(S .9 $n)""/>`n  <circle class=""k"" cx=""$(S 14 $n)"" cy=""$(S 14 $n)"" r=""$(S .8 $n)""/>" }
        '^flash-fill$' { return (Grid $n) + "`n" + (Txt $boltGlyph 10 10 9 $n) }
        '^remove-duplicates$' { return (Rct 5 4 8 11 $n) + "`n" + (Rct 8 6 8 11 $n) + "`n" + (L 6 6 15 15 $n 'r' 1.2) }
        '^data-validation$' { return (Grid $n) + "`n  <path class=""g"" d=""M$(S 6 $n) $(S 11 $n) L$(S 8.5 $n) $(S 13.5 $n) L$(S 14.5 $n) $(S 6.5 $n) L$(S 16 $n) $(S 8 $n) L$(S 8.6 $n) $(S 16 $n) L$(S 4.5 $n) $(S 12 $n) Z""/>" }
        '^consolidate$' { return (Rct 3.5 4 5.5 5.5 $n) + "`n" + (Rct 11 4 5.5 5.5 $n) + "`n" + (Rct 7.2 11 5.6 5.6 $n) + "`n" + (Arrow 8.5 8.5 9.5 10.8 $n 'g' .8) + "`n" + (Arrow 12 8.5 10.5 10.8 $n 'g' .8) }
        '^subtotal$' { return (Grid $n) + "`n" + (Txt $sigmaGlyph 14 14 6 $n) + "`n" + (L 5 14 11 14 $n 'b' 1) }
        '^scenario-manager$|^what-if-analysis$' { return (Rct 4 5 5 5 $n) + "`n" + (Rct 11 5 5 5 $n) + "`n" + (Rct 7.5 12 5 5 $n) + "`n" + (Txt '?' 10 9 5 $n) }
        '^forecast-sheet$' { return (Doc $n) + "`n" + (Chart 'line-chart' $n) }
        '^data-table$' { return (Grid $n) + "`n" + (Rct 11 11 5 5 $n 'b' 0) }
        '^group$|^ungroup$' { return "  <path class=""r"" d=""M$(S 7 $n) $(S 4 $n) C$(S 4.8 $n) $(S 4 $n) $(S 5 $n) $(S 7 $n) $(S 5 $n) $(S 10 $n) C$(S 5 $n) $(S 13 $n) $(S 4.8 $n) $(S 16 $n) $(S 7 $n) $(S 16 $n)"" stroke-width=""$(S 1 $n)""/>`n  <path class=""r"" d=""M$(S 13 $n) $(S 4 $n) C$(S 15.2 $n) $(S 4 $n) $(S 15 $n) $(S 7 $n) $(S 15 $n) $(S 10 $n) C$(S 15 $n) $(S 13 $n) $(S 15.2 $n) $(S 16 $n) $(S 13 $n) $(S 16 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (Txt '+' 10 10 7 $n) }
        '^hide-detail$|^collapse$' { return (Grid $n) + "`n" + (Txt '-' 15 5 8 $n) }
        '^show-detail$|^expand$' { return (Grid $n) + "`n" + (Txt '+' 15 5 8 $n) }
        '^bring-forward$' { return "  <rect class=""b"" x=""$(S 4.5 $n)"" y=""$(S 8 $n)"" width=""$(S 8 $n)"" height=""$(S 8 $n)""/>`n" + (Rct 8 4 8 8 $n) }
        '^send-backward$' { return (Rct 4.5 8 8 8 $n) + "`n  <rect class=""b"" x=""$(S 8 $n)"" y=""$(S 4 $n)"" width=""$(S 8 $n)"" height=""$(S 8 $n)""/>" }
        '^object-size$' { return (Rct 5 6 10 8 $n) + "`n" + (Arrow 5 16 15 16 $n 'b' .9) + "`n" + (Arrow 16 14 16 6 $n 'b' .9) }
        '^object-rotate$|^rotate$' { return "  <path class=""r"" d=""M$(S 14.5 $n) $(S 6.5 $n) A$(S 5.2 $n) $(S 5.2 $n) 0 1 0 $(S 15 $n) $(S 13.2 $n)"" stroke-width=""$(S 1.1 $n)""/>`n" + (Arrow 12 4.5 16.5 6.5 $n 'g' 1) }
        '^selection-pane$' { return (WindowIcon $n) + "`n" + (L 5 6 11 6 $n 'b' .8) + "`n" + (L 5 9 9 9 $n 'b' .8) }
        '^object-outline$|^outline-color$|^outline$' { return "  <rect x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 10 $n)"" fill=""none"" stroke=""#5b9bd5"" stroke-width=""$(S 1.4 $n)"" vector-effect=""non-scaling-stroke""/>`n  <rect class=""rd"" x=""$(S 5 $n)"" y=""$(S 16 $n)"" width=""$(S 10 $n)"" height=""$(S 1.6 $n)""/>" }
        '^object-fill$|^fill$' { return "  <rect class=""y"" x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 10 $n)""/>`n" + (Rct 4 5 12 10 $n 's' 1) }
        '^shape-gradient$|^gradient$' { return "  <defs><linearGradient id=""grad"" x1=""0"" x2=""1""><stop offset=""0"" stop-color=""#5b9bd5""/><stop offset=""1"" stop-color=""#ffffff""/></linearGradient></defs>`n  <rect x=""$(S 4 $n)"" y=""$(S 5 $n)"" width=""$(S 12 $n)"" height=""$(S 10 $n)"" fill=""url(#grad)"" stroke=""#1f1f1f"" stroke-width=""$(S 1 $n)"" vector-effect=""non-scaling-stroke""/>" }
        '^crop$' { return "  <rect class=""f s"" x=""$(S 5 $n)"" y=""$(S 5 $n)"" width=""$(S 10 $n)"" height=""$(S 10 $n)"" stroke-width=""$(S .8 $n)""/>`n  <path class=""s"" d=""M$(S 6.5 $n) $(S 2.8 $n) V$(S 13.5 $n) H$(S 17.2 $n) M$(S 2.8 $n) $(S 6.5 $n) H$(S 13.5 $n) V$(S 17.2 $n)"" stroke-width=""$(S 1.2 $n)""/>" }
        '^object-effects$|^effects$' { return (Txt $sparkleGlyph 9 8.5 10 $n) + "`n" + (Txt $sparkleGlyph 14.5 14 5 $n) }
        '^normal$' { return (Grid $n) + "`n  <rect class=""g2"" x=""$(S 3 $n)"" y=""$(S 3 $n)"" width=""$(S 14 $n)"" height=""$(S 3.5 $n)"" opacity="".6""/>" }
        '^custom-views$' { return (WindowIcon $n) + "`n" + (Txt $starGlyph 14 6 4.5 $n) }
        '^new-window$' { return WindowIcon $n }
        '^arrange-all$' { return (Rct 4 4 6 6 $n) + "`n" + (Rct 10 4 6 6 $n) + "`n" + (Rct 4 10 6 6 $n) + "`n" + (Rct 10 10 6 6 $n) }
        '^view-side-by-side$' { return (Rct 4 5 5.5 10 $n) + "`n" + (Rct 10.5 5 5.5 10 $n) }
        '^synchronous-scrolling$' { return (Rct 4 5 5.5 10 $n) + "`n" + (Rct 10.5 5 5.5 10 $n) + "`n" + (Arrow 7 8 13 8 $n 'b' .8) + "`n" + (Arrow 13 12 7 12 $n 'b' .8) }
        '^reset-window-position$' { return (WindowIcon $n) + "`n" + (Arrow 14 5 9 5 $n 'g' .8) + "`n" + (Arrow 6 15 11 15 $n 'g' .8) }
        '^switch-windows$' { return (WindowIcon $n) + "`n" + (Arrow 6 16 14 16 $n 'b' .8) }
        '^page-break-preview$' { return (Grid $n) + "`n" + (L 10 3 10 17 $n 'b' 1) + "`n" + (L 3 10 17 10 $n 'b' 1) + "`n" + (L 6.5 3 6.5 17 $n 'r' .6) }
        '^page-layout$' { return (Doc $n) + "`n" + (Rct 7 7 6 6 $n 's' .6) + "`n" + (L 9 7 9 13 $n 's' .5) + "`n" + (L 11 7 11 13 $n 's' .5) + "`n" + (L 7 9 13 9 $n 's' .5) + "`n" + (L 7 11 13 11 $n 's' .5) }
        '^freeze-panes$' { return (Grid $n) + "`n" + (L 8 3 8 17 $n 'r' 1.2) + "`n" + (L 3 8 17 8 $n 'r' 1.2) + "`n  <rect class=""b"" x=""$(S 3 $n)"" y=""$(S 3 $n)"" width=""$(S 5 $n)"" height=""$(S 5 $n)"" opacity="".35""/>" }
        '^split$' { return (Grid $n) + "`n" + (L 10 3 10 17 $n 'b' 1.2) }
        '^zoom-to-selection$' { return (Grid $n) + "`n  <rect x=""$(S 6 $n)"" y=""$(S 6 $n)"" width=""$(S 6 $n)"" height=""$(S 5 $n)"" fill=""none"" stroke=""#5b9bd5"" stroke-width=""$(S 1.2 $n)""/>`n  <circle class=""r"" cx=""$(S 13 $n)"" cy=""$(S 13 $n)"" r=""$(S 3 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (L 15 15 18 18 $n 'r' 1) }
        '^hide-unhide$' { return Eye $n }
        '^statistics$|^workbook-stats$|^workbook-statistics$' { return "  <rect class=""b"" x=""$(S 5 $n)"" y=""$(S 10 $n)"" width=""$(S 2.5 $n)"" height=""$(S 6 $n)""/><rect class=""k"" x=""$(S 9 $n)"" y=""$(S 6 $n)"" width=""$(S 2.5 $n)"" height=""$(S 10 $n)""/><rect class=""g"" x=""$(S 13 $n)"" y=""$(S 3.5 $n)"" width=""$(S 2.5 $n)"" height=""$(S 12.5 $n)""/>" }
        '^accessibility-checker$|^accessibility$' { return "  <path class=""y s"" d=""M$(S 10 $n) $(S 3 $n) L$(S 17 $n) $(S 16 $n) H$(S 3 $n) Z"" stroke-width=""$(S 1 $n)""/>`n" + (Txt '!' 10 11 8 $n) }
        '^alt-text$' { return (Rct 4 5 12 10 $n) + "`n" + (Txt 'alt' 10 10 5.2 $n) }
        '^allow-edit-ranges$' { return (Grid $n) + "`n  <path class=""g"" d=""M$(S 7 $n) $(S 11 $n) L$(S 9 $n) $(S 13 $n) L$(S 14 $n) $(S 7 $n) L$(S 15.5 $n) $(S 8.5 $n) L$(S 9.2 $n) $(S 15.5 $n) L$(S 5.5 $n) $(S 12 $n) Z""/>" }
        '^share$' { return "  <circle class=""b"" cx=""$(S 6 $n)"" cy=""$(S 7 $n)"" r=""$(S 2 $n)""/><circle class=""b"" cx=""$(S 14 $n)"" cy=""$(S 6 $n)"" r=""$(S 2 $n)""/><circle class=""b"" cx=""$(S 13 $n)"" cy=""$(S 14 $n)"" r=""$(S 2 $n)""/>`n" + (L 8 7 12 6 $n 'r' .9) + "`n" + (L 8 8 11.5 13 $n 'r' .9) }
        '^help$' { return "  <circle class=""r"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 7 $n)"" stroke=""#2f5597"" stroke-width=""$(S 1.2 $n)""/>`n" + (Txt '?' 10 10.4 12 $n) }
        '^about$|^info$' { return "  <circle class=""r"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" r=""$(S 7 $n)"" stroke=""#2f5597"" stroke-width=""$(S 1.2 $n)""/>`n" + (Txt 'i' 10 10.5 13 $n) }
        '^send-feedback$' { return "  <path class=""f s"" d=""M$(S 4 $n) $(S 5 $n) H$(S 16 $n) V$(S 13 $n) H$(S 10 $n) L$(S 6 $n) $(S 16 $n) V$(S 13 $n) H$(S 4 $n) Z"" stroke-width=""$(S 1 $n)""/>`n" + (Txt '!' 10 9.5 7 $n) }
        'chart|sparkline' { return Chart $slug $n }
        'pivot|table|grid|cell|row|column|border|format' { return Grid $n }
        'theme|color|font|margin|print|paper|page|scale|header|footer|break|background|effect|option' { if ($slug -match 'colors') { return "  <rect class=""b"" x=""$(S 4 $n)"" y=""$(S 4 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/><rect class=""o"" x=""$(S 8 $n)"" y=""$(S 4 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/><rect class=""g"" x=""$(S 12 $n)"" y=""$(S 4 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/><rect class=""y"" x=""$(S 4 $n)"" y=""$(S 8 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/><rect class=""rd"" x=""$(S 8 $n)"" y=""$(S 8 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/><rect class=""p"" x=""$(S 12 $n)"" y=""$(S 8 $n)"" width=""$(S 4 $n)"" height=""$(S 4 $n)""/>" }; return Doc $n }
        'formula|function|sum|calculate|precedent|dependent|error|watch|arrow|name|selection|math|logical|lookup|financial|date-time|text' { if ($slug -match 'sum') { return Txt $sigmaGlyph 10 10 13 $n }; return (Doc $n) + "`n" + (Txt 'fx' 10.5 11 6.2 $n) }
        'sort|filter|data|refresh|duplicate|validation|consolidate|scenario|forecast|subtotal|group|ungroup|detail|goal|flash|columns' { if ($slug -match 'filter') { return "  <path class=""f s"" d=""M$(S 3 $n) $(S 4 $n) H$(S 17 $n) L$(S 11.5 $n) $(S 10.5 $n) V$(S 16 $n) L$(S 8.5 $n) $(S 17 $n) V$(S 10.5 $n) Z"" stroke-width=""$(S 1 $n)""/>" }; if ($slug -match 'sort-ascending') { return (Txt 'A' 6 6 5 $n) + "`n" + (Txt 'Z' 6 14 5 $n) + "`n" + (Arrow 13 15 13 5 $n 'g' 1) }; if ($slug -match 'sort-descending') { return (Txt 'Z' 6 6 5 $n) + "`n" + (Txt 'A' 6 14 5 $n) + "`n" + (Arrow 13 5 13 15 $n 'g' 1) }; if ($slug -match 'refresh') { return "  <path class=""r"" d=""M$(S 15 $n) $(S 7 $n) A$(S 6 $n) $(S 6 $n) 0 1 0 $(S 16 $n) $(S 13 $n)"" stroke-width=""$(S 1 $n)""/>`n" + (Arrow 14 5 17 7 $n 'g' 1) }; return Grid $n }
        'spell|statistic|access|alt-text|comment|note|protect|share' { if ($slug -match 'protect') { return "  <path class=""f s"" d=""M$(S 5 $n) $(S 9 $n) H$(S 15 $n) V$(S 17 $n) H$(S 5 $n) Z M$(S 7 $n) $(S 9 $n) V$(S 6.7 $n) C$(S 7 $n) $(S 3.8 $n) $(S 13 $n) $(S 3.8 $n) $(S 13 $n) $(S 6.7 $n) V$(S 9 $n)"" stroke-width=""$(S 1 $n)""/>" }; if ($slug -match 'comment|note') { return "  <path class=""y s"" d=""M$(S 4 $n) $(S 5 $n) H$(S 16 $n) V$(S 13 $n) H$(S 10 $n) L$(S 6 $n) $(S 16 $n) V$(S 13 $n) H$(S 4 $n) Z"" stroke-width=""$(S 1 $n)""/>" }; return Doc $n }
        'rectangle|ellipse|line|shape|text-box|bring|send|size|rotate|selection-pane|outline|fill|crop|gradient' { if ($slug -match 'ellipse') { return "  <ellipse class=""f s"" cx=""$(S 10 $n)"" cy=""$(S 10 $n)"" rx=""$(S 6 $n)"" ry=""$(S 4.5 $n)"" stroke-width=""$(S 1 $n)""/>" }; if ($slug -eq 'line') { return L 4 15 16 5 $n 'r' 1 }; if ($slug -match 'text-box') { return (Rct 4 5 12 10 $n) + "`n" + (Txt 'T' 10 10 8 $n) }; if ($slug -match 'crop') { return "  <path class=""s"" d=""M$(S 6 $n) $(S 3 $n) V$(S 14 $n) H$(S 17 $n) M$(S 3 $n) $(S 6 $n) H$(S 14 $n) V$(S 17 $n)"" stroke-width=""$(S 1.2 $n)""/>" }; return Rct 4 5 12 10 $n }
        'normal|view|zoom|window|arrange|side-by-side|scrolling|freeze|ruler|headings' { if ($slug -match 'zoom') { return "  <circle class=""r"" cx=""$(S 8.5 $n)"" cy=""$(S 8.5 $n)"" r=""$(S 5 $n)"" stroke-width=""$(S 1.2 $n)""/>`n" + (L 12 12 17 17 $n 'r' 1.2) }; return Grid $n }
        default { return Doc $n }
    }
}

foreach ($file in $baseFiles) {
    foreach ($size in 20, 32) {
        $suffix = if ($size -eq 20) { 'small' } else { 'large' }
        Set-Content -Path (Join-Path $iconDir "$($file.BaseName)-$suffix.svg") -Encoding UTF8 -Value (Svg $size (Body $file.BaseName $size))
    }
}

Write-Host "Generated $($baseFiles.Count * 2) native ribbon SVG variants."
