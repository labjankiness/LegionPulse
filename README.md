# ⚡ LegionPulse

> **Lightweight, real-time desktop hardware telemetry overlay & thermal optimizer for Lenovo Legion laptops running Windows 11 & WSL2.**

![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%20WSL2-0078D6?style=flat-square&logo=windows)
![Language](https://img.shields.io/badge/Language-C%23%20%7C%20WPF-239120?style=flat-square&logo=csharp)
![Hardware](https://img.shields.io/badge/Hardware-Intel%20Tiger%20Lake%20%7C%20RTX%203060-76B900?style=flat-square&logo=nvidia)
![License](https://img.shields.io/badge/License-MIT-blue?style=flat-square)

---

## 📖 Overview

Gaming laptops like the **Lenovo Legion 5 (Intel Core i7-11800H + NVIDIA RTX 3060)** are computational powerhouses, but out-of-the-box Windows settings often lead to thermal throttling, micro-stuttering during multi-tasking, and phantom battery/power drain:
1. **Aggressive CPU Turbo Spikes:** Windows defaults CPU boost to *Aggressive*, slamming the CPU into 4.60 GHz (80W bursts) for minor background mouse clicks, generating unnecessary heat.
2. **HDMI Direct dGPU Wiring:** On Legion laptops, the HDMI port is hardwired directly to the discrete NVIDIA GPU. When an external monitor is plugged in, the dGPU stays awake drawing ~14.5W at idle.
3. **Unbounded WSL2 Allocation:** WSL2 dynamically claims up to 50% of system RAM without a hard ceiling, competing with Windows host memory.

**LegionPulse** solves this by providing:
* An **ultra-lightweight (16 KB) native desktop overlay widget** that monitors real-time CPU clock speeds, core loads, GPU temperatures, VRAM, RAM, and total silicon power draw (Intel RAPL + NVIDIA NVML).
* A calibrated power management profile that prevents thermal throttling while preserving instant auto-boost when heavy workloads demand it.
* A tuned `.wslconfig` configuration that establishes a 16 GB ceiling on Linux VMs, ensuring at least 48 GB remains dedicated to Windows applications.

---

## 🖥️ Live Telemetry & Features

| Metric | Source / Method | Refresh Interval | Description |
| :--- | :--- | :--- | :--- |
| **CPU Load & Freq** | Win32 `GetSystemTimes` & `CallNtPowerInformation` | 1.5s | Live load % across 16 threads & exact operating GHz (2.25 GHz base to 3.59+ GHz auto-boost). |
| **GPU Thermals & Load** | Native NVIDIA Management Library (`nvml.dll`) | 1.5s | Exact GPU core temperature (°C), live wattage, utilization %, and VRAM usage. |
| **RAM Allocation** | Win32 `GlobalMemoryStatusEx` | 1.5s | Exact Used GB / Total 64 GB and free memory headroom. |
| **⚡ Total Power Draw** | Intel RAPL (`rapl_package0_pkg`) + NVML | 1.5s | Combined CPU Package Watts + GPU Watts with dynamic color-coded wattage bar. |
| **Active Profile** | Win32 `PowerGetActiveScheme` | 1.5s | Live detection of active power profile (e.g. `Balanced (Efficient Boost)`). |

### 🎮 Widget Controls
* **Freely Draggable:** Click and drag anywhere on the widget to reposition it. Saves and restores position automatically across reboots.
* **📌 Pin to Top:** Toggle between **Desktop Mode** (sits quietly on your wallpaper behind your active windows) and **Always on Top** (floats over apps).
* **Startup Toggle:** Toggle auto-launch on Windows boot with a single click.
* **Zero Overhead:** Consumes **<0.1% CPU** and ~20 MB of RAM with zero console flicker.

---

## 🛠️ Quick Installation

### Option 1: One-Click Installer (Recommended)
1. Clone this repository:
   ```bash
   git clone https://github.com/labjankiness/LegionPulse.git
   cd LegionPulse
   ```
2. Double-click `install.bat`. It will:
   * Compile the standalone binary using Windows' built-in C# compiler (`csc.exe`).
   * Copy it to `%LOCALAPPDATA%\LegionMonitor`.
   * Create a desktop shortcut.
   * Enable run-on-startup in the Windows Registry.
   * Launch the overlay immediately.

### Option 2: Manual Build
Run `build.bat` or compile directly with the native .NET C# compiler:
```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /optimize+ /lib:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF /r:System.dll,System.Xaml.dll,PresentationCore.dll,PresentationFramework.dll,WindowsBase.dll /out:LegionMonitorOverlay.exe src\LegionMonitor.cs
```

---

## 📂 Repository Structure

```text
LegionPulse/
├── src/
│   └── LegionMonitor.cs                 # Native C# WPF source code
├── dashboard/
│   └── System_Diagnostic_Dashboard.html # Standalone interactive HTML dashboard
├── wsl/
│   └── .wslconfig                       # Balanced WSL2 memory/processor envelope
├── build.bat                            # Zero-dependency compiler script
├── install.bat                          # One-click install & autostart configurator
├── LICENSE                              # MIT License
└── README.md                            # Documentation
```

---

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
