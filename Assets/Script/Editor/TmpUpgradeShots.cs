using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

// TEMPORARY: walks every screen in play mode and saves a screenshot of each. Deleted after use.
[InitializeOnLoad]
public static class TmpUpgradeShots
{
    private const string RunningKey = "TmpUpgradeShots.Running";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
    private static string OutDir => SessionState.GetString("TmpUpgradeShots.Out", "C:/Temp");
    private static int _step, _ticks = -1;
    private static float _t0;
    private static bool _hooked;
    private static string _shot;
    private static readonly List<string> _log = new List<string>();
    private static RenderTexture _sceneRT, _uiRT;
    private static PanelSettings _panel;
    private static VisualElement _bg;

    static TmpUpgradeShots() { if (SessionState.GetBool(RunningKey, false)) EditorApplication.update += Tick; }

    public static void Run()
    {
        foreach (var k in new[] { "credits", "engine", "helm", "hull", "delivered", "voyages", "job", "atsea" }) PlayerPrefs.DeleteKey("career." + k);
        foreach (var m in new[] { "1-1", "1-2", "1-3", "2-1", "2-2", "2-3", "3-1", "3-2", "3-3" }) PlayerPrefs.DeleteKey("story.done." + m);
        PlayerPrefs.SetInt("story.done.1-1", 1);
        PlayerPrefs.SetInt("story.done.1-2", 1);
        PlayerPrefs.SetInt("tutorial_done", 1); PlayerPrefs.SetInt("career.delivered", 1); PlayerPrefs.SetInt("career.voyages", 9); PlayerPrefs.SetString("career.job", "EASY");
        PlayerPrefs.SetInt("career.credits", 1500);
        foreach (var kv in new (string, int)[] { ("skill.EngineOrders", 12), ("skill.HelmOrders", 9), ("skill.Questions", 4), ("skill.CrewTalk", 7), ("skill.CleanVoyages", 1), ("skill.ClearVoyages", 1), ("cert.engine2", 1), ("cert.rudder2", 1), ("cert.crew2", 1), ("headstart", 1) })
            PlayerPrefs.SetInt("progress." + kv.Item1, kv.Item2);
        PlayerPrefs.Save();
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity");
        SessionState.SetBool(RunningKey, true);
        SessionState.SetString("TmpUpgradeShots.Out", Environment.GetEnvironmentVariable("SHOT_DIR") ?? "C:/Temp");
        Directory.CreateDirectory(OutDir);
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Finish(string why)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(RunningKey);
        if (_panel != null) { _panel.targetTexture = null; _panel.clearColor = false; }
        foreach (var l in _log) Debug.Log("[UI] " + l);
        foreach (var k in new[] { "credits", "engine", "helm", "hull", "delivered", "voyages", "job", "atsea" }) PlayerPrefs.DeleteKey("career." + k);
        foreach (var m in new[] { "1-1", "1-2", "1-3" }) PlayerPrefs.DeleteKey("story.done." + m);
        Debug.Log("[UI] DONE " + why);
        EditorApplication.Exit(0);
    }

    private static void SetupCapture()
    {
        _panel = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
        _sceneRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _uiRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        _panel.targetTexture = _uiRT; _panel.clearColor = true; _panel.colorClearValue = Color.black;
        var go = new GameObject("TmpBg"); Object.DontDestroyOnLoad(go);
        var doc = go.AddComponent<UIDocument>(); doc.panelSettings = _panel; doc.sortingOrder = -1000;
        _bg = new VisualElement();
        foreach (var e in new[] { _bg, doc.rootVisualElement }) { e.style.position = Position.Absolute; e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0; }
        doc.rootVisualElement.Add(_bg);
    }

