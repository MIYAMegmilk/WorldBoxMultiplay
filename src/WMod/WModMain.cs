using System;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;
using WMod.Net;
using WMod.Rules;
using WMod.Sync;

namespace WMod;

public class WModMain : BasicMod<WModMain>
{
    private int _pingSeq;
    private string _toast;
    private float _toastUntil;
    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _manaFillStyle;

    protected override void OnModLoad()
    {
        LogInfo("WMod loaded — multiplayer mod scaffold v0.0.1");
        NetworkManager.OnMessage += HandleNetMessage;
        WModBridge.OnToast = Toast;

        var harmony = new Harmony("io.github.miyamegmilk.wmod");
        harmony.PatchAll();
        LogInfo("[WMod] Harmony patches applied");

        AutoTest.Init();
    }

    private void Update()
    {
        FixedTick.TickIfEnabled();
        NetworkManager.DrainInbox();
        ManaSystem.Tick(Time.unscaledDeltaTime);
        MatchRunner.Tick(Time.unscaledTime);
        WorldSnapshotSync.TickAuto(Time.unscaledTime);
        WorldSnapshotSync.TickHideMaintenance(Time.unscaledTime);
        ClientSimMode.Tick();
        UnitPositionSync.TickHost(Time.unscaledTime);
        AutoTest.Tick(Time.unscaledTime);

        if (Input.GetKeyDown(KeyCode.Keypad1)) DoHost();
        else if (Input.GetKeyDown(KeyCode.Keypad2)) DoJoin();
        else if (Input.GetKeyDown(KeyCode.Keypad3)) DoSendTest();
        else if (Input.GetKeyDown(KeyCode.Keypad0)) DoStatus();
        else if (Input.GetKeyDown(KeyCode.KeypadMinus)) DoDisconnect();
        else if (Input.GetKeyDown(KeyCode.K)) DoClaim();
        else if (Input.GetKeyDown(KeyCode.L)) DoRoster();
        else if (Input.GetKeyDown(KeyCode.G)) DoGenerateWorld();
        else if (Input.GetKeyDown(KeyCode.KeypadPeriod)) DoSendSnapshot();
        else if (Input.GetKeyDown(KeyCode.D)) DoDumpLocal();
        else if (Input.GetKeyDown(KeyCode.KeypadPlus)) WorldSnapshotSync.CycleAutoInterval();
        else if (Input.GetKeyDown(KeyCode.KeypadMultiply)) ParallelLockstep.Toggle();
        else if (Input.GetKeyDown(KeyCode.KeypadDivide)) FixedTick.Toggle();
    }

    private int _dumpCounter;

