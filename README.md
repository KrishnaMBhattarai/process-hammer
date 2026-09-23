# ⚡ Process Booster

**See everything about your Windows PC — and take control of it.** A free, open-source system
dashboard *and* process tuner in one fast, modern app. No nag screens, no paywall, no install:
just one portable `.exe`.

![License](https://img.shields.io/badge/license-MIT-blue) ![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D6) ![.NET](https://img.shields.io/badge/.NET-8-512BD4) ![Portable](https://img.shields.io/badge/single%20exe-no%20install-brightgreen)

If you've ever paid for **Process Lasso** just to pin a game to your P-cores, or juggled **Speccy +
HWiNFO + Autoruns + Task Manager** to check your rig — this is all of that, free and in one window.

> 📸 **Screenshots go here.** *(This is the #1 thing before sharing — drop a couple of PNGs and a
> short GIF into `docs/screenshots/` and they'll be embedded at the top.)*

---

## 🎮 Built for gamers & PC builders

- **Tune games for smoother frames.** Set a game to **High priority**, pin it to your **P-cores**,
  turn **efficiency mode off**, and force the **high-performance GPU** — then **save it as a rule**
  so it's applied automatically every time that game launches.
- **Know your rig cold.** Per-core **P/E layout with live clocks**, **RAM speed + timings + part
  numbers**, **true VRAM** (not the WMI 4 GB lie), monitor **connection (HDMI/DP) + HDR**, disk
  **health**, and **live temps / fans / voltages** on the Sensors tab.
- **Debloat & audit.** See exactly **what runs at startup**, browse **installed software** and
  **services** as sortable tables, and check **security** (Defender, Firewall, BitLocker, TPM,
  Secure Boot).
- **Share your specs.** One click → **Copy specs / Export report** as clean text for a forum post
  or a support ticket.

---

## 📥 Download

1. Grab **`ProcessBooster_SelfContained_vX.Y.Z.exe`** from the
   **[Releases page](https://github.com/KrishnaMBhattarai/process-booster/releases/latest)**.
   *(Portable single file, ~72 MB — bundles .NET, so **nothing else to install**.)*
2. Double-click it and accept the **UAC prompt** (admin is required to read/change processes).

### ✅ Verify your download (SHA-256)

Every release ships a matching `.sha256` file. Check your download hasn't been tampered with:

```powershell
# PowerShell — should match the hash in the .sha256 file on the release
Get-FileHash .\ProcessBooster_SelfContained_v0.10.0.exe -Algorithm SHA256
```
```cmd
:: or with certutil
certutil -hashfile ProcessBooster_SelfContained_v0.10.0.exe SHA256
```

Compare the output to the hash inside `ProcessBooster_SelfContained_vX.Y.Z.exe.sha256`. If they
match, the file is authentic and unmodified.

### ⚠️ SmartScreen / antivirus

The exe isn't code-signed yet, so **SmartScreen may warn** — click **More info → Run anyway**. The
live **Sensors** tab loads a kernel driver (`WinRing0`, the same one HWiNFO / LibreHardwareMonitor
use) to read temperatures, which some antivirus flags; if it's blocked, sensors show `—` and
everything else still works. Prefer to be safe? **Build it yourself** (below).

---

## 🧰 What's inside

**Processes** — a live, sortable table (CPU %, priority, affinity, I/O, memory, RAM, active rule).
Pick any process and set **CPU priority · affinity · I/O priority · memory priority · efficiency
mode (EcoQoS) · priority boost · CPU sets (P/E cores) · GPU preference · GPU scheduling**. Save it
as a **persistent rule**, **Apply now** one-off, or right-click for quick actions. Live activity log;
import/export rules as JSON.

**Info tabs:**

| | | |
|---|---|---|
| **System** – live tiles + summary | **OS** – edition, build, uptime, Secure Boot | **Security** – Defender, Firewall, BitLocker, TPM |
| **Users** – accounts & admins | **Startup** – autoruns | **Software** – installed programs |
| **Services** – running / stopped | **Devices** – hardware by class | **Environment** – variables |
| **CPU** – cores, cache, live graphs | **Memory** – modules, timings | **GPU** – VRAM, driver, live graphs |
| **Display** – HDMI/DP, HDR, refresh | **Storage** – disks & volumes | **Network** – IP/DNS, Wi-Fi, throughput |
| **Sensors** – all temps/fans/volts | **Power** – battery health, plan | |

Everything is enumerated from the live system — **nothing is hardcoded**, so it works on any PC
(any CPU vendor, GPU count, monitors, adapters…).

---

## 💻 Requirements

64-bit **Windows 10 (1809+) or 11**. Administrator rights (one UAC prompt).

## 🛠️ Build from source

Requires the **.NET 8 SDK**.

```powershell
dotnet build -c Release
dotnet test  -c Release     # 46 tests, incl. real self-process interop checks
./publish.ps1               # builds both distributables + the versioned exe & SHA-256
```

## 🗺️ Roadmap

Auto-start at logon · optional tray icon · code signing (to drop the SmartScreen warning) ·
left-sidebar navigation · per-process GPU usage.

## 🤝 Contributing

PRs welcome — see **[CONTRIBUTING.md](CONTRIBUTING.md)**. Best first help: **test on your hardware**
and file issues for anything that reads wrong.

## 📄 License

[MIT](LICENSE). Architecture & native-API notes in [`docs/`](docs/).
