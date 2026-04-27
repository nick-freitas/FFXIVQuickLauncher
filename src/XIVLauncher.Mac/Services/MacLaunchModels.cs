using System;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
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

public enum MacLaunchStage
{
    Preparing,
    CheckingPatches,
    LoggingIn,
    Patching,
    Launching,
}

public sealed record MacLaunchProgress(
    MacLaunchStage Stage,
    string Message,
    double? PercentComplete = null,
    TimeSpan? EstimatedRemaining = null);

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

    public static MacLaunchResult NoService(Launcher.OauthLoginResult? oauthLogin = null, bool isFreeTrial = false, bool isSteam = false)
    {
        var message = "This account does not have an active playable service account.";

        if (oauthLogin is not null)
        {
            message += $" Square Enix response: playable={oauthLogin.Playable}, termsAccepted={oauthLogin.TermsAccepted}, region={oauthLogin.Region}, maxExpansion={oauthLogin.MaxExpansion}.";
            message += $" Login flags: freeTrial={isFreeTrial}, steam={isSteam}.";
        }

        return new(MacLaunchResultKind.NoService, message);
    }

    public static MacLaunchResult NoTerms()
        => new(MacLaunchResultKind.NoTerms, "Terms must be accepted in the official launcher before continuing.");

    public static MacLaunchResult Failed(string message)
        => new(MacLaunchResultKind.Failed, message);
}