    private static bool Capture()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            var prevTarget = cam.targetTexture;
            cam.targetTexture = _sceneRT; cam.Render(); cam.targetTexture = prevTarget;
            _bg.style.backgroundImage = Background.FromRenderTexture(_sceneRT); _bg.MarkDirtyRepaint();
        }
        if (++_ticks < 4) return false;
        var prev = RenderTexture.active; RenderTexture.active = _uiRT;
        var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); RenderTexture.active = prev;
        var px = tex.GetPixels(); for (int i = 0; i < px.Length; i++) px[i] = px[i].linear; tex.SetPixels(px); tex.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, _shot + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        _log.Add("saved " + _shot);
        _ticks = -1;
        return true;
    }

    private static void Shot(string name) { _shot = name; _ticks = 0; }
    private static void Next() { _step++; _t0 = Time.realtimeSinceStartup; }
    private static float El => Time.realtimeSinceStartup - _t0;
    private static void Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Any).Invoke(o, args);
    private static ShipNavigator Nav => ShipNavigator.Instance;
    private static void Lens(float size)
    {
        var vc = Object.FindAnyObjectByType<Unity.Cinemachine.CinemachineCamera>();
        var lens = vc.Lens; lens.OrthographicSize = size; vc.Lens = lens;
    }

    private static void Place(Vector3 at, Vector3 forward)
    {
        Nav.transform.SetPositionAndRotation(new Vector3(at.x, Nav.transform.position.y, at.z), Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z)));
        Physics.SyncTransforms();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (!_hooked)
        {
            _hooked = true; _t0 = Time.realtimeSinceStartup; SetupCapture();
            Application.logMessageReceived += (m, st, t) => { if (t == LogType.Exception) _log.Add("EXCEPTION " + m + " | " + st.Split('\n')[0]); };
        }
        if (_ticks >= 0) { if (Capture()) Next(); return; }
        if (El > 60f) { Finish("timeout at step " + _step); return; }

        var yard = Object.FindAnyObjectByType<ShipyardManager>();
        var pilot = DockingPilot.Instance;
        var session = GameSession.Instance;
        switch (_step)
        {
            case 0: if (El < 3f) return; Shot("u00_menu_ship"); return;
            case 1: Call(Object.FindAnyObjectByType<MainMenuController>(), "OnPlayClicked"); Next(); return;
            case 2: if (El < 5f || yard == null) return; Call(yard, "ShowUpgrades", true); Next(); return;
            case 3: if (El < 0.8f) return; Shot("u01_upgrades"); return;
            case 4: session.TryBuy(Upgrade.Engine); Call(yard, "RefreshUI"); Next(); return;
            case 5: if (El < 0.8f) return; Shot("u02_after_fit"); return;
            case 6:
                foreach (var f in new[] { "_engineLevel", "_helmLevel", "_lookoutLevel", "_platingLevel", "_crewLevel" })
                    session.GetType().GetField(f, Any).SetValue(session, 3);
                Call(yard, "ShowUpgrades", false); Call(yard, "RefreshUI"); Next(); return;
            case 7: if (El < 1.5f) return; Call(yard, "OnStartMission"); Next(); return;
            case 8:
                if (El > 0.5f) yard.GetType().GetField("_skipLoading", Any).SetValue(yard, true);
                if (GameManager.Instance.CurrentState != GameState.Playing) return;
                { var sc = Object.FindAnyObjectByType<ShipCamera>(); if (sc != null) sc.enabled = false; }
                Place(new Vector3(0f, 0f, 200f), Vector3.forward); Lens(10f); Nav.SetGear(ShipEngine.TelegraphGear.Half); Next(); return;
            case 9: if (El < 4f) return; Shot("u03_ship_all_upgrades"); return;
            case 10: Lens(16f); Next(); return;
            case 11: if (El < 1f) return; Shot("u04_ship_wider"); return;
            case 12:
                {
                    LanguageLog.Begin();
                    for (int i = 0; i < 6; i++) LanguageLog.Call();
                    LanguageLog.Order(true, null, 1f, ShipAction.EngineSlow);
                    LanguageLog.Order(true, null, 1f, ShipAction.EngineHalf);
                    LanguageLog.Order(true, null, 1f, ShipAction.TurnPort, true);
                    LanguageLog.Question(); LanguageLog.Question(); LanguageLog.Talked();
                    var panel = Object.FindAnyObjectByType<MissionResultsPanel>();
                    panel.Show(new MissionResult { Delivered = true, JobName = "EASY", CargoLoaded = 5, CargoDelivered = 5, Seconds = 120f, ParSeconds = 190f, OnTimeBonus = 60, CreditsBefore = 600, CreditsAfter = 960 });
                }
                Next(); return;
            case 13: if (El < 1.5f) return; Shot("u05_results"); return;
            case 14: Finish("ok"); return;
        }
    }
}
