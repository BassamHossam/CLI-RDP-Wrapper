using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace rdpWrapper {

  internal enum WrapperInstalledState {
    Unknown,
    NotInstalled,
    ThirdParty,
    RdpWrap,
    TermWrap,
  }

  public enum SupportedWrappers {
    TermWrap = 0,
#if !LITEVERSION
    RdpWrap = 1,
#endif
  }

  internal class Wrapper {

    private const string RegKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
    private const string RegTermServiceKey = @"SYSTEM\CurrentControlSet\Services\TermService";
    private const string RegRdpKey = @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp";
    private const string RegWinLogonKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string RegTsKey = @"SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services";

    private const string ValueSingleSession = "fSingleSessionPerUser";
    private const string ValueDenyTsConnections = "fDenyTSConnections";
    private const string ValuePort = "PortNumber";
    private const string ValueDontDisplayLastUserName = "DontDisplayLastUsername";
    private const string ValueHonorLegacy = "HonorLegacySettings";
    private const string ValueNla = "UserAuthentication";
    private const string ValueSecurity = "SecurityLayer";
    private const string ValueShadow = "Shadow";
    private const string ValueDisableCam = "fDisableCam";
    private const string ValueDisableCameraRedir = "fDisableCameraRedir";
    private const string ValueDisableAudioCapture = "fDisableAudioCapture";
    private const string ValueDisablePNPRedir = "fDisablePNPRedir";
    private const string ValueUsbRedirectionEnableMode = "fUsbRedirectionEnableMode";
    private const string ValueRedirectionWarningDialogVersion = "RedirectionWarningDialogVersion";

    private const string RdpServiceName = "TermService";

    internal const string RdpWrapIniName = "rdpwrap.ini";
    private const string RdpWrapDllName = "rdpwrap.dll";
    private const string TermSrvName = "termsrv.dll";
    private const string TermWrapDllName = "TermWrap.dll";
    private const string UmWrapDllName = "UmWrap.dll";
    private const string EndpWrapDllName = "EndpWrap.dll";
    private const string ZydisDllName = "Zydis.dll";

    private readonly Logger logger;
    private readonly ServiceHelper serviceHelper;
    private readonly RegistryView registryView = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;

    internal Wrapper(Logger logger) {
      this.logger = logger;
      serviceHelper = new ServiceHelper(logger);

      WrapperFolderPath = Path.Combine(Environment.SystemDirectory, "Maintainer");

      if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess) {
        TermSrvFile = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative"), TermSrvName);
      }
      else {
        TermSrvFile = Path.Combine(Environment.SystemDirectory, TermSrvName);
      }
    }

    internal bool SingleSessionPerUser {
      get {
        bool result;
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var key = baseKey.OpenSubKey(RegKey))
            result = key != null && Convert.ToInt32(key.GetValue(ValueSingleSession, 0)) != 0;
          using (var key = baseKey.OpenSubKey(RegTsKey))
            result = result && (key != null && Convert.ToInt32(key.GetValue(ValueSingleSession, 0)) != 0);
        }
        return result;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)){
          using (var key = baseKey.OpenSubKey(RegKey, writable: true))
            key?.SetValue(ValueSingleSession, value ? 1 : 0, RegistryValueKind.DWord);
          using (var key = baseKey.OpenSubKey(RegTsKey, writable: true))
            key?.SetValue(ValueSingleSession, value ? 1 : 0, RegistryValueKind.DWord);
        }
      }
    }

    internal int MaximumConnectionsAllowed {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var key = baseKey.OpenSubKey(RegRdpKey)) {
            if (key != null) {
              var value = Convert.ToInt32(key.GetValue("MaxInstanceCount", -1));
              if (value > 0)
                return value;
            }
            return 0;
          }
        }
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)){
          if (value > 0) {
            using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
              key?.SetValue("MaxInstanceCount", value, RegistryValueKind.DWord); //max 999999
            using (var key = baseKey.OpenSubKey(RegTsKey, writable: true)) {
              //Enforce it (same effect as enabling the policy)
              key?.SetValue("fPolicyLimitConnections", 1, RegistryValueKind.DWord);
              key?.SetValue("MaxConnections", value, RegistryValueKind.DWord);
            }
          }
          else {
            using (var key = baseKey.OpenSubKey(RegTsKey, writable: true)) {
              key?.DeleteValue("fPolicyLimitConnections");
              key?.DeleteValue("MaxConnections");
            }
            using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
              key?.DeleteValue("MaxInstanceCount");
          }
        }
      }
    }

    internal bool AllowTsConnections {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDenyTsConnections, 0)) == 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegKey, writable: true))
          key?.SetValue(ValueDenyTsConnections, value ? 0 : 1, RegistryValueKind.DWord);
      }
    }

    internal bool HonorLegacy {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueHonorLegacy, 0)) != 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegKey, writable: true))
          key?.SetValue(ValueHonorLegacy, value ? 1 : 0, RegistryValueKind.DWord);
      }
    }

    internal int RdpPort {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey))
          return key != null
            ? Convert.ToInt32(key.GetValue(ValuePort, 3389))
            : 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
          key?.SetValue(ValuePort, value, RegistryValueKind.DWord);
      }
    }

    internal int UserAuthentication {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey))
          return key != null
            ? Convert.ToInt32(key.GetValue(ValueNla, 0))
            : 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
          key?.SetValue(ValueNla, value, RegistryValueKind.DWord);
      }
    }

    internal int SecurityLayer {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey))
          return key != null
            ? Convert.ToInt32(key.GetValue(ValueSecurity, 0))
            : 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
          key?.SetValue(ValueSecurity, value, RegistryValueKind.DWord);
      }
    }

    internal int ShadowOptions {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegRdpKey))
          return key != null
            ? Convert.ToInt32(key.GetValue(ValueShadow, 0))
            : 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var key = baseKey.OpenSubKey(RegRdpKey, writable: true))
            key?.SetValue(ValueShadow, value, RegistryValueKind.DWord);
          using (var key = baseKey.OpenSubKey(RegTsKey, writable: true))
            key?.SetValue(ValueShadow, value, RegistryValueKind.DWord);
        }
      }
    }

    internal bool DontDisplayLastUser {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegWinLogonKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDontDisplayLastUserName, 0)) != 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegWinLogonKey, writable: true))
          key?.SetValue(ValueDontDisplayLastUserName, value ? 1 : 0, RegistryValueKind.DWord);
      }
    }

    internal bool AllowHostPlaybackRedirect {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDisableCam, 1)) == 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey, writable: true)) {
          key?.SetValue(ValueDisableCam, value ? 0 : 1, RegistryValueKind.DWord);
        }
      }
    }

    internal bool AllowClientVideoCapture {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDisableCameraRedir, 1)) == 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey, writable: true)) {
          key?.SetValue(ValueDisableCameraRedir, value ? 0 : 1, RegistryValueKind.DWord);
        }
      }
    }

    internal bool AllowClientAudioCapture {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDisableAudioCapture, 0)) == 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey, writable: true)) {
          key?.SetValue(ValueDisableAudioCapture, value ? 0 : 1, RegistryValueKind.DWord);
        }
      }
    }

    internal bool AllowPnpRedirect {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey))
          return key != null && Convert.ToInt32(key.GetValue(ValueDisablePNPRedir, 1)) == 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var key = baseKey.OpenSubKey(RegTsKey, writable: true))
            key?.SetValue(ValueDisablePNPRedir, value ? 0 : 1, RegistryValueKind.DWord);
        }
      }
    }

    internal bool? RestrictUsbRedirection {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey + "\\Client"))
          return key != null
            ? Convert.ToInt32(key.GetValue(ValueUsbRedirectionEnableMode, 1)) == 1
            : null;
        //1 => Adminstrators Only
        //2 => Adminstrators and Users
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var key = baseKey.OpenSubKey(RegTsKey + "\\Client", writable: true)) {
            if (value.HasValue) {
              key?.SetValue(ValueUsbRedirectionEnableMode, value.Value ? 1 : 2, RegistryValueKind.DWord);
            }
            else {
              key?.DeleteValue(ValueUsbRedirectionEnableMode, false);
            }
          }
        }
      }
    }

    internal bool DisableSecurityWarning {
      get {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey + "\\Client"))
          return key != null && Convert.ToInt32(key.GetValue(ValueRedirectionWarningDialogVersion, 0)) != 0;
      }
      set {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView))
        using (var key = baseKey.OpenSubKey(RegTsKey + "\\Client", writable: true))
          key?.SetValue(ValueRedirectionWarningDialogVersion, value ? 1 : 0, RegistryValueKind.DWord);
      }
    }

    internal string WrapperPath { get; private set; }

    internal readonly string TermSrvFile;
    internal readonly string WrapperFolderPath;

    internal WrapperInstalledState CheckWrapperInstalled() {
      WrapperPath = string.Empty;
      try {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
          using (var serviceKey = baseKey.OpenSubKey(RegTermServiceKey)) {
            if (serviceKey == null)
              return WrapperInstalledState.Unknown;
            var termServiceHost = serviceKey.GetValue("ImagePath") as string;
            if (string.IsNullOrWhiteSpace(termServiceHost) || !termServiceHost.ToLower().Contains("svchost.exe"))
              return WrapperInstalledState.ThirdParty;
          }

          string termServicePath;
          using (var paramKey = baseKey.OpenSubKey(RegTermServiceKey + "\\Parameters")) {
            if (paramKey == null)
              return WrapperInstalledState.Unknown;

            termServicePath = paramKey.GetValue("ServiceDll") as string;
            if (string.IsNullOrWhiteSpace(termServicePath))
              return WrapperInstalledState.Unknown;
          }

          //if (Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem) {
          //  if (!File.Exists(termServicePath))
          //    return WrapperInstalledState.Unknown;
          //}
          var wrapperName = Path.GetFileName(termServicePath);
          if (TermSrvName.Equals(wrapperName, StringComparison.OrdinalIgnoreCase))
            return WrapperInstalledState.NotInstalled;
          if (RdpWrapDllName.Equals(wrapperName, StringComparison.OrdinalIgnoreCase)) {
            WrapperPath = termServicePath;
            //WrapperFolderPath = Path.GetDirectoryName(termServicePath);
            return WrapperInstalledState.RdpWrap;
          }
          if (TermWrapDllName.Equals(wrapperName, StringComparison.OrdinalIgnoreCase)) {
            WrapperPath = termServicePath;
            return WrapperInstalledState.TermWrap;
          }
        }
        return WrapperInstalledState.ThirdParty;
      }
      catch {
        return WrapperInstalledState.Unknown;
      }
    }

    internal static string GetVersionString(FileVersionInfo versionInfo) {
      return versionInfo.ProductMajorPart + "." + versionInfo.ProductMinorPart + "." + versionInfo.ProductBuildPart + "." + versionInfo.ProductPrivatePart;
    }

    #region Service wrapper

    internal void StopService(TimeSpan timeout) {
      serviceHelper.Stop(RdpServiceName, timeout);
    }

    internal void StartService(TimeSpan timeout) {
      serviceHelper.Start(RdpServiceName, timeout);
    }

    internal ServiceControllerStatus? GetServiceState() {
      return serviceHelper.GetState(RdpServiceName);
    }

    #endregion

