# Library research

Environment: Windows 11 10.0.26200, .NET SDK 10.0.404.

This document records the libraries that are actually shipped with the application and the API checks performed before adopting them.

## Shipped dependencies

| Package | Version | License | Why it is used |
| --- | --- | --- | --- |
| `AsmResolver.PE` + `AsmResolver.PE.File` | 6.1.0 | MIT | Managed PE parser with imports, exports, CLR, resources, and lazy reading. |
| `Iced` | 1.22.0 | MIT | Managed x86/x64 decoder with Intel formatter and encoder API. |
| `Dirkster.AvalonDock` | 5.0.2 | Ms-PL | Native `net10.0-windows` docking with floating panels and resizing. |
| `Dirkster.AvalonDock.Themes.VS` | 5.0.2 | Ms-PL | VS2022-style docking theme. |
| `CommunityToolkit.Mvvm` | 8.4.2 | MIT | Source-generated `ObservableProperty` and `RelayCommand`. |
| `MahApps.Metro.IconPacks.Codicons` | 6.2.1 | MIT (package) / CC BY 4.0 (icons) | Consistent developer-tool icon set. |
| `Microsoft.Extensions.DependencyInjection` | 10.0.15 | MIT | Application composition. |
| `Microsoft.Data.Sqlite` | 10.0.15 | MIT / SQLite public domain | Project database without an ORM. |
| `Serilog` + `Sinks.File` + `Sinks.Debug` | 4.4.0 / 7.0.0 / 3.0.0 | Apache-2.0 | Structured logging and UI sink. |
| `xunit` + `Microsoft.NET.Test.Sdk` | 2.9.3 / 18.12.0 | Apache-2.0 / MIT | Unit tests. |

**AsmResolver note:** the `AsmResolver` meta-package in 6.1.0 does not contain PE parsing. The project references `AsmResolver.PE` and `AsmResolver.PE.File` directly.

## Verified APIs

- **Resources:** `image.Resources`, `ResourceDirectory`, `ResourceData.CreateReader()`, RVA and file offset.
- **Relocations:** `image.Relocations`, `BaseRelocation.Type`, `Location`.
- **TLS:** `tls.CallbackFunctions`, `tls.Index`.
- **Exceptions:** `image.Exceptions.GetFunctions()` with `Begin`, `End`, and `UnwindInfo`.
- **Debug directory:** 28-byte entries are parsed manually; CodeView RSDS/NB10 paths use fixed offsets.
- **Load configuration:** `PEImage.LoadConfiguration`, `GuardFlags`.
- **Iced:** `Decoder.Create`, `decoder.IP`, `Instruction.FlowControl`, `NearBranchTarget`, `IsIPRelativeMemoryOperand`, `IntelFormatter`, and `IcedPreview`.
- **Analysis:** custom seed-based traversal over `DisassemblyImage`; no external graph library.
- **CFG:** WPF `Canvas`, `StreamGeometry`, and scale/translate transforms.
- **Call graph:** custom deterministic layout on a WPF `Canvas`.
- **SQLite:** `SqliteConnection`, parameterized SQL, transactions, WAL, schema version 2.
- **Patching:** `BinaryImage.WithBytes` and `IcedPreview`; same-length overlay and persisted operations.
- **Analysis browser:** `ListCollectionView` plus a virtualized `ListBox`.
- **Byte search:** custom `ByteSearch` in Core.

## Technical findings

- .NET 10 LTS and `Microsoft.WindowsDesktop.App 10.0.15` are available.
- AvalonDock 5.0.2 ships `lib/net10.0-windows7.0` and the VS2022 theme.
- AsmResolver 6.1.0 ships `lib/net10.0`; PE parsing is split across packages.
- The VS2022 AvalonDock theme draws a tiled grip on tool-window captions; brushes are replaced with transparent ones in `MainWindow.CreateDockTheme`.
- `WindowChrome` and a custom title bar use Codicons caption buttons.
- WPF screens are verified through the deterministic `--capture`/`RenderTargetBitmap` harness.
- Iced's `IntelFormatter` appends an `h` suffix; with `HexPrefix="0x"` the suffix must be empty and `ShowBranchSize` disabled.
- A linear sweep may not have a branch target exactly on an instruction boundary; navigation therefore selects the exact or nearest instruction and reports it.
- Notepad has about 47,000 instructions in two executable sections; kernel32 has about 170,000.
- Seed-based analysis finds 453 functions, 10,039 xrefs, and 1,624 strings in notepad; kernel32 yields 2,657 functions, 30,941 xrefs, and 13,772 strings.
- Strings are scanned only from non-executable sections to avoid false positives from code bytes.
- A WPF `Canvas` handles 10–30 CFG blocks comfortably; top-down layout is more readable than a compact left-to-right layout at desktop sizes.

## Licensing notes

- Codicons are CC BY 4.0 and require attribution; this is shown in About and in `THIRD-PARTY-NOTICES.md`.
- AvalonDock is Ms-PL; its notice is included.
- AsmResolver, Iced, CommunityToolkit, Microsoft.Extensions, and Microsoft.Data.Sqlite are MIT; Serilog and xUnit are Apache-2.0; SQLite is public domain.
- All packages restore from `nuget.org`.

## Decisions

1. WPF uses a custom design system rather than a web component library.
2. AvalonDock with custom VS palette provides the docking foundation.
3. AsmResolver is the PE engine; split packages are required.
4. Iced is the x86/x64 decoder: managed, reasonably fast, and sufficient for large native binaries.
5. Seed-based analysis plus custom WPF graphs avoids additional dependencies.
6. Microsoft.Data.Sqlite is sufficient for projects with thousands of annotations and patch operations.
7. Same-length patching avoids PE layout changes and can be validated with one Iced decode.
8. A virtualized WPF list handles 10,000+ xrefs without a third-party table library.
