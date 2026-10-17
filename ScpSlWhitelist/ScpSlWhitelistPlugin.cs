using System.Diagnostics;
using System.IO;
using System.Reflection;

using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;

namespace ScpSlWhitelist;

public class ScpSlWhitelistPlugin : Plugin<Config>
{
    private const string TokenEnvVar = "SCP_SL_DISCORD_TOKEN";
    private const string LinuxBotResourceName = "bot/linux/ScpSlWhitelist.Bot";
    private const string WindowsBotResourceName = "bot/win/ScpSlWhitelist.Bot.exe";

    private Process? _botProcess;
    private WhitelistMonitor? _monitor;
    private SynchronizationContext? _mainThreadContext;

    public override string Name => "ScpSlWhitelist";
    public override string Author => "jeweleyed";
    public override string Description => "Restricts server access to a whitelist of players.";
    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(1, 1, 7);

    public override void Enable()
    {
        _mainThreadContext = SynchronizationContext.Current;

        PlayerEvents.PreAuthenticating += OnPreAuthenticating;
        PlayerEvents.Joined += OnPlayerJoined;

        var whitelistPath = Whitelist.GetFilePath(GetPluginDirectory());
        Logger.Info($"Whitelist enabled: {Config.WhitelistEnabled}, deny_when_empty: {Config.DenyWhenEmpty}, file: {whitelistPath}");

        StartDiscordBot();

        if (Config.WhitelistEnabled)
        {
            _monitor = new WhitelistMonitor(whitelistPath, ScheduleKickDrain);
            var count = _monitor.Current.Count;
            if (count == 0 && !Config.DenyWhenEmpty)
                Logger.Warn("Whitelist is empty: everyone can connect until entries are added (set deny_when_empty to true for strict mode).");
            else
                Logger.Info($"Whitelist active with {count} entr{(count == 1 ? "y" : "ies")}.");
        }
        else
        {
            Logger.Warn("Whitelist is disabled in the config; connections will not be checked.");
        }
    }

    public override void Disable()
    {
        PlayerEvents.PreAuthenticating -= OnPreAuthenticating;
        PlayerEvents.Joined -= OnPlayerJoined;
        _monitor?.Dispose();
        _monitor = null;
        StopDiscordBot();
    }

    private void OnPreAuthenticating(PlayerPreAuthenticatingEventArgs e)
    {
        if (!Config.WhitelistEnabled)
            return;

        var userId = e.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            Logger.Warn("Pre-auth event fired with an empty UserId; skipping the whitelist check.");
            return;
        }

        DrainPendingKicks();

        var whitelist = _monitor?.Current ?? Whitelist.Load(Whitelist.GetFilePath(GetPluginDirectory()));
        if (whitelist.Count == 0 && !Config.DenyWhenEmpty)
        {
            Logger.Info($"Allowed {userId}: whitelist is empty and deny_when_empty is false.");
            return;
        }

        if (whitelist.Contains(Whitelist.Normalize(userId)))
        {
            Logger.Info($"Allowed whitelisted connection: {userId}");
            return;
        }

        e.RejectCustom(Config.RejectMessage);
        Logger.Info($"Kicked non-whitelisted connection: {userId}");
    }

    private void OnPlayerJoined(PlayerJoinedEventArgs e)
    {
        Logger.Info($"Player joined: {e.Player.Nickname} ({e.Player.UserId})");
        DrainPendingKicks();
    }

    private void ScheduleKickDrain()
    {
        var context = _mainThreadContext;
        if (context is not null)
            context.Post(_ => DrainPendingKicks(), null);
        else
            Logger.Warn("No SynchronizationContext available; pending kicks will be drained on the next pre-auth/join event instead.");
    }

    private void DrainPendingKicks()
    {
        if (_monitor is null)
            return;

        while (_monitor.PendingKicks.TryDequeue(out var steamId))
        {
            foreach (var player in Player.List)
            {
                if (string.IsNullOrEmpty(player.UserId))
                    continue;

                if (!string.Equals(Whitelist.Normalize(player.UserId), steamId, StringComparison.Ordinal))
                    continue;

                player.Kick(Config.RejectMessage);
                Logger.Info($"Kicked {player.Nickname} ({player.UserId}): removed from the whitelist.");
            }
        }
    }

    private void StartDiscordBot()
    {
        if (string.IsNullOrWhiteSpace(Config.DiscordToken))
        {
            Logger.Warn("DiscordToken is not set in the config, the Discord bot will not be started.");
            return;
        }

        string botPath;
        try
        {
            botPath = ExtractBot();
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to extract the embedded Discord bot: {ex.Message}");
            return;
        }

        if (Config.Debug)
            Logger.Info($"Extracted Discord bot to: {botPath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = botPath,
            WorkingDirectory = Path.GetDirectoryName(botPath) ?? ".",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.Environment[TokenEnvVar] = Config.DiscordToken;
        startInfo.Environment[Whitelist.EnvVar] = Whitelist.GetFilePath(GetPluginDirectory());
        startInfo.Environment["SCP_SL_WEBHOOK_URL"] = Config.LogWebhookUrl;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Logger.Info($"[DiscordBot] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Logger.Error($"[DiscordBot] {e.Data}");
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _botProcess = process;
            Logger.Info($"Started Discord bot process (PID {process.Id}).");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Failed to start the Discord bot: {ex.Message}. " +
                "If the temp directory is mounted noexec, point the TMPDIR environment variable at an exec-mounted directory and restart the server.");
            process.Dispose();
        }
    }

    private string GetPluginDirectory()
    {
        var pluginPath = FilePath;
        if (string.IsNullOrEmpty(pluginPath))
            pluginPath = Assembly.GetExecutingAssembly().Location;

        var pluginDir = Path.GetDirectoryName(pluginPath);
        return string.IsNullOrEmpty(pluginDir) ? "." : pluginDir;
    }

    private static bool IsWindows =>
        Path.DirectorySeparatorChar == '\\' || Environment.OSVersion.Platform == PlatformID.Win32NT;

    private string ExtractBot()
    {
        var resourceName = IsWindows ? WindowsBotResourceName : LinuxBotResourceName;
        var assembly = Assembly.GetExecutingAssembly();
        using var input = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded bot executable '{resourceName}' is missing from the plugin DLL.");

        var botDirectory = Path.Combine(Path.GetTempPath(), "ScpSlWhitelistBot");
        Directory.CreateDirectory(botDirectory);

        var botPath = Path.Combine(botDirectory, "ScpSlWhitelistBot" + (IsWindows ? ".exe" : string.Empty));
        using (var output = File.Create(botPath))
            input.CopyTo(output);

        if (!IsWindows)
            MakeExecutable(botPath);

        return botPath;
    }

    private static void MakeExecutable(string path)
    {
        using var chmod = Process.Start(new ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        chmod?.WaitForExit(5000);
    }

    private void StopDiscordBot()
    {
        if (_botProcess is null || _botProcess.HasExited)
        {
            _botProcess?.Dispose();
            _botProcess = null;
            return;
        }

        try
        {
            _botProcess.Kill();
            _botProcess.WaitForExit(5000);
            Logger.Info("Stopped the Discord bot process.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to stop the Discord bot: {ex.Message}");
        }
        finally
        {
            _botProcess.Dispose();
            _botProcess = null;
        }
    }
}