#if !LITEVERSION
    internal void GenerateIniFile(string destFilePath, bool executeCleanup = true, Action<string> onError = null) {

      string iniFile = null;
      string offsetFinder = null;
      string zydis = null;
      try {
        logger.Log("Generating config...");
        var workingDir = Path.GetTempPath();
        iniFile = ExtractResourceFile(RdpWrapIniName, workingDir, true);
        offsetFinder = ExtractResourceFile("RDPWrapOffsetFinder.exe", workingDir, archPrefix: true);
        zydis = ExtractResourceFile(ZydisDllName, workingDir, archPrefix: true);
        var p = StartProcess("cmd", $"/c \"{offsetFinder}\" >> {RdpWrapIniName} & exit", workingDir);
        p.WaitForExit();
        if (p.ExitCode == 0)
          logger.Log(" Done", Logger.StateKind.Info, false);
        else
          logger.Log(" Error", Logger.StateKind.Error, false);
      }
      catch (Exception ex) {
        var message = "Failed to generate config: " + ex.Message;
        logger.Log(message, Logger.StateKind.Error);
        onError?.Invoke(message);
      }
      finally {
        if (executeCleanup) {
          SafeDeleteFile(offsetFinder);
          SafeDeleteFile(zydis);
        }
      }

      if (string.IsNullOrEmpty(iniFile) || !File.Exists(iniFile)) return;

      try {
        serviceHelper.Stop(RdpServiceName, TimeSpan.FromSeconds(10));

        SafeDeleteFile(destFilePath);
        File.Move(iniFile, destFilePath);

        serviceHelper.Start(RdpServiceName, TimeSpan.FromSeconds(10));
      }
      catch (Exception ex) {
        var message = "Failed to update config: " + ex.Message;
        logger.Log(message, Logger.StateKind.Error);
        onError?.Invoke(message);
      }
    }
