# Process Booster

A free, open, native Windows utility for controlling how processes use the CPU, GPU, memory and
I/O — a lightweight alternative to Process Lasso. Set persistent per-process rules (CPU priority,
affinity, I/O priority, memory priority, efficiency mode / EcoQoS, CPU sets, priority boost, GPU
preference and GPU scheduling priority), watch a live process table, and keep it all in an
importable/exportable config.

**Version:** 0.4.0 • **Platform:** Windows 10/11 x64 • **Runtime:** .NET 8 (Desktop)

---

## Status

Process Booster is built in layers, each verified before the next. The **core engine is complete
and covered by a passing test suite** (46 tests, including real self-process integration tests that
prove every native call works), and the **Fluent desktop GUI is built** on top of it — a modern
WPF app (WPF-UI, Mica, dark theme) with a live process table, a full rule editor, and an activity
log. Auto-start-at-logon is the next milestone.

| Capability | Layer | State |
|---|---|---|
| CPU priority (Idle…Realtime) | Core | ✅ implemented + tested |
| CPU affinity (bitmask / ranges) | Core | ✅ |
| I/O priority (VeryLow/Low/Normal) | Core | ✅ |
| Memory priority (VeryLow…Normal) | Core | ✅ |
| Efficiency mode (EcoQoS) | Core | ✅ |
| Priority boost toggle | Core | ✅ |
| CPU sets (P-core / E-core / custom) | Core | ✅ |
| GPU preference (power-saving / high-perf) | Core | ✅ |
| GPU scheduling priority | Core | ✅ |
| Live process table (data source) | Core | ✅ (`ProcessInspector`) |
| Rules engine (persistent, idempotent) | Core | ✅ |
| Config save / load / import / export | Core | ✅ |
| Logging (ring buffer + file) | Core | ✅ |
| **Fluent GUI** (live table, rule editor, log) | App | ✅ implemented |
| Live CPU% · right-click quick actions | App | ✅ implemented |
| **Hardware tabs** (System, CPU, Memory, Graphics, Display, Storage, Network) | App | ✅ implemented |
| Live sensors + lazy per-tab load (polls only the active tab) | App | ✅ implemented |
| Purple Fluent accent + tab icons | App | ✅ implemented |
| Auto-start at logon / tray | App | 🚧 planned |

---

## Architecture

```
ProcessBooster.Core   class library — all logic, no UI. Independently testable.
  Interop/            hand-verified P/Invoke (kernel32, ntdll, gdi32) + structs
  Models/             enums, ProcessRule, AppConfig, ProcessSnapshot
  Services/           ProcessController (apply), ProcessInspector (read),
                      CpuTopology (P/E cores), GpuPreferenceStore, RuleMatcher, RuleEngine
  Config/             ConfigStore (JSON, versioned schema, import/export)
  Logging/            ActionLog (thread-safe ring + file sink)
ProcessBooster.App    WPF desktop app (Fluent / WPF-UI, MVVM)
ProcessBooster.Tests  xUnit — pure-logic + real self-process integration tests
```

Design principles: the Core has **zero UI dependencies** so it's fully testable headless; every
apply-action **returns a result instead of throwing** so one failure never aborts a rule; rule
application is **idempotent** so the watcher can re-apply safely every tick.

See [`docs/architecture.md`](docs/architecture.md) and [`docs/native-apis.md`](docs/native-apis.md).

---

## Build & test

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build  -c Release
dotnet test   -c Release      # 46 tests; includes live self-process interop checks
```

## Run

Publish a runnable copy (framework-dependent; needs the .NET 8 Desktop runtime):

```powershell
dotnet publish src/ProcessBooster.App -c Release -o publish
publish\ProcessBooster.exe        # launches the GUI (prompts for admin)
```

The app requires administrator rights (to change other processes' settings) and will show a UAC
prompt on launch.

---

## Notes

- Setting **Realtime** priority, or high I/O priority, requires elevation and is intentionally
  limited by Windows; Process Booster surfaces the failure rather than pretending it worked.
- **GPU preference** is a persistent per-executable Windows setting that takes effect the next time
  the app launches (not instantly).
- Efficiency mode uses **EcoQoS**; turning it off resets the process to system-managed QoS.

## License

MIT (see `LICENSE`).
