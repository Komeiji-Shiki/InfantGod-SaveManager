using System;
using System.Collections;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core;
using InfantGod.Core.OSSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.SceneManagement;
using InfantGod.Core.UI;
using InfantGod.Core.RewindSystem;
using InfantGod.UI.WindowSystem.Windows;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Graywill.InfantGodSaveManager
{
    public sealed class SaveManagerHost : MonoBehaviour
    {
        internal static SaveManagerHost Instance;
        internal SaveManagerWindow View;
        internal bool Working;
        private readonly BranchContext branchContext = new BranchContext();
        private float nextProbe;
        internal bool CapturesInput => Working || View?.IsOpen == true;
        internal bool InGame => SceneManager.GetActiveScene().name == "MainGame";

        private void Awake()
        {
            Instance = this;
            View = new SaveManagerWindow(this);
            SceneManager.sceneLoaded += SceneLoaded;
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { View.Dispose(); nextProbe = 0; }
        private void Update()
        {
            if (Time.unscaledTime >= nextProbe)
            {
                nextProbe = Time.unscaledTime + 0.5f;
                branchContext.Bind();
                View.EnsureEntry();
            }
            View.Layout();
            if (!Working && CapturesInput && Input.GetKeyDown(KeyCode.Escape)) View.Close();
        }
        internal void Toggle()
        {
            if (Working) return;
            if (View.IsOpen) View.Close();
            else if (SlotStore.Ready(SlotStore.System) && !SlotStore.Busy(SlotStore.System)) View.Open();
        }
        internal void OpenSavePage()
        {
            if (Working || !InGame || !SlotStore.Ready(SlotStore.System) || SlotStore.Busy(SlotStore.System)) return;
            View.Open(true);
        }
        internal void OpenLoadPage(GComponent mainMenu)
        {
            if (Working || !SlotStore.Ready(SlotStore.System)) return;
            View.Open(false, mainMenu);
        }
        internal void Save(int slot, string title, string note)
        {
            if (Working || !InGame) return;
            StartCoroutine(SaveRoutine(slot, title, note));
        }
        private IEnumerator SaveRoutine(int slot, string title, string note)
        {
            var system = SlotStore.System;
            if (!SlotStore.Ready(system) || SlotStore.Busy(system)) { View.Status("原生存读档正在进行，请完成后再保存。"); yield break; }
            Working = true;
            View.HideForCapture();
            // 等待实际渲染结束，缩略图与原生快照在同一帧捕获，不包含存档管理界面。
            yield return new WaitForEndOfFrame();
            ManualPoint point = null;
            string error = null;
            try { point = ManualPoints.Capture(slot, title, note); }
            catch (Exception exception) { Plugin.Log.LogError(exception); error = exception.Message; }
            finally { Working = false; View.ShowAfterCapture(); }
            View.Refresh();
            View.Status(point != null ? "已新建独立存档点：" + point.title + (point.hasPosition ? "。对话位置与游戏状态均已保存。" : "。已保存对话外游戏状态。") : "保存失败：" + error);
        }
        internal void Load(int slot, SavePoint point)
        {
            if (Working || point == null) return;
            var system = SlotStore.System;
            var manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager == null || system == null || SlotStore.Busy(system)) { View.Status("游戏的读档系统还未就绪。"); return; }
            View.Close();
            if (point.Manual != null)
            {
                if (!RewindService.HasSnapshotGameState(point.Manual.snapshot)) { View.Open(); View.Status("这个存档点的原生快照缺失，未修改当前游戏。"); return; }
                ManualPoints.PendingLoad = point.Manual;
                Working = true;
            }
            bool useRewind = point.Quick || point.Manual != null;
            if (InGame) manager.LoadGame(slot, point.Day, point.MidDay, point.Sequence, useRewind);
            else
            {
                var scenes = UnityEngine.Object.FindObjectOfType<GameSceneManager>();
                if (scenes == null) { ManualPoints.PendingLoad = null; Working = false; View.Open(); View.Status("场景管理器还未就绪。"); return; }
                var menu = UnityEngine.Object.FindObjectOfType<FGUIMainMenuController>();
                if (menu != null) NativeAccess.Call(menu, "ApplyModLoadPolicyBeforeEnterGame");
                manager.LoadGameFromMainMenu(slot, point.Day, point.MidDay, point.Sequence, useRewind);
                scenes.StartGame();
            }
        }
        internal void ManualLoadFinished(bool success)
        {
            Working = false;
            if (!success) { View.Open(); View.Status("这个存档点未恢复成功。原生恢复日志已保留，请选择其他存档点。"); }
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            View?.Dispose();
            branchContext.Dispose();
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch(typeof(FGUICommandManager), "GetBaseUserData")]
    internal static class OwnMetadataPatch
    {
        private static bool Prefix(GObject obj, ref string __result)
        {
            // 新增的普通保存按钮复用原生外观，行为由本 Mod 绑定，不继承模板的命令元数据。
            if (obj?.name == MenuIntegration.SaveButtonName) { __result = ""; return false; }
            if (!(obj is GComponent) || obj.packageItem != null) return true;
            for (var current = obj; current != null; current = current.parent)
                if (current.name?.StartsWith(SaveManagerWindow.Prefix, StringComparison.Ordinal) == true) { __result = ""; return false; }
            return true;
        }
    }

    [HarmonyPatch]
    internal static class ManagerInputPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SaveSystem), "HandleQuickSaveInput");
            yield return AccessTools.Method(typeof(OSManager), "UpdateEscapeShortcut");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "UpdateAdvanceHotKey");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "TryToggleDanmakuInputByHotKey");
            yield return AccessTools.Method(typeof(LiveStreamSpeakerManager), "TrySendDanmakuByEnter");
            yield return AccessTools.Method(typeof(PVShortcutManager), "Update");
        }
        private static bool Prefix() => SaveManagerHost.Instance?.CapturesInput != true;
    }
}
