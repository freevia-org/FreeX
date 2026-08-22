# Third-Party Notices

This file summarizes third-party NuGet packages referenced by the FreeX
solution after restore on 2026-08-22. Each package remains governed by its own
license. This notice does not change those license terms.

See [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) for bundled common
license text and package-provided license text found in the restored packages.

## Audit Status

- Audit command: `dotnet restore FreeX.slnx --disable-parallel -v:minimal`.
- Restored package inventory: 97 unique package names (99 name/version
  identities) across 119 `project.assets.json` files.
- Coverage: every restored package is listed below.
- Runtime package posture: the publishable app dependency set is covered by
  MIT, Apache-2.0, BSD-3-Clause, BSD-style package licenses, package license
  files, LGPL-2.1-or-later, and LGPL-3.0-only where listed below.
- Package-provided `NOTICE` files found in the local NuGet cache:
  Microsoft.NET.ILLink.Tasks `THIRD-PARTY-NOTICES.TXT`,
  System.Security.Cryptography.Pkcs `THIRD-PARTY-NOTICES.TXT`, and
  System.Security.Cryptography.Xml `THIRD-PARTY-NOTICES.TXT`.
- Package-provided license files found: Avalonia.Angle.Windows.Natives
  `LICENSE`, BouncyCastle.Cryptography `LICENSE.md`, FluentAssertions
  `LICENSE`, Newtonsoft.Json `LICENSE.md`, NPOI `LICENSE`, SharpVectors.Wpf
  `lib/License.txt`, and System.IO.Packaging `LICENSE.TXT`.

## Commercial-Use Note

FluentAssertions 8.9.0 is a test/development dependency only; it is not part of
the application runtime publish output. Versions 8 and later require a paid
license for commercial use. Before using the test suite in a commercial
organization or distributing it for commercial use, replace this dependency,
use a suitably licensed version, or confirm the required license has been
obtained.

## Runtime Packages

| Package | Version | License | Project |
| --- | --- | --- | --- |
| AngleSharp | 1.5.1 | MIT | https://anglesharp.github.io/ |
| Avalonia | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-style package license file | https://avaloniaui.net/ |
| Avalonia.BuildServices | 11.3.2 | MIT | https://avaloniaui.net/ |
| Avalonia.Desktop | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Fonts.Inter | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.FreeDesktop | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.FreeDesktop.AtSpi | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.HarfBuzz | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Headless | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Native | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Remote.Protocol | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Skia | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Themes.Fluent | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.Win32 | 12.0.4 | MIT | https://avaloniaui.net/ |
| Avalonia.X11 | 12.0.4 | MIT | https://avaloniaui.net/ |
| BouncyCastle.Cryptography | 2.6.2 | MIT | https://www.bouncycastle.org/stable/nuget/csharp/website |
| ClosedXML | 0.105.0 | MIT | https://github.com/ClosedXML/ClosedXML |
| ClosedXML.Parser | 2.0.0 | MIT | https://github.com/ClosedXML/ClosedXML.Parser |
| DocSharp.Binary.Common | 0.20.0 | MIT | https://github.com/manfromarce/DocSharp |
| DocSharp.Binary.Doc | 0.20.0 | MIT | https://github.com/manfromarce/DocSharp |
| DocumentFormat.OpenXml | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| Enums.NET | 5.0.0 | MIT | https://github.com/TylerBrinkley/Enums.NET |
| ExcelDataReader | 3.8.0 | MIT | https://github.com/ExcelDataReader/ExcelDataReader |
| ExcelNumberFormat | 1.1.0 | MIT | https://github.com/andersnm/ExcelNumberFormat |
| ExtendedNumerics.BigDecimal | 2025.1001.2.129 | MIT | https://www.nuget.org/packages/ExtendedNumerics.BigDecimal/ |
| HarfBuzzSharp | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| LibVLCSharp | 3.10.0 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| LibVLCSharp.Avalonia | 3.10.0 | LGPL-2.1-or-later | https://code.videolan.org/videolan/LibVLCSharp |
| MathNet.Numerics.Signed | 5.0.0 | MIT | https://numerics.mathdotnet.com/ |
| Microsoft.Extensions.DependencyInjection | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Primitives | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | MIT | https://github.com/Microsoft/Microsoft.IO.RecyclableMemoryStream |
| Microsoft.Win32.SystemEvents | 10.0.0 | MIT | https://dot.net/ |
| MicroCom.Runtime | 0.11.4 | MIT |  |
| MimeKit | 4.17.0 | MIT | https://github.com/jstedfast/MimeKit |
| NPOI | 2.7.6 | Apache-2.0 | https://github.com/nissl-lab/npoi |
| NSax | 1.0.2 | LGPL-3.0-only | https://github.com/antony-liu/NSax |
| OxyPlot.Core | 2.2.0 | MIT | https://oxyplot.github.io/ |
| OxyPlot.Wpf | 2.2.0 | MIT | https://oxyplot.github.io/ |
| OxyPlot.Wpf.Shared | 2.2.0 | MIT | https://oxyplot.github.io/ |
| PDFsharp-WPF | 6.2.4 | MIT | https://docs.pdfsharp.net/ |
| RBush.Signed | 4.0.0 | MIT |  |
| Sentry | 6.5.0 | MIT | https://sentry.io/ |
| Serilog | 4.3.1 | Apache-2.0 | https://serilog.net/ |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | https://github.com/serilog/serilog-extensions-logging |
| Serilog.Sinks.Console | 6.1.1 | Apache-2.0 | https://github.com/serilog/serilog-sinks-console |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |
| SharpVectors.Wpf | 1.8.5 | BSD-3-Clause | https://github.com/ElinamLLC/SharpVectors |
| SharpZipLib | 1.4.2 | MIT | https://github.com/icsharpcode/SharpZipLib |
| SixLabors.Fonts | 1.0.1 | Apache-2.0 | https://github.com/SixLabors/Fonts |
| SixLabors.ImageSharp | 2.1.11 | Apache-2.0 | https://github.com/SixLabors/ImageSharp |
| SkiaSharp | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT | https://go.microsoft.com/fwlink/?linkid=868515 |
| System.IO.Packaging | 8.0.1 | MIT | https://dot.net/ |
| System.Drawing.Common | 10.0.0 | MIT | https://dot.net/ |
| System.Security.Cryptography.Pkcs | 10.0.10 | MIT | https://dot.net/ |
| System.Security.Cryptography.Xml | 10.0.10 | MIT | https://dot.net/ |
| System.Speech | 10.0.0 | MIT | https://dot.net/ |
| Tmds.DBus.Protocol | 0.92.0 | MIT |  |
| UglyToad.PdfPig | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| UglyToad.PdfPig.Core | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| UglyToad.PdfPig.DocumentLayoutAnalysis | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| UglyToad.PdfPig.Fonts | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| UglyToad.PdfPig.Tokenization | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| UglyToad.PdfPig.Tokens | 1.7.0-custom-5 | Apache-2.0 | https://github.com/UglyToad/PdfPig |
| Velopack | 1.2.0 | MIT | https://github.com/velopack/velopack |
| VideoLAN.LibVLC.Windows | 3.0.23.1 | LGPL-2.1-or-later | https://www.videolan.org/vlc/libvlc.html |
| ZString | 2.6.0 | MIT | https://github.com/Cysharp/ZString |

