using System.Threading;
using System.Threading.Tasks;

namespace XIVLauncher.Mac.ViewModels;

public interface IClipboardService
{
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public sealed class NullClipboardService : IClipboardService
{
    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
