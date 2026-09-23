# Process Booster

**A free, open-source Windows system dashboard + process tuner — all in one fast, modern app.**

Process Booster shows you *everything* about your PC (CPU, GPU, memory, storage, network, sensors,
security, startup, services, installed software and more) **and** lets you take control of how
individual processes use your hardware — a free, no-nagware alternative to tools like **Process
Lasso**, with a slice of Speccy / HWiNFO / Autoruns thrown in.

- **Free & open source** (MIT) — no ads, no nag screens, no paid tier.
- **One portable `.exe`** — ~72 MB, self-contained, no .NET install required.
- **Modern Fluent UI** — dark, Mica, violet accent.
- **Live where it matters, lazy everywhere else** — sensors/graphs poll only the tab you're viewing;
  static specs load once. It stays fast.

> ⚠️ **Screenshots needed here.** A GUI app lives or dies by its screenshots — please add a couple
> of PNGs (and ideally a short GIF cycling the tabs) to `docs/screenshots/` and embed them at the top.
> This is the single most important thing before sharing it publicly.

---

## Download & run

1. Grab the latest **`ProcessBooster_SelfContained.exe`** from the [Releases page](../../releases).
2. Double-click it. Accept the **UAC prompt** (admin is required to read/change other processes).

That's it — no installer, no dependencies.

**About the SmartScreen / antivirus warning:** the exe is **not code-signed yet**, so Windows
SmartScreen may say *"Windows protected your PC."* Click **More info → Run anyway**. Some antivirus
may also flag it because the live **Sensors** tab loads a kernel driver (`WinRing0`, the same one
HWiNFO/LibreHardwareMonitor use) to read temperatures. This is expected for hardware monitoring; if
your AV blocks it, sensors show "—" and everything else still works. The full source is here so you
can build it yourself if you prefer.

**Requirements:** 64-bit **Windows 10 (1809+) or 11**. Administrator rights.

---

## Features

**Processes** — live, sortable process table (CPU %, priority, affinity, I/O, memory, RAM, governing
rule). Select any process to set **CPU priority, CPU affinity, I/O priority, memory priority,
efficiency mode (EcoQoS), priority boost, CPU sets (P/E cores), GPU preference, and GPU scheduling
priority**. Save it as a **persistent rule** (re-applied automatically to that app, even new
instances), or **Apply now** one-off. Right-click for quick priority / efficiency actions. Live
activity log. Import/export your rules as JSON.

**System information tabs:**

| Tab | What it shows |
|---|---|
| **System** | Machine, OS summary, live CPU/GPU/RAM/network stat tiles |
| **OS** | Edition, version + build, install date, uptime, boot mode (UEFI/BIOS), Secure Boot |
| **Security** | Defender, Firewall, BitLocker, TPM, Secure Boot, UAC |
| **Users** | Current session, local accounts, Administrators group |
| **Startup** | Everything that runs at logon (sortable table) |
| **Software** | Full installed-programs inventory (sortable table) |
| **Services** | Running / stopped Windows services (sortable tables) |
| **Devices** | Present hardware, grouped by class (Device-Manager style) |
| **Environment** | User & system environment variables |
| **CPU** | Model, cache, per-core P/E layout + live load/temp/clock graphs |
| **Memory** | Per-module detail (size, speed, type, part #) + usage graph |
| **GPU** | Adapter, true VRAM, driver + live temp/load/clock graphs |
| **Display** | Per-monitor: connection (HDMI/DP), resolution, refresh, HDR, size |
| **Storage** | Physical disks (SSD/HDD/NVMe, health) + volumes |
| **Network** | Per-adapter IP/DNS/gateway, Wi-Fi SSID, live throughput graphs |
| **Sensors** | Every hardware sensor (temps, fans, voltages, clocks), live |
| **Power** | Battery charge/health/wear + power plan (desktop-aware) |

**Copy specs / Export report** — one click to copy or save a full plain-text system report.

Nothing is hardcoded to a specific machine — everything is enumerated from the live system, so it
works on any Windows PC (any CPU vendor, GPU count, monitors, adapters, etc.).

---

## Build from source

Requires the **.NET 8 SDK** on Windows.

```powershell
dotnet build -c Release
dotnet test  -c Release        # 46 tests, incl. real self-process interop checks

# produce both distributables (runtime-dependent folder + self-contained single exe):
./publish.ps1
```

`publish.ps1` writes `Desktop\ProcessBooster\` (small, needs the .NET 8 Desktop Runtime) and
`Desktop\ProcessBooster-portable\ProcessBooster_SelfContained.exe` (portable, no prerequisites).

---

## Architecture

```
ProcessBooster.Core   class library — all process-control logic, no UI, fully unit-tested
ProcessBooster.App    WPF desktop app (Fluent / WPF-UI, MVVM); Hardware/ = system-info collectors
ProcessBooster.Tests  xUnit — pure-logic + real self-process integration tests
```

Principles: the Core has **zero UI dependencies** and is fully testable headless; every apply-action
**returns a result instead of throwing**, so one denied setting never aborts a rule; rule application
is **idempotent**. System-info collectors are defensive — a missing WMI class or property yields
"—" rather than crashing a tab.

See [`docs/architecture.md`](docs/architecture.md) and [`docs/native-apis.md`](docs/native-apis.md).

---

## Roadmap

- Auto-start at logon (scheduled task) so rules apply on boot
- Optional tray icon
- Code signing (to remove the SmartScreen warning)
- Left-sidebar navigation (the tab strip is getting long)
- Per-process GPU usage; sparkline history on more tabs

---

## Contributing

Contributions welcome — see [`CONTRIBUTING.md`](CONTRIBUTING.md). Good first areas: testing on more
hardware, new system-info collectors, and UI polish.

## License

[MIT](LICENSE).

---

### Notes / gotchas
- **Realtime** priority and high I/O priority are limited by Windows and may fail without special
  privileges — Process Booster surfaces the failure instead of pretending it worked.
- **GPU preference** is a persistent per-exe Windows setting that applies the next time that app
  launches (not instantly).
- Efficiency mode uses **EcoQoS**; turning it off resets the process to system-managed QoS.