## Test And Development Packages

| Package | Version | License | Project |
| --- | --- | --- | --- |
| coverlet.collector | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet |
| FluentAssertions | 8.9.0 | Package license file | https://xceed.com/products/unit-testing/fluent-assertions/ |
| Microsoft.CodeCoverage | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| Microsoft.NET.ILLink.Tasks | 10.0.8 | MIT | https://dot.net/ |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| Microsoft.TestPlatform.ObjectModel | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| Microsoft.TestPlatform.TestHost | 17.14.1 | MIT | https://github.com/microsoft/vstest |
| Newtonsoft.Json | 13.0.3 | MIT | https://www.newtonsoft.com/json |
| xunit | 2.9.3 | Apache-2.0 |  |
| xunit.abstractions | 2.0.3 | Package license URL | https://github.com/xunit/xunit |
| xunit.analyzers | 1.18.0 | Apache-2.0 |  |
| xunit.assert | 2.9.3 | Apache-2.0 |  |
| xunit.core | 2.9.3 | Apache-2.0 |  |
| xunit.extensibility.core | 2.9.3 | Apache-2.0 |  |
| xunit.extensibility.execution | 2.9.3 | Apache-2.0 |  |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |  |
| Xunit.StaFact | 1.2.69 | MS-PL | https://github.com/AArnott/Xunit.StaFact |

## Common License Texts

- MIT, Apache License 2.0, package-provided BSD/additional license text, and
  package-provided third-party notice text are bundled in
  [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).
- LGPL-2.1-or-later, LGPL-3.0-only, GPL-3.0-only (incorporated by LGPL 3.0),
  and MS-PL texts are bundled under [docs/legal/licenses](docs/legal/licenses).

## LGPL Runtime Distribution Requirements

FreeP includes LibVLCSharp and the VideoLAN LibVLC Windows runtime under
LGPL-2.1-or-later. FreeX's legacy import dependency graph includes NSax under
LGPL-3.0-only. Binary distributions must preserve the applicable notices and
license texts, permit replacement/relinking as required by the applicable
LGPL, and provide the corresponding-source or written-offer materials required
for any LGPL-covered binaries that are distributed. Release engineering must
verify the exact native LibVLC bundle and source-offer obligations for every
target platform; listing the packages here is not by itself sufficient.

Some package licenses are provided as files or legacy license URLs inside the
NuGet package metadata. Preserve those package-provided notices when
redistributing a binary bundle that includes the package.
