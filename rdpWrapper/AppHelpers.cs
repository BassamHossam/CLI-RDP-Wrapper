using System;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace rdpWrapper {

  // ── Logger ─────────────────────────────────────────────────────────────────
  internal class Logger {
    public enum StateKind { Default, Info, Error }
    public event Action<string, StateKind, bool> OnNewLogEvent;
    private StreamWriter _sw;

    public Logger() { }
    protected Logger(string logPath) {
      try { _sw = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true }; }
      catch { /* ignore log file errors */ }
    }

    public void Log(string message, StateKind state = StateKind.Default, bool newLine = true) {
      try { _sw?.WriteLine($"{DateTime.Now:T} - {message}"); } catch { }
      OnNewLogEvent?.Invoke(message, state, newLine);
    }

    public virtual void Dispose() { try { _sw?.Dispose(); } catch { } }
  }

  // ── ServiceHelper ───────────────────────────────────────────────────────────
  internal class ServiceHelper {
    private readonly Logger _log;
    public ServiceHelper(Logger log) => _log = log;

    public void Start(string name, TimeSpan timeout) {
      try {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Running) { _log.Log($"{name} already running."); return; }
        _log.Log($"Starting {name}...");
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, timeout);
        _log.Log($"{name} started.", Logger.StateKind.Info);
      }
      catch (Exception ex) { _log.Log($"Start error: {ex.Message}", Logger.StateKind.Error); }
    }

    public void Stop(string name, TimeSpan timeout) {
      try {
        using var sc = new ServiceController(name);
        if (sc.Status == ServiceControllerStatus.Stopped) { _log.Log($"{name} already stopped."); return; }
        _log.Log($"Stopping {name}...");
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        _log.Log($"{name} stopped.", Logger.StateKind.Info);
      }
      catch (Exception ex) { _log.Log($"Stop error: {ex.Message}", Logger.StateKind.Error); }
    }

    public ServiceControllerStatus? GetState(string name) {
      try { using var sc = new ServiceController(name); return sc.Status; }
      catch { return null; }
    }
  }

  // ── OSHelper ────────────────────────────────────────────────────────────────
  internal static class OSHelper {
    public static bool IsWindowsServer {
      get {
        try {
          var v = Microsoft.Win32.Registry.LocalMachine
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?.GetValue("InstallationType") as string;
          return v?.Contains("Server") == true;
        }
        catch { return false; }
      }
    }

    public static bool IsCompatible(bool _, out string err, out Action fix) {
      err = null; fix = null;
      if (Environment.OSVersion.Platform != PlatformID.Win32NT) {
        err = "This application only runs on Windows.";
        return false;
      }
      return true;
    }
  }

  // ── WinApiHelper ────────────────────────────────────────────────────────────
  internal static class WinApiHelper {
    public static bool CheckRunningInstances(bool _, bool __) {
      var me = System.Diagnostics.Process.GetCurrentProcess();
      return System.Diagnostics.Process.GetProcessesByName(me.ProcessName).Length > 1;
    }
  }

  // ── WinStationHelper ────────────────────────────────────────────────────────
  internal static class WinStationHelper {
    public static bool IsListenerWorking() {
      try {
        var port = Microsoft.Win32.Registry.GetValue(
          @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp",
          "PortNumber", 3389);
        var p = Convert.ToInt32(port ?? 3389);
        foreach (var ep in System.Net.NetworkInformation.IPGlobalProperties
                              .GetIPGlobalProperties().GetActiveTcpListeners())
          if (ep.Port == p) return true;
        return false;
      }
      catch { return false; }
    }
  }

  // ── Updater (name / version stubs) ──────────────────────────────────────────
  internal static class Updater {
    public static string ApplicationName  => "rdpWrapper";
    public static string ApplicationTitle => "RDP Wrapper";
    public static string CurrentVersion   =>
      typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string CurrentFileLocation =>
      System.Reflection.Assembly.GetExecutingAssembly().Location ?? typeof(Updater).Assembly.Location;
  }
}