#endif

    internal void Install(SupportedWrappers wrapToInstall, bool addDefenderExclusion) {
      //Uninstall();
      var prevServiceState = serviceHelper.GetState(RdpServiceName);
      if (prevServiceState is ServiceControllerStatus.Running)
        serviceHelper.Stop(RdpServiceName, TimeSpan.FromSeconds(10));

      Directory.CreateDirectory(WrapperFolderPath);
      logger.Log("Folder created: " + WrapperFolderPath);

      AddDirectorySecurity(WrapperFolderPath, [
        "S-1-5-18", // Local System account
        "S-1-5-6", // Service group
        "S-1-5-32-545", // SID for "Users"
      ] , FileSystemRights.FullControl, AccessControlType.Allow);

      if (addDefenderExclusion && IsDefenderActive() && !IsFolderExcluded(WrapperFolderPath)) {
        SetFolderExclusion(WrapperFolderPath);
      }

      string wrapPath;
      switch (wrapToInstall) {
        case SupportedWrappers.TermWrap:
          wrapPath = ExtractResourceFile(TermWrapDllName, WrapperFolderPath, archPrefix: true);
          logger.Log("Extracted TermWrap.dll -> " + wrapPath);
          var zydis = ExtractResourceFile(ZydisDllName, WrapperFolderPath, archPrefix: true);
          logger.Log("Extracted zydis.dll -> " + zydis);
          if (Environment.Is64BitOperatingSystem) {
            var umWrap = ExtractResourceFile(UmWrapDllName, WrapperFolderPath, archPrefix: true);
            logger.Log("Extracted umWrap.dll -> " + umWrap);
            var endpWrap = ExtractResourceFile(EndpWrapDllName, WrapperFolderPath, archPrefix: true);
            logger.Log("Extracted endpWrap.dll -> " + endpWrap);
          }

          break;
#if !LITEVERSION
        case SupportedWrappers.RdpWrap:
          wrapPath = ExtractResourceFile(RdpWrapDllName, WrapperFolderPath, archPrefix: true);
          logger.Log("Extracted rdpWrap.dll -> " + wrapPath);
          break;
#endif
        default:
          throw new NotSupportedException($"Wrapper of type '{wrapToInstall}' is not supported.");
      }

      logger.Log("Configuring service library...");
      using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
        using (var reg = baseKey.OpenSubKey(RegTermServiceKey + "\\Parameters", writable: true)) {
          if (reg == null) {
            logger.Log($"OpenKey error (code {Marshal.GetLastWin32Error()}).", Logger.StateKind.Error);
            return;
          }
          reg.SetValue("ServiceDll", wrapPath, RegistryValueKind.ExpandString);
        }
      }
