using System;

namespace WMod;

public static class WModBridge
{
    public static Action<string> OnToast;
    public static void Toast(string text) => OnToast?.Invoke(text);
}
