using ArctisNovaTray.Models;

namespace ArctisNovaTray.Rendering;

/// <summary>
/// Caches rendered Win32 Icons per (HeadsetBaseVisual, BluetoothBadgeVisual, SizePx)
/// so periodic tray updates allocate zero new GDI/HICON handles.
/// </summary>
public sealed class TrayIconCache : IDisposable
{
    private readonly Dictionary<(TrayVisualKey Key, int SizePx), Icon> _cache = [];
    private bool _disposed;

    public int CachedCount => _cache.Count;

    public Icon GetIcon(TrayVisualKey key, int sizePx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int normalizedSize = Math.Clamp(sizePx, 16, 256);
        var cacheKey = (key, normalizedSize);

        if (_cache.TryGetValue(cacheKey, out Icon? existing))
        {
            return existing;
        }

        Icon created = TrayIconRenderer.CreateOwnedIcon(key, normalizedSize);
        _cache[cacheKey] = created;
        return created;
    }

    public static int GetSystemTrayIconSize()
    {
        Size small = SystemInformation.SmallIconSize;
        int raw = Math.Max(small.Width, small.Height);
        // Render at >=32px when high-DPI or standard 16/20/24/32 small icon size is active.
        return raw >= 16 ? raw : 32;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Icon icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
    }
}