#if !LITEVERSION
      if (wrapToInstall == SupportedWrappers.RdpWrap) {
        GenerateIniFile(Path.Combine(WrapperFolderPath, RdpWrapIniName));
      }
#endif
      logger.Log(" Done", Logger.StateKind.Info, false);
      if (prevServiceState is ServiceControllerStatus.Running)
        serviceHelper.Start(RdpServiceName, TimeSpan.FromSeconds(10));
    }

    internal void Uninstall() {
      logger.Log("Resetting service library...");
      using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, registryView)) {
        using (var reg = baseKey.OpenSubKey(RegTermServiceKey + "\\Parameters", writable: true)) {
          if (reg == null) {
            logger.Log($"OpenKey error (code {Marshal.GetLastWin32Error()}).", Logger.StateKind.Error);
            return;
          }
          reg.SetValue("ServiceDll", @"%SystemRoot%\System32\termsrv.dll", RegistryValueKind.ExpandString);
        }
      }
      logger.Log(" Done", Logger.StateKind.Info, false);

      // ── Remove Defender exclusion if it was added ────────────────────────────
      if (IsDefenderActive() && IsFolderExcluded(WrapperFolderPath)) {
        logger.Log("Removing Defender exclusion for: " + WrapperFolderPath);
        SetFolderExclusion(WrapperFolderPath, add: false);
      }

      // ── Robust service stop with stuck-process recovery ──────────────────────
      StopServiceRobust();

      // ── Delete wrapper folder (with retry after stuck-process kill) ──────────
      logger.Log("Removed folder: " + WrapperFolderPath);
      if (!TryDeleteFolder(WrapperFolderPath, retryAfterKill: true)) {
        logger.Log("Could not fully remove wrapper folder. Manual cleanup may be required.", Logger.StateKind.Error);
      }

      // ── Restart TermService cleanly ──────────────────────────────────────────
      serviceHelper.Start(RdpServiceName, TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Stops TermService gracefully. If the service is stuck in STOP_PENDING
    /// (SC error 1061 / 1056), locates the svchost process hosting TermService
    /// by checking its loaded modules and forcefully terminates it so the SCM
    /// can clean up.
    /// </summary>
    private void StopServiceRobust() {
      const int maxWaitSeconds = 15;
      var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);

      try {
        using var sc = new ServiceController(RdpServiceName);
        var status = sc.Status;

        if (status == ServiceControllerStatus.Stopped) {
          logger.Log($"{RdpServiceName} is already stopped.");
          return;
        }

        if (status == ServiceControllerStatus.Running) {
          logger.Log($"Stopping {RdpServiceName}...");
          try { sc.Stop(); } catch { /* ignore – might fail if already transitioning */ }
        }

        // Poll until Stopped or deadline
        while (DateTime.UtcNow < deadline) {
          sc.Refresh();
          if (sc.Status == ServiceControllerStatus.Stopped) {
            logger.Log($"{RdpServiceName} stopped.", Logger.StateKind.Info);
            return;
          }
          System.Threading.Thread.Sleep(500);
        }

        // ---- Timed out: service is likely stuck in STOP_PENDING ----
        sc.Refresh();
        if (sc.Status == ServiceControllerStatus.StopPending) {
          logger.Log($"{RdpServiceName} stuck in STOP_PENDING – killing hosting svchost...", Logger.StateKind.Error);
          KillSvchostHostingService(RdpServiceName);

          // Give SCM a moment to notice the process is gone
          System.Threading.Thread.Sleep(2000);
          sc.Refresh();
          if (sc.Status == ServiceControllerStatus.Stopped)
            logger.Log($"{RdpServiceName} stopped after svchost kill.", Logger.StateKind.Info);
          else
            logger.Log($"{RdpServiceName} still not stopped. Will attempt file deletion anyway.", Logger.StateKind.Error);
        }
      }
      catch (Exception ex) {
        logger.Log($"StopServiceRobust error: {ex.Message}", Logger.StateKind.Error);
      }
    }

    /// <summary>
    /// Finds the svchost.exe process that hosts the given service name by
    /// querying the Win32 API for service PID, then kills it.
    /// </summary>
    private void KillSvchostHostingService(string serviceName) {
      try {
        // Use sc.exe queryex to get the PID of the stuck service host
        var psi = new ProcessStartInfo("sc.exe", $"queryex {serviceName}") {
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        };
        using var queryProc = Process.Start(psi);
        var output = queryProc.StandardOutput.ReadToEnd();
        queryProc.WaitForExit();

        // Parse PID from output (line like: "PID                  : 1234")
        int pid = 0;
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
          var trimmed = line.Trim();
          if (trimmed.StartsWith("PID", StringComparison.OrdinalIgnoreCase) && trimmed.Contains(":")) {
            var pidStr = trimmed.Substring(trimmed.IndexOf(':') + 1).Trim();
            int.TryParse(pidStr, out pid);
            break;
          }
        }

        if (pid > 0) {
          try {
            var hostProc = Process.GetProcessById(pid);
            logger.Log($"Killing svchost PID={pid} hosting {serviceName}...");
            hostProc.Kill();
            hostProc.WaitForExit(5000);
            logger.Log($"svchost PID={pid} terminated.", Logger.StateKind.Info);
          }
          catch (Exception ex) {
            logger.Log($"Failed to kill PID={pid}: {ex.Message}", Logger.StateKind.Error);
          }
        }
        else {
          logger.Log("Could not determine svchost PID from sc queryex output.", Logger.StateKind.Error);
        }
      }
      catch (Exception ex) {
        logger.Log($"KillSvchostHostingService error: {ex.Message}", Logger.StateKind.Error);
      }
    }

    /// <summary>
    /// Attempts to delete a folder. If it fails due to access denied (locked DLLs),
    /// waits briefly and retries once. If retryAfterKill is true and files are still
    /// locked, logs specific files that failed.
    /// </summary>
    private bool TryDeleteFolder(string path, bool retryAfterKill = false) {
      try {
        Directory.Delete(path, recursive: true);
        return true;
      }
      catch (DirectoryNotFoundException) {
        return true; // already gone
      }
      catch (Exception ex) when (retryAfterKill) {
        logger.Log($"First delete attempt failed ({ex.Message}), waiting and retrying...", Logger.StateKind.Error);
        System.Threading.Thread.Sleep(2000);
        try {
          // Try deleting file by file so we can report exactly which ones fail
          if (Directory.Exists(path)) {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) {
              try { File.Delete(file); }
              catch (Exception fex) {
                logger.Log($"Cannot delete '{Path.GetFileName(file)}': {fex.Message}", Logger.StateKind.Error);
              }
            }
            try { Directory.Delete(path, recursive: true); } catch { }
          }
          return !Directory.Exists(path);
        }
        catch (Exception retryEx) {
          logger.Log($"Retry delete failed: {retryEx.Message}", Logger.StateKind.Error);
          return false;
        }
      }
      catch (Exception ex) {
        logger.Log(ex.Message, Logger.StateKind.Error);
        return false;
      }
    }

    #region supplementary methods

