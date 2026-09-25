# Contributing to Process Hammer

Thanks for your interest! This is a young project and help is very welcome — especially **testing on
different hardware**, **new system-info collectors**, and **UI polish**.

## Development setup

- **Windows 10 (1809+) / 11, x64** (the app is Windows-only).
- **.NET 8 SDK** — https://dotnet.microsoft.com/download
- Any editor (Visual Studio 2022, VS Code + C# Dev Kit, or Rider).

```powershell
git clone https://github.com/KrishnaMBhattarai/process-hammer.git
cd process-hammer
dotnet build -c Release
dotnet test  -c Release        # must stay green
./publish.ps1                  # optional: build the distributables
```

Run the app elevated (it self-elevates via its manifest; accept the UAC prompt).

## Project layout

```
src/ProcessHammer.Core   process-control logic + interop; NO UI; unit-tested
src/ProcessHammer.App    WPF app
    Hardware/             system-info collectors (one file per area)
    ViewModels/           MVVM view-models (MainViewModel, tabs, etc.)
    Behaviors/            small attached behaviors (e.g. dynamic DataGrid columns)
    MainWindow.xaml       the shell + tab layout
tests/ProcessHammer.Tests  xUnit
docs/                     architecture + native-API reference
```

## How to add a new system-info tab (the most common contribution)

1. **Collector** — add `src/ProcessHammer.App/Hardware/XxxInfo.cs`:
   ```csharp
   public static class XxxInfoService
   {
       public static List<InfoSection> Collect() { /* WMI/registry/etc. */ }
       // OR, for list/table data:
       public static List<DataTable> CollectTables() { ... }
   }
   ```
   Reuse `InfoItem` / `InfoSection` / `DataTable` — don't redefine them.
2. **Wire it** in `MainViewModel`: add a `HardwareTabViewModel` (label/value cards) or
   `TableTabViewModel` (sortable grid), and add it to `_tabByIndex`.
3. **Add the tab** in `MainWindow.xaml` with a valid `SymbolRegular` icon (see note below).

## House rules (please follow these — they keep the app robust)

- **Never hardcode for one machine.** Enumerate real devices; work with 0..N of anything.
- **Collectors must never throw.** Wrap every WMI/registry/native call; a missing value becomes
  `"—"` (or the row is skipped). `Collect()` returning partial data is fine; crashing a tab is not.
- **Core stays UI-free and tested.** Any logic that can be unit-tested should live in `Core` with a
  test. Apply-actions return a result rather than throwing.
- **Don't block the UI thread.** Heavy scans run via `Task.Run`; live tabs poll only while active.
- **Icons are validated by the compiler, not XAML.** `SymbolRegular` names in XAML are resolved at
  runtime, so a typo crashes at launch. Confirm a name compiles by referencing
  `SymbolRegular.TheName` in code before using it in XAML.

## Commits & pull requests

- Use clear, conventional-ish messages (`feat:`, `fix:`, `perf:`, `docs:`).
- Keep `dotnet test` green; add tests for Core changes.
- Note what hardware you tested on (CPU/GPU/OS build) — coverage across machines is gold here.
- Open a PR against `main` with a short description and, for UI changes, a screenshot.

## Good first contributions

- Test on your machine and file issues for anything that shows wrong data or "—" that shouldn't be.
- A new collector (e.g. **Reliability / recent crashes**, **USB tree**, **audio devices**).
- Per-column width hints for the table tabs; sparkline polish; a left-sidebar navigation layout.
