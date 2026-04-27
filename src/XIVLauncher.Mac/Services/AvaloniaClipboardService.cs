using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using XIVLauncher.Mac.ViewModels;

namespace XIVLauncher.Mac.Services;

public sealed class AvaloniaClipboardService : IClipboardService
{
    private readonly Func<IClipboard?> getClipboard;

    public AvaloniaClipboardService(Func<IClipboard?> getClipboard)
    {
        this.getClipboard = getClipboard;
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var clipboard = this.getClipboard();
        if (clipboard is null)
            return;

        await clipboard.SetTextAsync(text);
    }
}
