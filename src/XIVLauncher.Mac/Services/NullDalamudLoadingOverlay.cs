using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Mac.Services;

public sealed class NullDalamudLoadingOverlay : IDalamudLoadingOverlay
{
    public void SetStep(IDalamudLoadingOverlay.DalamudUpdateStep step)
    {
    }

    public void SetVisible()
    {
    }

    public void SetInvisible()
    {
    }

    public void ReportProgress(long? size, long downloaded, double? progress)
    {
    }
}
