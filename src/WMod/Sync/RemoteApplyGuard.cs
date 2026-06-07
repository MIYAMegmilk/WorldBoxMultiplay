using System;

namespace WMod.Sync;

internal static class RemoteApplyGuard
{
    [ThreadStatic] private static bool _applying;

    public static bool IsApplyingRemote => _applying;

    public static IDisposable Scope()
    {
        _applying = true;
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        public void Dispose() => _applying = false;
    }
}
