using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Input;
using System.Runtime.InteropServices;
using Microsoft.Win32;

public class LegionOverlayApp : Application {
    [STAThread]
    public static void Main() {
        LegionOverlayApp app = new LegionOverlayApp();
        app.Run(new MonitorWindow());
    }
}

public class MonitorWindow : Window {
    // Win32 Power APIs
    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    public static extern uint PowerReadFriendlyName(IntPtr RootPowerKey, ref Guid SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid, IntPtr PowerSettingGuid, IntPtr Buffer, ref uint BufferSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSOR_POWER_INFORMATION {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [DllImport("powrprof.dll")]
    public static extern int CallNtPowerInformation(int infoLevel, IntPtr input, int inputSize, [Out] PROCESSOR_POWER_INFORMATION[] output, int outputSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx([In, Out] ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

    // NVML native imports for instantaneous GPU metrics
    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    public static extern int nvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
    public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
    public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

    [StructLayout(LayoutKind.Sequential)]
    public struct nvmlUtilization_t {
        public uint gpu;
        public uint memory;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

    [StructLayout(LayoutKind.Sequential)]
    public struct nvmlMemory_t {
        public ulong total;
        public ulong free;
        public ulong used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    public static extern int nvmlShutdown();

    private IntPtr nvmlDev = IntPtr.Zero;
    private bool nvmlAvailable = false;
    private PerformanceCounter cpuPowerCounter = null;

    private long prevIdle = 0;
    private long prevKernel = 0;
    private long prevUser = 0;

    // Hardware Names (Auto-detected)
    private string detectedCpu = "CPU";
    private string detectedGpu = "GPU";
    private int detectedRamGb = 16;

    // UI elements
    private TextBlock txtSubtitle;
    private TextBlock txtProfileBadge;

    private TextBlock txtCpuTitle;
    private TextBlock txtCpuDetails;
    private Border pbCpuInner;
    private Border pbCpuOuter;

    private TextBlock txtGpuTitle;
    private TextBlock txtGpuDetails;
    private Border pbGpuInner;
    private Border pbGpuOuter;

    private TextBlock txtRamTitle;
    private TextBlock txtRamDetails;
    private Border pbRamInner;
    private Border pbRamOuter;

    private TextBlock txtPowerTitle;
    private TextBlock txtPowerDetails;
    private Border pbPowerInner;
    private Border pbPowerOuter;

    private CheckBox chkAutoStart;
    private Button btnPin;
    private bool isPinned = false;

    private DispatcherTimer timer;

    public MonitorWindow() {
        this.Title = "LegionPulse Monitor";
        this.Width = 320;
        this.Height = 405;
        this.WindowStyle = WindowStyle.None;
        this.AllowsTransparency = true;
        this.Background = Brushes.Transparent;
        this.ShowInTaskbar = false;
        this.Topmost = false;

        // Position on desktop (default top right)
        double screenW = SystemParameters.PrimaryScreenWidth;
        this.Left = screenW - 340;
        this.Top = 30;

        DetectHardware();
        LoadPosition();
        InitNvml();
        InitCpuPowerCounter();
        BuildUI();

        // Draggable window anywhere
        this.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) {
            if (e.LeftButton == MouseButtonState.Pressed) {
                this.DragMove();
                SavePosition();
            }
        };

        timer = new DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        timer.Tick += delegate(object s, EventArgs e) { UpdateStats(); };
        timer.Start();

        UpdateStats();
    }

    private void DetectHardware() {
        // 1. Detect CPU
        try {
            RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key != null) {
                object val = key.GetValue("ProcessorNameString");
                key.Close();
                if (val != null) {
                    string s = val.ToString().Trim();
                    s = s.Replace("11th Gen ", "").Replace("12th Gen ", "").Replace("13th Gen ", "").Replace("14th Gen ", "");
                    s = s.Replace("Intel(R) Core(TM) ", "").Replace("AMD Ryzen ", "Ryzen ");
                    int atIdx = s.IndexOf("@");
                    if (atIdx > 0) s = s.Substring(0, atIdx).Trim();
                    detectedCpu = s;
                }
            }
        } catch {
            detectedCpu = "Intel/AMD";
        }

        // 2. Detect RAM
        try {
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem)) {
                double total = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                detectedRamGb = (int)Math.Round(total);
            }
        } catch {
            detectedRamGb = 16;
        }
    }

    private void InitNvml() {
        try {
            int res = nvmlInit();
            if (res == 0) {
                int devRes = nvmlDeviceGetHandleByIndex(0, out nvmlDev);
                nvmlAvailable = (devRes == 0);
                if (nvmlAvailable) {
                    StringBuilder sb = new StringBuilder(64);
                    if (nvmlDeviceGetName(nvmlDev, sb, 64) == 0) {
                        string s = sb.ToString().Trim();
                        s = s.Replace("NVIDIA GeForce ", "").Replace(" Laptop GPU", "").Replace(" GPU", "");
                        detectedGpu = s;
                    }
                }
            }
        } catch {
            nvmlAvailable = false;
            detectedGpu = "GPU";
        }
    }

    private void InitCpuPowerCounter() {
        try {
            cpuPowerCounter = new PerformanceCounter("Energy Meter", "Power", "rapl_package0_pkg");
            cpuPowerCounter.NextValue();
        } catch {
            cpuPowerCounter = null;
        }
    }

    private void BuildUI() {
        Border root = new Border();
        root.Background = new SolidColorBrush(Color.FromArgb(238, 18, 22, 31)); // 93% deep dark acrylic
        root.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61));
        root.BorderThickness = new Thickness(1.2);
        root.CornerRadius = new CornerRadius(14);
        root.Padding = new Thickness(16, 14, 16, 14);

        DropShadowEffect shadow = new DropShadowEffect();
        shadow.Color = Colors.Black;
        shadow.BlurRadius = 22;
        shadow.ShadowDepth = 5;
        shadow.Opacity = 0.65;
        root.Effect = shadow;

        StackPanel panel = new StackPanel();

        // 1. HEADER
        Grid headerGrid = new Grid();
        ColumnDefinition col1 = new ColumnDefinition();
        col1.Width = new GridLength(1, GridUnitType.Star);
        ColumnDefinition col2 = new ColumnDefinition();
        col2.Width = GridLength.Auto;
        headerGrid.ColumnDefinitions.Add(col1);
        headerGrid.ColumnDefinitions.Add(col2);

        StackPanel titleBox = new StackPanel();
        TextBlock title = new TextBlock();
        title.Text = "⚡ LEGIONPULSE";
        title.FontWeight = FontWeights.Bold;
        title.FontSize = 13;
        title.Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        titleBox.Children.Add(title);

        txtSubtitle = new TextBlock();
        txtSubtitle.Text = string.Format("{0} • {1} • {2}GB", detectedCpu, detectedGpu, detectedRamGb);
        txtSubtitle.FontSize = 10;
        txtSubtitle.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
        txtSubtitle.Margin = new Thickness(0, 1, 0, 0);
        titleBox.Children.Add(txtSubtitle);

        Grid.SetColumn(titleBox, 0);
        headerGrid.Children.Add(titleBox);

        // Header Action Buttons
        StackPanel btnPanel = new StackPanel();
        btnPanel.Orientation = Orientation.Horizontal;

        // Pin Button
        btnPin = new Button();
        btnPin.Content = "📌";
        btnPin.Width = 26;
        btnPin.Height = 24;
        btnPin.Background = Brushes.Transparent;
        btnPin.BorderThickness = new Thickness(0);
        btnPin.Foreground = new SolidColorBrush(isPinned ? Color.FromRgb(88, 166, 255) : Color.FromRgb(139, 148, 158));
        btnPin.ToolTip = "Toggle Always On Top";
        btnPin.Cursor = Cursors.Hand;
        btnPin.Click += delegate(object s, RoutedEventArgs e) {
            isPinned = !isPinned;
            this.Topmost = isPinned;
            btnPin.Foreground = new SolidColorBrush(isPinned ? Color.FromRgb(88, 166, 255) : Color.FromRgb(139, 148, 158));
            SavePosition();
        };
        btnPanel.Children.Add(btnPin);

        // Close Button
        Button btnClose = new Button();
        btnClose.Content = "✕";
        btnClose.Width = 24;
        btnClose.Height = 24;
        btnClose.Background = Brushes.Transparent;
        btnClose.BorderThickness = new Thickness(0);
        btnClose.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
        btnClose.FontSize = 11;
        btnClose.Cursor = Cursors.Hand;
        btnClose.Click += delegate(object s, RoutedEventArgs e) {
            if (nvmlAvailable) {
                try { nvmlShutdown(); } catch {}
            }
            this.Close();
        };
        btnPanel.Children.Add(btnClose);

        Grid.SetColumn(btnPanel, 1);
        headerGrid.Children.Add(btnPanel);
        panel.Children.Add(headerGrid);

        // 2. SYSTEM PROFILE BADGE
        Border profileBox = new Border();
        profileBox.Background = new SolidColorBrush(Color.FromArgb(140, 31, 111, 235));
        profileBox.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 139, 253));
        profileBox.BorderThickness = new Thickness(1);
        profileBox.CornerRadius = new CornerRadius(6);
        profileBox.Padding = new Thickness(8, 4, 8, 4);
        profileBox.Margin = new Thickness(0, 8, 0, 8);

        txtProfileBadge = new TextBlock();
        txtProfileBadge.Text = "🔋 Profile: Balanced (Efficient Boost)";
        txtProfileBadge.FontSize = 10;
        txtProfileBadge.FontWeight = FontWeights.SemiBold;
        txtProfileBadge.Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252));
        txtProfileBadge.HorizontalAlignment = HorizontalAlignment.Center;
        profileBox.Child = txtProfileBadge;
        panel.Children.Add(profileBox);

        // 3. CPU SECTION
        txtCpuTitle = new TextBlock();
        txtCpuTitle.Text = string.Format("CPU ({0})", detectedCpu);
        txtCpuTitle.FontWeight = FontWeights.SemiBold;
        txtCpuTitle.FontSize = 11;
        txtCpuTitle.Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252));

        txtCpuDetails = new TextBlock();
        txtCpuDetails.Text = "Loading...";
        txtCpuDetails.FontSize = 11;
        txtCpuDetails.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        txtCpuDetails.Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255));
        txtCpuDetails.HorizontalAlignment = HorizontalAlignment.Right;

        Grid cpuGrid = new Grid();
        cpuGrid.Children.Add(txtCpuTitle);
        cpuGrid.Children.Add(txtCpuDetails);
        panel.Children.Add(cpuGrid);

        pbCpuOuter = CreateProgressBarContainer();
        pbCpuInner = CreateProgressBarFill(Color.FromRgb(56, 139, 253), Color.FromRgb(88, 166, 255));
        pbCpuOuter.Child = pbCpuInner;
        panel.Children.Add(pbCpuOuter);

        // 4. GPU SECTION
        txtGpuTitle = new TextBlock();
        txtGpuTitle.Text = string.Format("GPU ({0})", detectedGpu);
        txtGpuTitle.FontWeight = FontWeights.SemiBold;
        txtGpuTitle.FontSize = 11;
        txtGpuTitle.Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252));
        txtGpuTitle.Margin = new Thickness(0, 9, 0, 0);

        txtGpuDetails = new TextBlock();
        txtGpuDetails.Text = "Loading...";
        txtGpuDetails.FontSize = 11;
        txtGpuDetails.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        txtGpuDetails.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));
        txtGpuDetails.HorizontalAlignment = HorizontalAlignment.Right;
        txtGpuDetails.Margin = new Thickness(0, 9, 0, 0);

        Grid gpuGrid = new Grid();
        gpuGrid.Children.Add(txtGpuTitle);
        gpuGrid.Children.Add(txtGpuDetails);
        panel.Children.Add(gpuGrid);

        pbGpuOuter = CreateProgressBarContainer();
        pbGpuInner = CreateProgressBarFill(Color.FromRgb(46, 160, 67), Color.FromRgb(63, 185, 80));
        pbGpuOuter.Child = pbGpuInner;
        panel.Children.Add(pbGpuOuter);

        // 5. RAM SECTION
        txtRamTitle = new TextBlock();
        txtRamTitle.Text = string.Format("RAM ({0} GB Total)", detectedRamGb);
        txtRamTitle.FontWeight = FontWeights.SemiBold;
        txtRamTitle.FontSize = 11;
        txtRamTitle.Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252));
        txtRamTitle.Margin = new Thickness(0, 9, 0, 0);

        txtRamDetails = new TextBlock();
        txtRamDetails.Text = "Loading...";
        txtRamDetails.FontSize = 11;
        txtRamDetails.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        txtRamDetails.Foreground = new SolidColorBrush(Color.FromRgb(163, 113, 247));
        txtRamDetails.HorizontalAlignment = HorizontalAlignment.Right;
        txtRamDetails.Margin = new Thickness(0, 9, 0, 0);

        Grid ramGrid = new Grid();
        ramGrid.Children.Add(txtRamTitle);
        ramGrid.Children.Add(txtRamDetails);
        panel.Children.Add(ramGrid);

        pbRamOuter = CreateProgressBarContainer();
        pbRamInner = CreateProgressBarFill(Color.FromRgb(137, 87, 229), Color.FromRgb(187, 128, 255));
        pbRamOuter.Child = pbRamInner;
        panel.Children.Add(pbRamOuter);

        // 6. POWER SECTION
        txtPowerTitle = new TextBlock();
        txtPowerTitle.Text = "⚡ POWER DRAW";
        txtPowerTitle.FontWeight = FontWeights.SemiBold;
        txtPowerTitle.FontSize = 11;
        txtPowerTitle.Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252));
        txtPowerTitle.Margin = new Thickness(0, 9, 0, 0);

        txtPowerDetails = new TextBlock();
        txtPowerDetails.Text = "Loading...";
        txtPowerDetails.FontSize = 11;
        txtPowerDetails.FontFamily = new FontFamily("Consolas, Courier New, monospace");
        txtPowerDetails.Foreground = new SolidColorBrush(Color.FromRgb(240, 136, 62));
        txtPowerDetails.HorizontalAlignment = HorizontalAlignment.Right;
        txtPowerDetails.Margin = new Thickness(0, 9, 0, 0);

        Grid powerGrid = new Grid();
        powerGrid.Children.Add(txtPowerTitle);
        powerGrid.Children.Add(txtPowerDetails);
        panel.Children.Add(powerGrid);

        pbPowerOuter = CreateProgressBarContainer();
        pbPowerInner = CreateProgressBarFill(Color.FromRgb(210, 153, 34), Color.FromRgb(240, 136, 62));
        pbPowerOuter.Child = pbPowerInner;
        panel.Children.Add(pbPowerOuter);

        // 7. FOOTER
        Border sep2 = new Border();
        sep2.Height = 1;
        sep2.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
        sep2.Margin = new Thickness(0, 10, 0, 8);
        panel.Children.Add(sep2);

        chkAutoStart = new CheckBox();
        chkAutoStart.Content = " Run on Windows Startup";
        chkAutoStart.FontSize = 10;
        chkAutoStart.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
        chkAutoStart.HorizontalAlignment = HorizontalAlignment.Center;
        chkAutoStart.IsChecked = IsAutoStartEnabled();
        chkAutoStart.Click += delegate(object s, RoutedEventArgs e) {
            SetAutoStart(chkAutoStart.IsChecked == true);
        };
        panel.Children.Add(chkAutoStart);

        root.Child = panel;
        this.Content = root;
    }

    private Border CreateProgressBarContainer() {
        Border b = new Border();
        b.Height = 6;
        b.Background = new SolidColorBrush(Color.FromRgb(33, 38, 45));
        b.CornerRadius = new CornerRadius(3);
        b.Margin = new Thickness(0, 4, 0, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    private Border CreateProgressBarFill(Color start, Color end) {
        Border b = new Border();
        b.Height = 6;
        b.CornerRadius = new CornerRadius(3);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        LinearGradientBrush grad = new LinearGradientBrush();
        grad.StartPoint = new Point(0, 0.5);
        grad.EndPoint = new Point(1, 0.5);
        grad.GradientStops.Add(new GradientStop(start, 0));
        grad.GradientStops.Add(new GradientStop(end, 1));
        b.Background = grad;
        b.Width = 0;
        return b;
    }

    private void UpdateBar(Border bar, double percent, double totalWidth) {
        if (percent < 0) percent = 0;
        if (percent > 100) percent = 100;
        double targetW = (percent / 100.0) * (totalWidth > 0 ? totalWidth : 288);
        bar.Width = targetW;
    }

    private string GetActivePowerSchemeName() {
        try {
            IntPtr pGuid;
            if (PowerGetActiveScheme(IntPtr.Zero, out pGuid) == 0) {
                Guid g = (Guid)Marshal.PtrToStructure(pGuid, typeof(Guid));
                uint size = 256;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                if (PowerReadFriendlyName(IntPtr.Zero, ref g, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0) {
                    string name = Marshal.PtrToStringUni(buf);
                    Marshal.FreeHGlobal(buf);
                    return name;
                }
                Marshal.FreeHGlobal(buf);
            }
        } catch {}
        return "Balanced";
    }

    private void UpdateStats() {
        try {
            string schemeName = GetActivePowerSchemeName();
            txtProfileBadge.Text = string.Format("🔋 Profile: {0} (Efficient Boost)", schemeName);

            // 1. CPU LOAD & FREQ
            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user)) {
                if (prevKernel != 0) {
                    long kernelDelta = kernel - prevKernel;
                    long userDelta = user - prevUser;
                    long idleDelta = idle - prevIdle;
                    long totalDelta = kernelDelta + userDelta;
                    if (totalDelta > 0) {
                        double cpuLoad = ((double)(totalDelta - idleDelta) / totalDelta) * 100.0;
                        if (cpuLoad < 0) cpuLoad = 0;
                        if (cpuLoad > 100) cpuLoad = 100;

                        int coreCount = Environment.ProcessorCount;
                        PROCESSOR_POWER_INFORMATION[] ppi = new PROCESSOR_POWER_INFORMATION[coreCount];
                        int size = Marshal.SizeOf(typeof(PROCESSOR_POWER_INFORMATION)) * coreCount;
                        CallNtPowerInformation(11, IntPtr.Zero, 0, ppi, size);
                        uint mhz = ppi[0].CurrentMhz;
                        double ghz = mhz / 1000.0;

                        txtCpuDetails.Text = string.Format("{0:0}%  |  {1:0.00} GHz", cpuLoad, ghz);
                        UpdateBar(pbCpuInner, cpuLoad, pbCpuOuter.ActualWidth);
                    }
                }
                prevIdle = idle;
                prevKernel = kernel;
                prevUser = user;
            }

            // 2. GPU TELEMETRY
            double gpuWatts = 0;
            if (nvmlAvailable && nvmlDev != IntPtr.Zero) {
                uint temp = 0;
                nvmlDeviceGetTemperature(nvmlDev, 0, out temp);

                uint powerMilli = 0;
                nvmlDeviceGetPowerUsage(nvmlDev, out powerMilli);
                gpuWatts = powerMilli / 1000.0;

                nvmlUtilization_t util;
                nvmlDeviceGetUtilizationRates(nvmlDev, out util);

                txtGpuDetails.Text = string.Format("{0}°C  |  {1:0.0} W  |  {2}%", temp, gpuWatts, util.gpu);
                UpdateBar(pbGpuInner, util.gpu, pbGpuOuter.ActualWidth);
            } else {
                txtGpuDetails.Text = "Standby (iGPU Mode)";
                UpdateBar(pbGpuInner, 0, pbGpuOuter.ActualWidth);
            }

            // 3. RAM TELEMETRY
            MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem)) {
                double totalGB = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                double freeGB = mem.ullAvailPhys / (1024.0 * 1024 * 1024);
                double usedGB = totalGB - freeGB;

                txtRamDetails.Text = string.Format("{0:0.0} / {1:0} GB ({2}%)", usedGB, totalGB, mem.dwMemoryLoad);
                UpdateBar(pbRamInner, mem.dwMemoryLoad, pbRamOuter.ActualWidth);
            }

            // 4. POWER DRAW (TOTAL = CPU RAPL + GPU NVML)
            double cpuWatts = 0;
            if (cpuPowerCounter != null) {
                try {
                    float cpuMilli = cpuPowerCounter.NextValue();
                    cpuWatts = cpuMilli / 1000.0;
                    if (cpuWatts < 0) cpuWatts = 0;
                } catch {
                    cpuWatts = 18.0;
                }
            }
            double totalPower = cpuWatts + gpuWatts;
            txtPowerDetails.Text = string.Format("{0:0.0} W  (C: {1:0.0}W  G: {2:0.0}W)", totalPower, cpuWatts, gpuWatts);

            double powerPercent = (totalPower / 175.0) * 100.0;
            UpdateBar(pbPowerInner, powerPercent, pbPowerOuter.ActualWidth);

        } catch {}
    }

    private void SavePosition() {
        try {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\LegionMonitor");
            if (key != null) {
                key.SetValue("Left", (int)this.Left);
                key.SetValue("Top", (int)this.Top);
                key.SetValue("Pinned", isPinned ? 1 : 0);
                key.Close();
            }
        } catch {}
    }

    private void LoadPosition() {
        try {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\LegionMonitor");
            if (key != null) {
                object l = key.GetValue("Left");
                object t = key.GetValue("Top");
                object p = key.GetValue("Pinned");
                if (l != null && t != null) {
                    this.Left = Convert.ToDouble(l);
                    this.Top = Convert.ToDouble(t);
                }
                if (p != null) {
                    isPinned = (Convert.ToInt32(p) == 1);
                    this.Topmost = isPinned;
                }
                key.Close();
            }
        } catch {}
    }

    private bool IsAutoStartEnabled() {
        try {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key != null) {
                object val = key.GetValue("LegionMonitorOverlay");
                key.Close();
                return (val != null);
            }
        } catch {}
        return false;
    }

    private void SetAutoStart(bool enable) {
        try {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null) {
                if (enable) {
                    string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    key.SetValue("LegionMonitorOverlay", "\"" + exePath + "\"");
                } else {
                    key.DeleteValue("LegionMonitorOverlay", false);
                }
                key.Close();
            }
        } catch {}
    }
}
