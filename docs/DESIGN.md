# Design system

## Direction

Obsidian Disassembler is a dense, dark-first desktop IDE, not a web dashboard. The reference points are Visual Studio 2022, modern IDA, and Binary Ninja: compact rows, restrained color, monospace data, clear hierarchy, and no decorative motion.

Principles:

1. **Data first.** Chrome recedes; addresses, bytes, and instructions carry contrast.
2. **One accent.** Blue is reserved for focus, selection, and active state.
3. **Dense but readable.** 20 px table rows, 11–12 px data text, 24 px controls.
4. **No fake chrome.** Every visible control is functional.
5. **Consistent themes.** Dark and light use the same semantic tokens.

## Color tokens

All colors are semantic `SolidColorBrush` resources in `Themes/Palette.Dark.xaml` and `Palette.Light.xaml`. Components reference them through `DynamicResource`, so theme changes do not rebuild the visual tree.

Core dark tokens:

| Token | Role |
| --- | --- |
| `Brush.Window` | Title bar / window background |
| `Brush.Chrome` | Menu bar, toolbars, dialogs |
| `Brush.Panel` | Panels and cards |
| `Brush.Editor` | Hex, disassembly, graphs |
| `Brush.Border` / `Brush.BorderSubtle` | Structural separators |
| `Brush.Text` | Primary text |
| `Brush.TextMuted` | Addresses, metadata, captions |
| `Brush.Accent` | Focus/active accent |
| `Brush.Selected` | Selected row/node |
| `Brush.Hover` | Hover state |
| `Brush.Info` | Branch targets, links |
| `Brush.Warning` | Back edges, cautions |
| `Brush.Danger` | Invalid data and errors |

Light theme uses the same semantic roles with a light surface hierarchy; all data views are verified in both themes.

## Typography

| Usage | Family | Size |
| --- | --- | --- |
| UI labels | Segoe UI Variable / Segoe UI | 12 px |
| Section labels | Segoe UI Semibold, uppercase, letter-spaced | 10 px |
| Captions | Segoe UI | 11 px |
| Addresses, bytes, instructions | Cascadia Mono | 11–12 px |
| Graph nodes | Cascadia Mono | 11–12 px |

Monospace values use tabular alignment. Long paths, strings, and labels trim with an ellipsis and expose the full value in a tooltip.

## Spacing and layout

- 4/8/12/16 px spacing scale.
- 34 px title bar, 26 px menu row, 32 px command bar, 24 px status bar.
- 288 px Solution Explorer, 304 px Properties, 210 px bottom panel by default.
- 20 px table rows; 23–24 px group/tree rows.
- 1 px borders; no shadows except OS-managed window chrome.
- Minimum window size is 900 × 600; 1280 × 800 is the reference compact layout.

## Components

| Component | Behavior |
| --- | --- |
| Title bar | Custom `WindowChrome`, logo, binary/project identity, search, theme toggle, window controls |
| Menu bar | File, Edit, View, Analysis, Navigation, Tools, Window, Help; all items functional |
| Command bar | Open, Save project, Reload/Analyze, Cancel, Go To, palette, back/forward, panel toggles, Settings |
| Solution Explorer | Filter, refresh, collapse, tree icons/details, empty state |
| Overview | Identity, badges, entry point, memory map, sections, data directories, findings |
| Hex editor | Inline find, Go To, address modes, copy, selection, virtualized rows |
| Disassembly | Go To, entry point, follow branch, copy, progress/cancel, virtualized rows |
| Analysis browser | Virtualized Functions/Strings/Cross references tabs with a filter |
| CFG | Top-down basic blocks, edges, back-edge color, fit/zoom/pan |
| Call graph | Callers above, selected function center, callees below; click to recenter |
| Projects | Project identity, annotations, comments, bookmarks, save prompt |
| Patch dialog | Hex input, Iced preview, same-length validation, disabled Apply when invalid |
| Properties | Grouped sheet, monospace values, wrapping, scroll |
| Output | Virtualized log, auto-scroll, clear/copy/open folder |
| Dialog | Custom chrome, consistent tokens, keyboard defaults |

