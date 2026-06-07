using System;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;
using WMod.Net;
using WMod.Sync;

namespace WMod;

public class WModMain : BasicMod<WModMain>
{
    private int _pingSeq;
    private string _toast;
    private float _toastUntil;
    private GUIStyle _boxStyle;
    private GUIStyle _labelStyle;

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

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_toast) || Time.unscaledTime > _toastUntil) return;
        EnsureStyles();
        var rect = new Rect(20, 20, 720, 36);
        GUI.Box(rect, GUIContent.none, _boxStyle);
        GUI.Label(rect, _toast, _labelStyle);
    }

    private void EnsureStyles()
    {
        if (_boxStyle != null) return;
        var bg = new Texture2D(1, 1);
        bg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.78f));
        bg.Apply();
        _boxStyle = new GUIStyle { normal = { background = bg } };
        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(12, 12, 4, 4),
        };
        _labelStyle.normal.textColor = Color.white;
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
            Toast($"[Numpad 1] Host started on :{NetworkManager.DefaultPort}");
        }
        catch (Exception ex) { Toast($"[Numpad 1] Host failed: {ex.Message}"); }
    }

    private void DoJoin()
    {
        try
        {
            NetworkManager.ConnectClient("127.0.0.1");
            Toast($"[Numpad 2] Connected as client -> 127.0.0.1:{NetworkManager.DefaultPort}");
        }
        catch (Exception ex) { Toast($"[Numpad 2] Connect failed: {ex.Message}"); }
    }

    private void DoSendTest()
    {
        _pingSeq++;
        var msg = NetMessage.Create("PING", _pingSeq.ToString());
        NetworkManager.Send(msg);
        Toast($"[Numpad 3] sent PING #{_pingSeq}");
    }

    private void DoStatus() => Toast($"[Numpad 0] {NetworkManager.StatusLine()}");

    private void DoDisconnect()
    {
        NetworkManager.Shutdown();
        Toast("[Numpad -] disconnected");
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
        }
    }
}