#if DEBUG
    public void EncryptResources() {
      var externalsPath = Path.Combine(Path.GetDirectoryName(Updater.CurrentFileLocation), "../externals");
      var di = new DirectoryInfo(externalsPath);
      if (!di.Exists)
        return;
      foreach (var fi in di.EnumerateFiles("*.*", SearchOption.AllDirectories)) {
        if (fi.Extension == ".gz") continue;
        var newFileName = fi.FullName + ".gz";
        using (var inputFileStream = File.OpenRead(fi.FullName))
        using (var outputFileStream = File.Create(newFileName))
        using (var gzipStream = new GZipStream(outputFileStream, CompressionMode.Compress))
          inputFileStream.CopyTo(gzipStream);
        fi.Delete();
      }
    }

    public void DecryptResources() {
      var externalsPath = Path.Combine(Path.GetDirectoryName(Updater.CurrentFileLocation), "../externals");
      var di = new DirectoryInfo(externalsPath);
      if (!di.Exists)
        return;

      foreach (var fi in di.EnumerateFiles("*.*", SearchOption.AllDirectories)) {
        if (fi.Extension != ".gz") continue;
        var newFileName = fi.FullName.Substring(0, fi.FullName.Length - 3);
        using (var inputFileStream = File.OpenRead(fi.FullName))
        using (var outputFileStream = File.Create(newFileName))
        using (var gzipStream = new GZipStream(inputFileStream, CompressionMode.Decompress))
          gzipStream.CopyTo(outputFileStream);
        //fi.Delete();
      }
    }