    private void DoDumpLocal()
    {
        try
        {
            if (MapBox.instance == null) { Toast("[D] MapBox not ready"); return; }
            var bytes = SaveManager.currentWorldToSavedMap().toZip();
            _dumpCounter++;
            string role;
            if ((NetworkManager.Role & NetRole.Host) != 0) role = "A";
            else if ((NetworkManager.Role & NetRole.Client) != 0) role = "B";
            else role = "X";
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"wmod_dump_{role}_{_dumpCounter:D3}.bin");
            System.IO.File.WriteAllBytes(path, bytes);
            Toast($"[D] dump #{_dumpCounter} ({role}) {bytes.Length:N0}B -> {System.IO.Path.GetFileName(path)}");
        }
        catch (System.Exception ex)
        {
            Toast($"[D] dump error: {ex.Message}");
        }
    }

    private void DoSendSnapshot()
    {
        if ((NetworkManager.Role & NetRole.Host) == 0)
        {
            Toast("[Numpad .] only host can send snapshot");
            return;
        }
        WorldSnapshotSync.HostSendSnapshot();
    }

    private void DoGenerateWorld()
    {
        if ((NetworkManager.Role & NetRole.Host) == 0)
        {
            Toast("[G] only host can trigger synced world gen");
            return;
        }
        WorldGenSync.HostStartNewWorld();
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawToast();
        if (PlayerRegistry.Self.id >= 0) DrawManaBar();
    }

    private void DrawToast()
    {
        if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;
        var rect = new Rect(20, 20, 720, 36);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.Label(rect, _toast, _labelStyle);
    }

    private void DrawManaBar()
    {
        var p = ManaSystem.Get(PlayerRegistry.Self.id);
        if (p == null) return;
        const int w = 260, h = 24;
        var rect = new Rect(20, Screen.height - 44, w, h);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        var fillW = (int)((w - 4) * Mathf.Clamp01(p.current / p.max));
        if (fillW > 0)
        {
            var fillRect = new Rect(rect.x + 2, rect.y + 2, fillW, h - 4);
            GUI.Box(fillRect, GUIContent.none, _manaFillStyle);
        }
        GUI.Label(rect, $"  Mana {p.current:0}/{p.max:0}", _labelStyle);
    }

    private void EnsureStyles()
    {
        if (_boxStyle != null) return;
        _boxStyle = new GUIStyle { normal = { background = MakeTex(new Color(0f, 0f, 0f, 0.78f)) } };
        _manaFillStyle = new GUIStyle { normal = { background = MakeTex(new Color(0.25f, 0.55f, 1.0f, 0.85f)) } };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(12, 12, 4, 4),
        };
        _labelStyle.normal.textColor = Color.white;
    }

    private static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastUntil = Time.unscaledTime + 2.5f;
        LogInfo($"[WMod] {text}");
    }

    private void DoHost()
    {
        try
        {
            NetworkManager.StartHost();
            PlayerRegistry.SetSelf(0, "Host");
            EnableLockstep();
            Toast($"[Numpad 1] Host on :{NetworkManager.DefaultPort} + lockstep mode (id 0)");
        }
        catch (Exception ex) { Toast($"[Numpad 1] Host failed: {ex.Message}"); }
    }

    private void DoJoin()
    {
        try
        {
            NetworkManager.ConnectClient("127.0.0.1");
            PlayerRegistry.SetSelf(1, "Client");
            ClientSimMode.Enable();
            Toast($"[Numpad 2] Connected -> 127.0.0.1:{NetworkManager.DefaultPort} + sim PAUSED (id 1)");
        }
        catch (Exception ex) { Toast($"[Numpad 2] Connect failed: {ex.Message}"); }
    }

    private static void EnableLockstep()
    {
        if (!ParallelLockstep.ForceSequential) ParallelLockstep.Toggle();
        if (!FixedTick.Enabled) FixedTick.Toggle();
    }

    private static void DisableLockstep()
    {
        if (ParallelLockstep.ForceSequential) ParallelLockstep.Toggle();
        if (FixedTick.Enabled) FixedTick.Toggle();
    }

    private void DoClaim()
    {
        var result = KingdomClaimHandler.TryClaimAtCursor();
        Toast($"[K] {result}");
    }

    private void DoRoster() => Toast($"[L] roster: {PlayerRegistry.RosterText()}");

    private void DoSendTest()
    {
        _pingSeq++;
        var msg = NetMessage.Create("PING", _pingSeq.ToString());
        NetworkManager.Send(msg);
        Toast($"[Numpad 3] sent PING #{_pingSeq}");
    }

    private void DoStatus() => Toast($"[Numpad 0] {NetworkManager.StatusLine()} | snap: {WorldSnapshotSync.StatusLine()}");

    private void DoDisconnect()
    {
        NetworkManager.Shutdown();
        PlayerRegistry.Reset();
        ManaSystem.Reset();
        MatchRunner.Reset();
        DisableLockstep();
        ClientSimMode.Disable();
        Toast("[Numpad -] disconnected + sim restored");
    }

    private void HandleNetMessage(NetMessage msg)
    {
        LogInfo($"[NET<-] {msg.Type} payload={msg.Payload} ts={msg.Timestamp}");

        switch (msg.Type)
        {
            case "PING":
                NetworkManager.Send(NetMessage.Create("PONG", msg.Payload));
                break;
            case "PONG":
                Toast($"PONG #{msg.Payload} round-trip ok");
                break;
            case "_SYS":
                Toast($"[NET] {msg.Payload}");
                break;
            case "CLICK":
                GodPowerSync.HandleRemoteClick(msg.Payload);
                break;
            case "CLAIM":
                KingdomClaimHandler.HandleRemoteClaim(msg.fromPlayerId, msg.Payload);
                Toast($"peer id={msg.fromPlayerId} claimed kingdom");
                break;
            case "MATCH_RESULT":
                MatchRunner.HandleRemote(msg.Payload);
                break;
            case "WORLD_GEN":
                WorldGenSync.HandleRemoteGen(msg.Payload);
                break;
            case "WORLD_SNAPSHOT":
                WorldSnapshotSync.HandleRemoteSnapshot(msg.Payload);
                break;
            case "UNIT_POS":
                UnitPositionSync.HandleRemote(msg.Payload);
                break;
            case "UNIT_SPAWN":
                UnitLifecycleSync.HandleRemoteSpawn(msg.Payload);
                break;
            case "UNIT_DEATH":
                UnitLifecycleSync.HandleRemoteDeath(msg.Payload);
                break;
            case "TILE_CHANGE":
                TileChangeSync.HandleRemote(msg.Payload);
                break;
        }
    }
}
