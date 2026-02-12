# CanaryWire

Canary (honeytoken) file deployment and access monitoring tool for Windows.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)
![Windows](https://img.shields.io/badge/platform-Windows_Only-blue.svg)

<p align="center">
  <img src="output.png" alt="CanaryWire Alert Example" width="300">
</p>

CanaryWire deploys decoy (“canary”) files into sensitive directories and generates an alert when they are accessed.  
It is intended for detecting unauthorized browsing of protected folders such as finance, HR, or credential storage locations.

The deployed file appears as a normal `.pdf` in Windows Explorer to blend naturally into typical user environments.  
When opened, CanaryWire records contextual system information and sends a structured alert to a configured Discord webhook.

> **Use only on systems you own or are explicitly authorized to test. Unauthorized deployment is illegal.**

---

## Key Features

* **Shortcut-Based Deployment:** Uses Windows Shortcut (`.lnk`) behavior, which is always hidden by the OS shell, ensuring the decoy file displays as a normal document.
* **Document Appearance:** Uses an embedded Adobe Acrobat icon and assembly metadata so the decoy resembles a standard PDF file.
* **Background Execution:** The monitoring component runs without disrupting the user experience.
* **Screenshot Capture:** Captures the primary display at the moment of access to provide visual context.
* **Accurate OS Detection:** Reads Windows registry values to report full OS edition and version (e.g. `Windows 11 Enterprise 24H2`).
* **Structured Alerts:** Sends alerts as formatted Discord embeds with clear field layout and severity highlighting.
* **Silent Error Handling:** Runs without visible UI or console windows.

---

## How It Works

Windows shortcuts (`.lnk`) are treated uniquely by the operating system: their file extension is never shown, even when “Show file extensions” is enabled.  
This allows the decoy file to appear as a standard document while launching a monitoring executable in the background.

CanaryWire deploys three files into the chosen directory. Only the decoy file is visible:

| File | Visible? | Purpose |
|---|---|---|
| `name.pdf` | Yes | `.lnk` shortcut styled as a PDF |
| `~$xxxxxxxx.exe` | No | Monitoring component (Hidden + System) |
| `~$xxxxxxxx.ico` | No | Icon resource (Hidden + System) |

When the decoy file is opened:

1. The monitoring executable starts in the background  
2. The webhook URL is extracted from the binary tail  
3. A screenshot of the primary display is captured  
4. System context is gathered (user, host, IPs, OS, file path)  
5. A structured alert is sent to the Discord webhook  
6. A standard “file error” dialog is shown to simulate a damaged document  

---

## Data Collected

| Field | Description |
|---|---|
| User | Windows username |
| Host | Machine hostname |
| Domain | Domain or workgroup |
| OS | Full edition, version, and build (registry-based) |
| CPU Cores | Processor count |
| Public IP | External IP via `api.ipify.org` |
| Local IP | Active local network addresses |
| File Path | Path the decoy file was opened from |
| Screenshot | Primary monitor capture at access time |
| Timestamp | UTC time of access |

---

## Prerequisites

| Requirement | Details |
|---|---|
| Windows | 10 / 11 |
| .NET 10 Desktop Runtime | https://dotnet.microsoft.com/download/dotnet/10.0 |

> Building from source requires the .NET 10 SDK rather than only the runtime.

---

## Quick Start

### Option A: Download Release

1. Install the .NET 10 Desktop Runtime if required  
2. Download the latest `.zip` from Releases  
3. Extract all files into the same directory  
4. Run `CanaryWire.CLI.exe`

Release contents:

| File | Purpose |
|---|---|
| `CanaryWire.CLI.exe` | Deployment tool |
| `CanaryWire.CLI.dll` | Managed code |
| `CanaryWire.CLI.deps.json` | Dependency manifest |
| `CanaryWire.CLI.runtimeconfig.json` | Framework config |
| `CanaryWire.Canary.exe` | Monitoring template |
| `image.png` | Icon source |

> All files must remain in the same directory for deployment to work correctly.

---

### Option B: Build from Source

```bash
git clone https://github.com/j0xh/CanaryWire.git
cd CanaryWire
dotnet publish CanaryWire.Canary -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
dotnet run --project CanaryWire.CLI

```

### Follow the Prompts

```
=== CanaryWire Deployer ===

[*] Found payload template: ...\publish\CanaryWire.Canary.exe
[*] Found icon source: ...\image.png

Enter Discord Webhook URL: https://discord.com/api/webhooks/...
Enter canary name (e.g. 'passwords', 'confidential'): payroll
Enter output directory (full path, e.g. 'C:\Users\you\Desktop'): C:\Users\target\Desktop\Finance

[+] Canary deployed successfully!

    Shortcut : C:\Users\target\Desktop\Finance\payroll.pdf.lnk
               Displayed as "payroll.pdf" (.lnk is always hidden)
    Payload  : C:\Users\target\Desktop\Finance\~$a1b2c3d4.exe
               Hidden + System (invisible in Explorer)
    Icon     : C:\Users\target\Desktop\Finance\~$a1b2c3d4.ico
               Hidden + System (invisible in Explorer)

[!] Deploy ALL generated files together to the target location.
    Only the .pdf shortcut is visible to the target user.
```

## Architecture

```
CanaryWire.CLI (Deployer)
├── Finds published payload template
├── Embeds webhook URL into binary tail
├── Generates ICO from PNG (PNG-in-ICO format)
├── Deploys hidden .exe + .ico (Hidden + System)
└── Creates .lnk shortcut with Adobe icon

CanaryWire.Canary (Payload)
├── Extracts webhook URL from own binary tail
├── Captures screenshot (primary monitor)
├── Gathers system intel (registry-based OS, IPs, path)
├── POSTs Discord embed with screenshot attachment
└── Shows fake Adobe Acrobat error dialog
```

## Disclaimer

This tool is for authorized security testing and defensive auditing only. It is designed to detect unauthorized access to sensitive files and directories. Misuse is the sole responsibility of the user.

## License

MIT
