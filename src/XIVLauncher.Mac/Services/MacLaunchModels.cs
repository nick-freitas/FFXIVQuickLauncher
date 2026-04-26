using XIVLauncher.Common;
using XIVLauncher.Common.Game.OfficialMacApp;

namespace XIVLauncher.Mac.Services;

public sealed record MacLaunchRequest(
    OfficialMacAppInstall Install,
    string Username,
    string Password,
    string Otp,
    ClientLanguage Language,
    bool IsSteam,
    bool IsFreeTrial);

public enum MacLaunchResultKind
{
    Launched,
    PatchingRequired,
    NoService,
    NoTerms,
    Failed,
}

public sealed class MacLaunchResult
{
    private MacLaunchResult(MacLaunchResultKind kind, string message, int pendingPatchCount = 0)
    {
        this.Kind = kind;
        this.Message = message;
        this.PendingPatchCount = pendingPatchCount;
    }

    public MacLaunchResultKind Kind { get; }

    public string Message { get; }

    public int PendingPatchCount { get; }

    public static MacLaunchResult Launched()
        => new(MacLaunchResultKind.Launched, "Game launch started.");

    public static MacLaunchResult PatchingRequired(int patchCount, string patchType)
        => new(
            MacLaunchResultKind.PatchingRequired,
            $"{patchType} patching required. {patchCount} patch{(patchCount == 1 ? string.Empty : "es")} pending.",
            patchCount);

    public static MacLaunchResult NoService()
        => new(MacLaunchResultKind.NoService, "This account does not have an active playable service account.");

    public static MacLaunchResult NoTerms()
        => new(MacLaunchResultKind.NoTerms, "Terms must be accepted in the official launcher before continuing.");

    public static MacLaunchResult Failed(string message)
        => new(MacLaunchResultKind.Failed, message);
}
