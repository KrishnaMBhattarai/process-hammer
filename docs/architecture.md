# Architecture

## Layers

- **ProcessBooster.Core** — all behaviour, no UI. Depends only on the BCL. This is what the tests
  exercise and what any front-end (WinForms app, CLI, service) builds on.
- **ProcessBooster.App** — the WinForms desktop UI (next milestone). Thin: it renders the live
  table, edits rules, and drives the engine. No business logic of its own.
- **ProcessBooster.Tests** — xUnit. Pure-logic tests run anywhere; integration tests mutate and
  restore *this* process to prove the native calls work.

## Data flow

```
ProcessInspector.Snapshot()  ──►  List<ProcessSnapshot>   (live table + matching)
                                        │
RuleMatcher.FirstMatch(rules, name) ────┘
                                        │
ProcessController.ApplyRule(rule, pid) ─► native APIs ─► List<ActionResult> ─► ActionLog
```

The **RuleEngine** ties these together on a timer: snapshot → match → apply → log, every
`PollSeconds`. Application is idempotent, so re-running each tick is safe; the engine only logs the
first time a (pid, rule) pair is applied to avoid noise, and forgets pids once they exit.

## Key decisions

- **Nullable actions.** Every field on `ProcessRule` is nullable; only set fields are applied. One
  rule can touch a single setting or all of them.
- **Results, not exceptions.** Each apply-method returns an `ActionResult` (`Applied`, `Failed`,
  `Skipped`, `Unsupported`). A denied setting never aborts the rest of the rule.
- **Best-effort reads.** `ProcessInspector` returns null fields for processes it can't open
  (protected/cross-bitness) instead of failing the whole scan.
- **Versioned config.** `AppConfig.SchemaVersion` gates loading; newer-than-supported files are
  rejected with a clear message, and there's a migration hook for future changes.
- **Interop isolated.** All P/Invoke lives in `Interop/NativeMethods.cs`, verified against Microsoft
  Learn (see `native-apis.md`). Services own the semantics; the interop file stays policy-free.

## Extending

Add a new capability by: (1) adding the P/Invoke to `NativeMethods`, (2) a `Set*` method returning
`ActionResult` on `ProcessController`, (3) a nullable field on `ProcessRule`, (4) a read in
`ProcessInspector`, (5) tests. The GUI then picks it up as one more column/editor row.
