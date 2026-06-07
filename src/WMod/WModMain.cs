using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;
using WMod.Net;
using WMod.Sync;

namespace WMod;

public class WModMain : BasicMod<WModMain>
{
    private int _pingSeq;

    protected override void OnModLoad()
    {
        LogInfo("WMod loaded — multiplayer mod scaffold v0.0.1");
        NetworkManager.OnMessage += HandleNetMessage;

        var harmony = new Harmony("io.github.miyamegmilk.wmod");
        harmony.PatchAll();
        LogInfo("[WMod] Harmony patches applied");
    }

    private void Update()
    {
        NetworkManager.DrainInbox();

        if (Input.GetKeyDown(KeyCode.Keypad1)) DoHost();
        else if (Input.GetKeyDown(KeyCode.Keypad2)) DoJoin();
        else if (Input.GetKeyDown(KeyCode.Keypad3)) DoSendTest();
        else if (Input.GetKeyDown(KeyCode.Keypad0)) DoStatus();
        else if (Input.GetKeyDown(KeyCode.KeypadMinus)) DoDisconnect();
    }

    private void DoHost()
    {
        try
        {
            NetworkManager.StartHost();
            LogInfo($"[NET] Hosting on port {NetworkManager.DefaultPort}");
        }
        catch (System.Exception ex) { LogInfo($"[NET] Host failed: {ex.Message}"); }
    }

    private void DoJoin()
    {
        try
        {
            NetworkManager.ConnectClient("127.0.0.1");
            LogInfo($"[NET] Connected as client -> 127.0.0.1:{NetworkManager.DefaultPort}");
        }
        catch (System.Exception ex) { LogInfo($"[NET] Connect failed: {ex.Message}"); }
    }

    private void DoSendTest()
    {
        _pingSeq++;
        var msg = NetMessage.Create("PING", _pingSeq.ToString());
        NetworkManager.Send(msg);
        LogInfo($"[NET] sent PING #{_pingSeq}");
    }

    private void DoStatus() => LogInfo($"[NET] status: {NetworkManager.StatusLine()}");

    private void DoDisconnect()
    {
        NetworkManager.Shutdown();
        LogInfo("[NET] disconnected");
    }

    private void HandleNetMessage(NetMessage msg)
    {
        LogInfo($"[NET<-] {msg.Type} payload={msg.Payload} ts={msg.Timestamp}");

        switch (msg.Type)
        {
            case "PING":
                NetworkManager.Send(NetMessage.Create("PONG", msg.Payload));
                break;
            case "CLICK":
                GodPowerSync.HandleRemoteClick(msg.Payload);
                break;
        }
    }
}
