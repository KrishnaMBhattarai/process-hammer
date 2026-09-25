# Architecture

## Layers

- **ProcessHammer.Core** — all behaviour, no UI. Depends only on the BCL. This is what the unit tests
  exercise and what the app builds on: models, the process inspector/controller, the rule engine, CPU
  topology, power/GPU services, config, and the isolated P/Invoke surface.
- **ProcessHammer.App** — the WPF desktop UI (MVVM). View models hold the interaction logic; the XAML
  is a thin view. Also home to the live hardware/WMI readers, the system-tray integration, and the
  start-with-Windows helper.
- **ProcessHammer.Tests** — xUnit. Pure-logic tests run anywhere; functional tests mutate and restore
  *this* process (or a spawned child) to prove the native calls actually work.

## Data flow

```
ProcessInspector.Snapshot()  ──►  List<ProcessSnapshot>   (live table + matching)
                                        │
RuleMatcher.FirstMatch(rules, name) ────┘
                                        │
ProcessController.ApplyRule(rule, pid, exePath) ─► native APIs ─► List<ActionResult> ─► ActionLog
```

The **RuleEngine** ties these together on a timer: snapshot → match → apply → log, every
`PollSeconds`. It takes the snapshot/apply operations as delegates (not the concrete services), so the
loop is unit-testable with fakes. Application is idempotent, so re-running each tick is safe; the
engine only logs the first time a (pid, rule) pair is applied, and forgets pids once they exit.

The UI adds a second path for one-off, user-driven changes: the right-click menu and the Rule panel
both call the same `ProcessController` methods and stay mirrored to the process's live state.

## Key decisions

- **Nullable actions.** Every field on `ProcessRule` is nullable; only set fields are applied. One
  rule can touch a single setting or all of them.
- **Results, not exceptions.** Each apply-method returns an `ActionResult` (`Applied`, `Failed`,
  `Skipped`, `Unsupported`). A denied setting never aborts the rest of the rule, and the UI surfaces
  failures (e.g. protected/anti-cheat processes) instead of failing silently.
- **Best-effort reads.** `ProcessInspector` returns null fields for processes it can't open
  (protected/cross-bitness) instead of failing the whole scan. Expensive per-process reads (exe path,
  CPU sets, GPU state) are done lazily — only for the selected process, or only when a rule needs them.
- **Versioned config.** `AppConfig.SchemaVersion` gates loading; newer-than-supported files are
  rejected with a clear message, and there's a migration hook for future changes.
- **Interop isolated.** All P/Invoke lives in `Interop/NativeMethods.cs`, verified against Microsoft
  Learn (see `native-apis.md`). Services own the semantics; the interop file stays policy-free.

## Extending

Add a new capability by: (1) adding the P/Invoke to `NativeMethods`, (2) a `Set*` (and, if readable, a
`Read*`) method returning `ActionResult` on `ProcessController`, (3) a nullable field on `ProcessRule`,
(4) a read in `ProcessInspector` or `ReadCurrentExtras`, (5) tests. The UI then picks it up as one more
menu item / editor row.