## Data views

### Hex editor

- Four bands: find/navigation, address/selection status, column header, virtualized rows.
- Address is muted, bytes primary, ASCII secondary.
- 20 px rows, no zebra striping.
- Copy produces an offset + hex + ASCII dump.
- Row cache resets when the address mode changes; the selected offset is preserved.

### Disassembly

- Columns: RVA, bytes, mnemonic, operands.
- Address and bytes are muted; mnemonic is primary; operands secondary.
- Static branch targets use `Brush.Info`; invalid instructions use `Brush.Danger`.
- A persistent "Linear sweep · read-only" state explains the analysis mode.
- Toolbar wraps at 1280 px instead of clipping.

### Analysis browser

- Virtualized rows, fixed columns, 11 px monospace values.
- Filter is a 440 px toolbar field; counts live in the footer.
- Tabs use a custom template with an accent underline and hover state.
- Double-click navigates to the CFG, hex, or source instruction.

### CFG

- 240 px block cards with three instruction lines.
- Entry block uses a 2 px accent border; back edges use warning color.
- Edges are stroked bezier curves with arrowheads.
- Fit/100 %/pan/wheel zoom are view-only transforms.

### Call graph

- 220 × 56 px nodes with role, function name, RVA, and block count.
- Target node uses accent border and selected background.
- Read-only Canvas in a scroll viewer; clicking a node recenters.
- Explicit empty state when no direct callers/callees exist.

### Projects and patch dialog

- Project name appears in the title bar; `*` marks unsaved annotations.
- Function rename updates the tree, Properties, CFG header, and call graph.
- Bookmarks are a dedicated tree group.
- Patch dialog is 620 px wide with monospace bytes, preview, and clear safety copy.

## Interaction

- Hover: `Brush.Hover`; selected: `Brush.Selected`.
- Keyboard: standard Windows shortcuts; command palette mirrors commands.
- `Enter`/double-click follows a branch or opens a function.
- No animated page transitions, decorative fades, or physics motion.
- The app never blocks the UI with a modal progress dialog; long work is async with cancel.

## Accessibility

- Minimum 11 px text in data views; high-contrast palettes.
- Focus states use the accent brush.
- Buttons have tooltips and text labels where practical.
- Tables support keyboard selection and copy.
- Graphs are mouse-driven; keyboard access is provided through the tree/browser and document shortcuts.

## Visual QA

The WPF UI is captured offscreen with:

```text
Obsidian.exe --capture <png> [--theme light|dark] [--open <binary>] [--document ...]
Obsidian.exe --dialog settings|about|patch|prompt|savechanges --capture <png>
```

Reviewed screens:

- Start page, Overview, Disassembly, Hex, Analysis Browser, CFG, Call Graph
- Functions, Strings, Cross References, bookmarks, project annotations
- Patch dialog, rename prompt, save-changes prompt, Settings, About
- Dark/light themes and 1600 × 1000 / 1280 × 800 layouts

The UI smoke run (`Obsidian.exe --smoke`) covers 55 interactions; unit tests cover 69 behaviors.

## Known compromises

- AvalonDock's VS theme paints a subtle accent line under the document pane; replacing the template is not worth the maintenance cost.
- Tool-window close buttons hide panels rather than destroying them, so View/Window can restore them.
- Start page scrolls vertically at 1280 px instead of compressing the layout.
- Light theme uses softer map segments; section table and tooltips preserve readability.
- Hex editing is read-only; instruction patching is a separate validated workflow.
- Disassembly is a linear sweep; seed-based analysis is explicit in the UI.
- The disassembly cap is 1,000,000 instructions; larger sweeps report truncation.
- Patch operations are same-length only.
- Project layout/open-document state is not persisted.
