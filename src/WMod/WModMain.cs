using NeoModLoader.api;

namespace WMod;

public class WModMain : BasicMod<WModMain>
{
    protected override void OnModLoad()
    {
        LogInfo("WMod loaded — multiplayer mod scaffold v0.0.1");
    }
}
