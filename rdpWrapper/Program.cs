using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace rdpWrapper {
  internal static class Program {

    private const int CodeException = -1;
    private const int CodeOk = 0;
    private const int CodeInvalidArgs = 1;

    private static int result = CodeOk;
    private static Logger logger;

    [STAThread]
    private static void Main(string[] args) {

      logger = new Logger();
      logger.OnNewLogEvent += AddToLog;

      if (args.Length == 0 || args.Any(a => a.ToLower() == "-help" || a.ToLower() == "/?")) {
        PrintHelp();
        Environment.Exit(CodeOk);
      }

      logger.Log($"{Updater.ApplicationTitle} {typeof(Program).Assembly.GetName().Version.ToString(3)} {(Environment.Is64BitProcess ? "x64" : "x32")}", Logger.StateKind.Info);

      if (!OSHelper.IsCompatible(true, out var errorMessage, out var fixAction)) {
        logger.Log(errorMessage, Logger.StateKind.Error);
        Environment.Exit(0);
      }

      if (WinApiHelper.CheckRunningInstances(true, true)) {
        logger.Log($"{Updater.ApplicationName} is already running.", Logger.StateKind.Error);
        Environment.Exit(0);
      }

      StartConsole(args);

      Environment.Exit(result);
    }

    private static void PrintHelp() {
      Console.WriteLine("rdpWrapper CLI Usage:");
      Console.WriteLine("  -install [TermWrap|RdpWrap] \t Install wrapper.");
      Console.WriteLine("  -uninstall                  \t Uninstall wrapper.");
      Console.WriteLine("  -start                      \t Start TermService.");
      Console.WriteLine("  -stop                       \t Stop TermService.");
      Console.WriteLine("  -restart                    \t Restart TermService.");
      Console.WriteLine("  -status                     \t Show current configuration and status.");
      Console.WriteLine("  -generate                   \t Generate rdpwrap.ini.");
      Console.WriteLine("  -port <number>              \t Set RDP port and update firewall.");
      Console.WriteLine("  -singlesession <1|0>        \t Toggle Single Session Per User.");
      Console.WriteLine("  -maxconn <number>           \t Set Maximum Connections Allowed.");
      Console.WriteLine("  -allowts <1|0>              \t Toggle Allow TS Connections.");
      Console.WriteLine("  -nla <0|1|2>                \t Set NLA (0: GUI Auth Only, 1: Default, 2: NLA).");
      Console.WriteLine("  -shadow <0-4>               \t Set Shadow Options (0: Disable, 1: Full w/perm, 2: Full w/o perm, 3: View w/perm, 4: View w/o perm).");
      Console.WriteLine("  -adduser <user> <pass>      \t Create local user & add to Remote Desktop Users.");
      Console.WriteLine("  -fixmsuser <user>           \t Fix MS user account.");
      Console.WriteLine("  -honorlegacy <1|0>          \t Toggle Honor Legacy Settings.");
      Console.WriteLine("  -dontdisplaylastuser <1|0>  \t Toggle Don't Display Last User.");
      Console.WriteLine("  -disablesecuritywarning <1|0>\t Toggle Disable Security Warning.");
      Console.WriteLine("  -restrictusb <1|0>          \t Toggle Restrict Client USB Redirection.");
      Console.WriteLine("  -allowplayback <1|0>        \t Toggle Allow Host Playback Redirect.");
      Console.WriteLine("  -allowaudiocapture <1|0>    \t Toggle Allow Client Audio Capture.");
      Console.WriteLine("  -allowvideocapture <1|0>    \t Toggle Allow Client Video Capture.");
      Console.WriteLine("  -allowpnp <1|0>             \t Toggle Allow PNP Redirect.");
      Console.WriteLine("  -help, /?                   \t Show this help.");
    }

    private static void StartConsole(string[] args) {
      try {
        var wrapper = new Wrapper(logger);

        // Preprocess args to support key=value format
        var parsedArgs = new System.Collections.Generic.List<string>();
        foreach (var a in args) {
          if (a.StartsWith("-") && a.Contains("=")) {
            var parts = a.Split(new[] { '=' }, 2);
            parsedArgs.Add(parts[0]);
            parsedArgs.Add(parts[1]);
          } else {
            parsedArgs.Add(a);
          }
        }
        args = parsedArgs.ToArray();

        for (int i = 0; i < args.Length; i++) {
          var arg = args[i].ToLower();
          switch (arg) {
            case "-install": {
              SupportedWrappers preferredWrapper = SupportedWrappers.TermWrap;
              if (i + 1 < args.Length && Enum.TryParse(args[i + 1], true, out SupportedWrappers parsed)) {
                preferredWrapper = parsed;
                i++;
              }
              wrapper.Install(preferredWrapper, true);
              break;
            }
            case "-uninstall":
              wrapper.Uninstall();
              break;
            case "-start":
              wrapper.StartService(TimeSpan.FromSeconds(10));
              break;
            case "-stop":
              wrapper.StopService(TimeSpan.FromSeconds(10));
              break;
            case "-restart":
              wrapper.StopService(TimeSpan.FromSeconds(10));
              wrapper.StartService(TimeSpan.FromSeconds(10));
              break;
            case "-generate":
#if !LITEVERSION
              var wrapperIniPath = Path.Combine(wrapper.WrapperFolderPath, Wrapper.RdpWrapIniName);
              wrapper.GenerateIniFile(wrapperIniPath);
#else
              logger.Log("No need for Ini file with TermWrap.");
#endif
              break;
            case "-status":
              PrintStatus(wrapper);
              break;
            case "-port":
              if (i + 1 < args.Length && int.TryParse(args[++i], out int port)) {
                wrapper.RdpPort = port;
                wrapper.SetFirewallPort(port);
              }
              break;
            case "-maxconn":
              if (i + 1 < args.Length && int.TryParse(args[++i], out int maxConn))
                wrapper.MaximumConnectionsAllowed = maxConn;
              break;
            case "-singlesession":
              if (i + 1 < args.Length) wrapper.SingleSessionPerUser = args[++i] == "1";
              break;
            case "-allowts":
              if (i + 1 < args.Length) wrapper.AllowTsConnections = args[++i] == "1";
              break;
            case "-honorlegacy":
              if (i + 1 < args.Length) wrapper.HonorLegacy = args[++i] == "1";
              break;
            case "-dontdisplaylastuser":
              if (i + 1 < args.Length) wrapper.DontDisplayLastUser = args[++i] == "1";
              break;
            case "-disablesecuritywarning":
              if (i + 1 < args.Length) wrapper.DisableSecurityWarning = args[++i] == "1";
              break;
            case "-restrictusb":
              if (i + 1 < args.Length) wrapper.RestrictUsbRedirection = args[++i] == "1";
              break;
            case "-allowplayback":
              if (i + 1 < args.Length) wrapper.AllowHostPlaybackRedirect = args[++i] == "1";
              break;
            case "-allowaudiocapture":
              if (i + 1 < args.Length) wrapper.AllowClientAudioCapture = args[++i] == "1";
              break;
            case "-allowvideocapture":
              if (i + 1 < args.Length) wrapper.AllowClientVideoCapture = args[++i] == "1";
              break;
            case "-allowpnp":
              if (i + 1 < args.Length) wrapper.AllowPnpRedirect = args[++i] == "1";
              break;
            case "-nla":
              if (i + 1 < args.Length && int.TryParse(args[++i], out int nla)) {
                if (nla == 0) { wrapper.UserAuthentication = 0; wrapper.SecurityLayer = 0; }
                else if (nla == 1) { wrapper.UserAuthentication = 0; wrapper.SecurityLayer = 1; }
                else if (nla == 2) { wrapper.UserAuthentication = 1; wrapper.SecurityLayer = 2; }
              }
              break;
            case "-shadow":
              if (i + 1 < args.Length && int.TryParse(args[++i], out int shadow))
                wrapper.ShadowOptions = shadow;
              break;
            case "-adduser":
              if (i + 2 < args.Length) {
                var user = args[++i];
                var pass = args[++i];
                using (var usersManager = new LocalUsersManager(logger)) {
                  var u = usersManager.CreateUserIfNotExist(user);
                  usersManager.SetUserPassword(u, pass);
                  usersManager.EnsureUserInRemoteDesktopUsers(u);
                }
              }
              else {
                logger.Log("Usage: -adduser <username> <password>", Logger.StateKind.Error);
              }
              break;
            case "-fixmsuser":
              if (i + 1 < args.Length) {
                var user = args[++i];
                var process = Process.Start("runAs", $"/u:{user} \"{Updater.CurrentFileLocation}\"");
                if (process != null) {
                  process.WaitForExit(10000);
                  if (process.ExitCode == 0) {
                    logger.Log("MS User fixed successfully.", Logger.StateKind.Info);
                  }
                }
              }
              break;
          }
        }
      }
      catch (Exception ex) {
        logger.Log("Error: " + ex.Message, Logger.StateKind.Error);
        result = CodeException;
      }
      finally {
        logger?.Dispose();
      }
    }

    private static void PrintStatus(Wrapper wrapper) {
      logger.Log("--- Status ---", Logger.StateKind.Info, true);
      logger.Log($"Wrapper State: {wrapper.CheckWrapperInstalled()}", Logger.StateKind.Info, true);
      logger.Log($"Service State: {wrapper.GetServiceState()}", Logger.StateKind.Info, true);
      logger.Log($"Listener State: {(WinStationHelper.IsListenerWorking() ? "Listening" : "Not Listening")}", Logger.StateKind.Info, true);
      logger.Log($"RDP Port: {wrapper.RdpPort}", Logger.StateKind.Info, true);
      logger.Log($"Max Connections: {wrapper.MaximumConnectionsAllowed}", Logger.StateKind.Info, true);
      logger.Log($"Single Session Per User: {wrapper.SingleSessionPerUser}", Logger.StateKind.Info, true);
      logger.Log($"Allow TS Connections: {wrapper.AllowTsConnections}", Logger.StateKind.Info, true);
      logger.Log($"NLA (UserAuth/Security): {wrapper.UserAuthentication}/{wrapper.SecurityLayer}", Logger.StateKind.Info, true);
      logger.Log($"Shadow Options: {wrapper.ShadowOptions}", Logger.StateKind.Info, true);
      logger.Log($"Honor Legacy Settings: {wrapper.HonorLegacy}", Logger.StateKind.Info, true);
      logger.Log($"Dont Display Last User: {wrapper.DontDisplayLastUser}", Logger.StateKind.Info, true);
      logger.Log($"Disable Security Warning: {wrapper.DisableSecurityWarning}", Logger.StateKind.Info, true);
      logger.Log($"Restrict USB Redirection: {wrapper.RestrictUsbRedirection}", Logger.StateKind.Info, true);
      logger.Log($"Allow Host Playback: {wrapper.AllowHostPlaybackRedirect}", Logger.StateKind.Info, true);
      logger.Log($"Allow Audio Capture: {wrapper.AllowClientAudioCapture}", Logger.StateKind.Info, true);
      logger.Log($"Allow Video Capture: {wrapper.AllowClientVideoCapture}", Logger.StateKind.Info, true);
      logger.Log($"Allow PNP Redirect: {wrapper.AllowPnpRedirect}", Logger.StateKind.Info, true);
    }

    private static void AddToLog(string message, Logger.StateKind state, bool newLine) {
      if (newLine) {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"\n{DateTime.Now:T} - ");
      }
      switch (state) {
        case Logger.StateKind.Error: Console.ForegroundColor = ConsoleColor.Red; break;
        case Logger.StateKind.Info: Console.ForegroundColor = ConsoleColor.White; break;
        default: Console.ForegroundColor = ConsoleColor.Gray; break;
      }
      Console.Write(message);
      Console.ResetColor();
    }
  }
}