#endif

    private string ExtractResourceFile(string resourceName, string path, bool deleteExisting = false, bool archPrefix = false) {
      var filePath = Path.Combine(path, resourceName);
      if (File.Exists(filePath)) {
        if (!deleteExisting)
          return filePath;
        SafeDeleteFile(filePath);
      }
      try {
        var type = typeof(SupportedWrappers);
        resourceName += ".gz";
        var scriptsPath = archPrefix
          ? $"{type.Namespace}.externals.{(Environment.Is64BitOperatingSystem ? "x64" : "x86")}.{resourceName}"
          : $"{type.Namespace}.externals.{resourceName}";
        using var stream = type.Assembly.GetManifestResourceStream(scriptsPath);
        if (stream == null)
          throw new Exception($"Resource '{resourceName}' is not found!");
        using (var fileStream = File.Create(filePath)) {
          stream.Seek(0, SeekOrigin.Begin);
          using (var gzipStream = new GZipStream(stream, CompressionMode.Decompress))
            gzipStream.CopyTo(fileStream);
        }
        return filePath;
      }
      catch (Exception ex) {
        logger.Log(ex.Message, Logger.StateKind.Error);
        throw;
      }
    }

    private void SafeDeleteFile(string filePath) {
      try {
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath)) File.Delete(filePath);
      }
      catch (Exception ex) {
        logger.Log(ex.Message, Logger.StateKind.Error);
      }
    }

    private static Process StartProcess(string app, string arg, string workingDir = null) {

      var p = new Process();
      p.StartInfo.UseShellExecute = true;
      p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
      p.StartInfo.FileName = app;
      p.StartInfo.WorkingDirectory = (workingDir ?? Path.GetDirectoryName(app)) ?? string.Empty;
      p.StartInfo.Arguments = arg;
      p.StartInfo.RedirectStandardOutput = false;
      p.StartInfo.RedirectStandardInput = false;
      p.StartInfo.RedirectStandardError = false;
      p.Start();
      return p;
    }

    private static void AddDirectorySecurity(string fileName, string[] sids, FileSystemRights rights, AccessControlType controlType) {
      var dInfo = new DirectoryInfo(fileName);
      var dSecurity = dInfo.GetAccessControl();
      foreach(var sid in sids) {
        var sIdentifier = new SecurityIdentifier(sid);
        dSecurity.AddAccessRule(new FileSystemAccessRule(sIdentifier, rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, controlType));
      }
      dInfo.SetAccessControl(dSecurity);
    }

    private bool IsDefenderActive() {
      try {
        var state = serviceHelper.GetState("WinDefend");
        if (state != null) {
          bool isActive = (state == ServiceControllerStatus.Running);
          logger.Log("Defender is " + (isActive ? "active" : "not active"));
          return isActive;
        }

        using var process = Process.Start(new ProcessStartInfo {
          FileName = "powershell",
          Arguments = "Get-MpComputerStatus | Select-Object -ExpandProperty AMServiceEnabled",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        });
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var isActive2 = output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        logger.Log("Defender is " + (isActive2 ? "active" : "not active"));
        return isActive2;
      }
      catch (Exception ex) {
        logger.Log("Error checking is defender active: " + ex.Message, Logger.StateKind.Error);
        return false;
      }
    }

    private bool IsFolderExcluded(string folderPath) {
      try {
        using var process = Process.Start(new ProcessStartInfo {
          FileName = "powershell",
          Arguments = "Get-MpPreference | Select-Object -ExpandProperty ExclusionPath",
          RedirectStandardOutput = true,
          RedirectStandardError = true,
          UseShellExecute = false,
          CreateNoWindow = true
        });
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Any(p => string.Equals(p, folderPath, StringComparison.OrdinalIgnoreCase));
      }
      catch (Exception ex) {
        logger.Log("Error checking is folder excluded: " + ex.Message, Logger.StateKind.Error);
        return false;
      }
    }

    private bool SetFolderExclusion(string folderPath, bool add = true) {
      try {
        var process = Process.Start(new ProcessStartInfo {
          FileName = "powershell",
          Arguments = $"{(add ? "Add" : "Remove")}-MpPreference -ExclusionPath '{folderPath}'",
          Verb = "runas",
          UseShellExecute = true
        });
        if (process != null) {
          process.WaitForExit();
          if (process.ExitCode != 0) return false;
          logger.Log("Defender folder exclusion configured", Logger.StateKind.Info);
          return true;
        }
        logger.Log("UAC not confirmed by user.");
        return false;
      }
      catch (System.ComponentModel.Win32Exception ex) {
        logger.Log(ex.NativeErrorCode == 1223
            ? "UAC not confirmed by user"
            : $"Error starting command: {ex.Message}",
          Logger.StateKind.Error);
        return false;
      }
      catch (Exception ex) {
        logger.Log("Error changing exclusion: " + ex.Message, Logger.StateKind.Error);
        return false;
      }
    }

    public void SetFirewallPort(int port) {
      try {
        dynamic fwPolicy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")); //INetFwPolicy2
        dynamic inboundRule = null; //INetFwRule
        try {
          inboundRule = fwPolicy.Rules.Item("rdpWrapper");
        }
        catch (System.IO.FileNotFoundException) {
          //ignore
        }
        var createNewRule = inboundRule != null;
        if (inboundRule == null) {
          inboundRule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
          inboundRule.Name = "rdpWrapper";
        }

        inboundRule.Enabled = true;
        inboundRule.Action = 1;// NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
        inboundRule.Protocol = 6; // TCP
        inboundRule.LocalPorts = port.ToString();
        inboundRule.Profiles = fwPolicy.CurrentProfileTypes;

        if (!createNewRule) {
          fwPolicy.Rules.Add(inboundRule);
          logger.Log($"Firewall rule added for port {port}", Logger.StateKind.Info);
        }
        else {
          logger.Log($"Firewall rule updated to use port {port}", Logger.StateKind.Info);
        }
      }
      catch (Exception ex) {
        logger.Log($"Error configuring firewall: " + ex.Message, Logger.StateKind.Error);
      }
    }

    #endregion
  }
}
