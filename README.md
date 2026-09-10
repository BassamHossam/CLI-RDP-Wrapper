# CLI RDP Wrapper

> RDP Wrapper rebuilt as a lightweight CLI tool — no GUI, smaller footprint, full control from the terminal.

![Windows](https://img.shields.io/badge/Windows-7%20%E2%80%93%2011-blue)
![.NET](https://img.shields.io/badge/.NET-4.5-purple)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What is it?

**CLI RDP Wrapper** is a command-line reimagining of the original GUI-based RDP Wrapper.  
It lets you install, configure, and fully manage Windows Remote Desktop — all from a terminal, with no GUI overhead.

> ⚠️ Must be run as **Administrator**.

---

## Features

- ✅ Install / Uninstall RDP Wrapper (TermWrap or RdpWrap)
- ✅ Start / Stop / Restart the Terminal Service
- ✅ Show full RDP status at a glance
- ✅ Change RDP port with automatic firewall update
- ✅ Set max concurrent connections
- ✅ Toggle single-session per user
- ✅ Configure NLA / Security Layer
- ✅ Set session shadowing options
- ✅ Manage local users (create & add to RDP group)
- ✅ Toggle audio, video, USB, and PnP redirection
- ✅ Auto-generate `rdpwrap.ini` for unsupported Windows versions
- ✅ No GUI — smaller, faster, scriptable

---

## Usage

```
rdpWrapper.exe [command] [options]
```

### Core Commands

| Command | Description |
|---|---|
| `-install [TermWrap\|RdpWrap]` | Install the wrapper (default: TermWrap) |
| `-uninstall` | Uninstall the wrapper |
| `-start` | Start Terminal Service |
| `-stop` | Stop Terminal Service |
| `-restart` | Restart Terminal Service |
| `-status` | Show full RDP configuration & status |
| `-generate` | Generate `rdpwrap.ini` for current Windows version |

### Configuration Commands

| Command | Description |
|---|---|
| `-port <number>` | Set RDP port & update firewall rule |
| `-maxconn <number>` | Set maximum allowed connections |
| `-singlesession <1\|0>` | Toggle single session per user |
| `-allowts <1\|0>` | Toggle allow TS connections |
| `-nla <0\|1\|2>` | Set auth mode (0=GUI only, 1=Default, 2=NLA) |
| `-shadow <0-4>` | Set shadow mode (0=off, 1-4=various permissions) |
| `-honorlegacy <1\|0>` | Toggle honor legacy settings |
| `-dontdisplaylastuser <1\|0>` | Toggle display of last logged-in user |
| `-disablesecuritywarning <1\|0>` | Toggle client security warning |

### Redirection Commands

| Command | Description |
|---|---|
| `-restrictusb <1\|0>` | Toggle client USB redirection |
| `-allowplayback <1\|0>` | Toggle host audio playback redirect |
| `-allowaudiocapture <1\|0>` | Toggle client audio capture |
| `-allowvideocapture <1\|0>` | Toggle client video capture |
| `-allowpnp <1\|0>` | Toggle PnP device redirect |

### User Management

| Command | Description |
|---|---|
| `-adduser <username> <password>` | Create local user & add to Remote Desktop Users group |
| `-fixmsuser <username>` | Fix Microsoft account local cache |

---

## Examples

```powershell
# Install wrapper and start service
rdpWrapper.exe -install -start

# Change RDP port to 3390
rdpWrapper.exe -port 3390

# Allow 5 concurrent connections
rdpWrapper.exe -maxconn 5

# Create a new RDP user
rdpWrapper.exe -adduser john P@ssw0rd

# Check current status
rdpWrapper.exe -status
```

> 💡 You can also use `key=value` format: `-port=3390`

---

## Requirements

- Windows 7 / 8 / 10 / 11 or Windows Server 2012–2025
- .NET Framework 4.5+
- Administrator privileges

---

## Notes
> <sub>This project is a CLI rebuild inspired by the original [stascorp/rdpwrap](https://github.com/stascorp/rdpwrap).</sub>
> RDP Wrapper does **not** patch `termsrv.dll`. It loads it with different parameters, keeping the original file untouched — making it resilient against Windows Updates.

> Some antivirus software (including Defender) may flag this tool due to its behavior. Consider adding the install folder to your exclusion list:
> ```
> C:\Program Files\RDP Wrapper\
> ```

---


