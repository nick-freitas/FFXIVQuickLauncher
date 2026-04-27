using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacAppDalamudRunner : IDalamudRunner
{
    private readonly OfficialMacAppInstall install;
    private readonly DirectoryInfo? dotnetRuntime;

    public OfficialMacAppDalamudRunner(OfficialMacAppInstall install)
        : this(install, dotnetRuntime: null)
    {
    }

    public OfficialMacAppDalamudRunner(OfficialMacAppInstall install, DirectoryInfo? dotnetRuntime)
    {
        this.install = install;
        this.dotnetRuntime = dotnetRuntime;
    }

    public Process? Run(
        FileInfo runner,
        bool fakeLogin,
        bool noPlugins,
        bool noThirdPlugins,
        FileInfo gameExe,
        string gameArgs,
        IDictionary<string, string> environment,
        DalamudLoadMethod loadMethod,
        DalamudStartInfo dalamudStartInfo)
    {
        var plan = BuildLaunchPlan(
            this.install,
            runner,
            fakeLogin,
            noPlugins,
            noThirdPlugins,
            gameExe,
            gameArgs,
            environment,
            loadMethod,
            dalamudStartInfo,
            this.dotnetRuntime);

        var startInfo = new ProcessStartInfo(plan.FileName)
        {
            Arguments = plan.Arguments,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var pair in plan.Environment)
        {
            startInfo.EnvironmentVariables[pair.Key] = pair.Value;
        }

        try
        {
            var dalamudProcess = Process.Start(startInfo)
                ?? throw new DalamudRunnerException("Could not start Dalamud injector.");

            StartLogThread(dalamudProcess.StandardError, "DALAMUD STDERR");

            var dalamudOutput = dalamudProcess.StandardOutput.ReadLine();
            if (string.IsNullOrWhiteSpace(dalamudOutput))
                throw new DalamudRunnerException("Injector output stream was empty");

            Log.Information("[DALAMUD] {Log}", dalamudOutput);
            ParseDalamudConsoleOutput(dalamudOutput);

            StartLogThread(dalamudProcess.StandardOutput, "DALAMUD");

            return dalamudProcess;
        }
        catch (DalamudRunnerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DalamudRunnerException("Error trying to start Dalamud.", ex);
        }
    }

    public static OfficialMacDalamudLaunchPlan BuildLaunchPlan(
        OfficialMacAppInstall install,
        FileInfo runner,
        bool fakeLogin,
        bool noPlugins,
        bool noThirdPlugins,
        FileInfo gameExe,
        string gameArgs,
        IDictionary<string, string> environment,
        DalamudLoadMethod loadMethod,
        DalamudStartInfo startInfo,
        DirectoryInfo? dotnetRuntime = null)
    {
        var pathConverter = new OfficialMacWinePathConverter(install.WinePrefix);
        var launchEnvironment = new Dictionary<string, string>(environment)
        {
            ["WINEPREFIX"] = install.WinePrefix.FullName,
        };

        var dalamudRuntime = environment.TryGetValue("DALAMUD_RUNTIME", out var environmentRuntime)
            ? environmentRuntime
            : dotnetRuntime?.FullName;

        if (!string.IsNullOrWhiteSpace(dalamudRuntime))
        {
            var wineRuntime = pathConverter.ToWinePath(dalamudRuntime);
            launchEnvironment["DALAMUD_RUNTIME"] = wineRuntime;
            launchEnvironment["DOTNET_ROOT"] = wineRuntime;
            launchEnvironment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        }

        var runnerPath = pathConverter.ToWinePath(runner.FullName);
        var gameExePath = pathConverter.ToWinePath(gameExe.FullName);
        var workingDirectory = pathConverter.ToWinePath(startInfo.WorkingDirectory);
        var configurationPath = pathConverter.ToWinePath(startInfo.ConfigurationPath);
        var loggingPath = pathConverter.ToWinePath(startInfo.LoggingPath);
        var pluginDirectory = pathConverter.ToWinePath(startInfo.PluginDirectory);
        var assetDirectory = pathConverter.ToWinePath(startInfo.AssetDirectory);

        var launchArguments = new List<string>
        {
            $"\"{runnerPath}\"",
            DalamudInjectorArgs.LAUNCH,
            DalamudInjectorArgs.Mode(loadMethod == DalamudLoadMethod.EntryPoint ? "entrypoint" : "inject"),
            DalamudInjectorArgs.Game(gameExePath),
            DalamudInjectorArgs.WorkingDirectory(workingDirectory),
            DalamudInjectorArgs.ConfigurationPath(configurationPath),
            DalamudInjectorArgs.LoggingPath(loggingPath),
            DalamudInjectorArgs.PluginDirectory(pluginDirectory),
            DalamudInjectorArgs.AssetDirectory(assetDirectory),
            DalamudInjectorArgs.ClientLanguage((int)startInfo.Language),
            DalamudInjectorArgs.DelayInitialize(startInfo.DelayInitializeMs),
            DalamudInjectorArgs.TsPackB64(Convert.ToBase64String(Encoding.UTF8.GetBytes(startInfo.TroubleshootingPackData))),
        };

        if (loadMethod == DalamudLoadMethod.ACLonly)
            launchArguments.Add(DalamudInjectorArgs.WITHOUT_DALAMUD);

        if (fakeLogin)
            launchArguments.Add(DalamudInjectorArgs.FAKE_ARGUMENTS);

        if (noPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_PLUGIN);

        if (noThirdPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_THIRD_PARTY);

        launchArguments.Add("--");
        launchArguments.Add(gameArgs);

        return new OfficialMacDalamudLaunchPlan(
            install.WineExecutable.FullName,
            runner.DirectoryName ?? install.AppBundle.FullName,
            string.Join(" ", launchArguments),
            launchEnvironment);
    }

    private static void ParseDalamudConsoleOutput(string dalamudOutput)
    {
        try
        {
            var consoleOutput = JsonConvert.DeserializeObject<DalamudConsoleOutput>(dalamudOutput);
            if (consoleOutput == null)
                throw new DalamudRunnerException("Deserialized Dalamud output was null.");

            Log.Verbose(
                "Got Dalamud injector output with Wine pid {WinePid} and handle {Handle}",
                consoleOutput.Pid,
                consoleOutput.Handle);
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Couldn't parse Dalamud output: {DalamudOutput}", dalamudOutput);
            throw new DalamudRunnerException("Couldn't parse Dalamud output.", ex);
        }
    }

    private static void StartLogThread(StreamReader reader, string logPrefix)
    {
        var thread = new Thread(() =>
        {
            try
            {
                while (!reader.EndOfStream)
                {
                    var logOutput = reader.ReadLine();
                    if (logOutput != null)
                        Log.Information("[{LogPrefix}] {Log}", logPrefix, logOutput);
                }
            }
            catch (IOException ex)
            {
                Log.Debug(ex, "Stopped reading {LogPrefix} output.", logPrefix);
            }
            catch (ObjectDisposedException ex)
            {
                Log.Debug(ex, "Stopped reading {LogPrefix} output.", logPrefix);
            }
        })
        {
            IsBackground = true,
            Name = $"OfficialMacAppDalamudRunner {logPrefix}",
        };

        thread.Start();
    }
}

public sealed record OfficialMacDalamudLaunchPlan(
    string FileName,
    string WorkingDirectory,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);
