# Third-Party Notices

This file summarizes third-party NuGet packages referenced by the FreeX
solution after restore on 2026-05-30. Each package remains governed by its own
license. This notice does not change those license terms.

See [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) for bundled common
license text and package-provided license text found in the restored packages.

## Audit Status

- Audit command: `dotnet restore FreeX.slnx --disable-parallel -v:minimal`.
- Restored package inventory: 40 unique NuGet packages across 15
  `project.assets.json` files.
- Coverage: every restored package is listed below.
- Runtime package posture: the publishable app dependency set is covered by
  MIT, Apache-2.0, and BSD-3-Clause style licenses.
- Package-provided `NOTICE` files found in the local NuGet cache: none.
- Package-provided license files found: FluentAssertions `LICENSE`,
  Newtonsoft.Json `LICENSE.md`, SharpVectors.Wpf `lib/License.txt`, and
  System.IO.Packaging `LICENSE.TXT`.

## Commercial-Use Note

FluentAssertions 8.9.0 is a test/development dependency only; it is not part of
the FreeX runtime publish output. Its package metadata points to the Xceed
Community License, which is limited to non-commercial use unless a commercial
license is obtained. If FreeX source/tests are distributed for commercial use,
replace this dependency or confirm the project has the required Xceed license.

## Runtime Packages

| Package | Version | License | Project |
| --- | --- | --- | --- |
| ClosedXML | 0.105.0 | MIT | https://github.com/ClosedXML/ClosedXML |
| ClosedXML.Parser | 2.0.0 | MIT | https://github.com/ClosedXML/ClosedXML.Parser |
| DocumentFormat.OpenXml | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| DocumentFormat.OpenXml.Framework | 3.1.1 | MIT | https://github.com/dotnet/Open-XML-SDK |
| ExcelDataReader | 3.8.0 | MIT | https://github.com/ExcelDataReader/ExcelDataReader |
| ExcelNumberFormat | 1.1.0 | MIT | https://github.com/andersnm/ExcelNumberFormat |
| Microsoft.Extensions.DependencyInjection | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Logging.Abstractions | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Options | 10.0.7 | MIT | https://dot.net/ |
| Microsoft.Extensions.Primitives | 10.0.7 | MIT | https://dot.net/ |
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
| SixLabors.Fonts | 1.0.0 | Apache-2.0 | https://github.com/SixLabors/Fonts |
| System.IO.Packaging | 8.0.1 | MIT | https://dot.net/ |

## Test And Development Packages

| Package | Version | License | Project |
| --- | --- | --- | --- |
| coverlet.collector | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet |
| FluentAssertions | 8.9.0 | Package license file | https://xceed.com/products/unit-testing/fluent-assertions/ |
| Microsoft.CodeCoverage | 17.14.1 | MIT | https://github.com/microsoft/vstest |
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

## Common License Texts

- MIT, Apache License 2.0, and package-provided BSD/additional license text are
  bundled in [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md).

Some package licenses are provided as files or legacy license URLs inside the
NuGet package metadata. Preserve those package-provided notices when
redistributing a binary bundle that includes the package.
