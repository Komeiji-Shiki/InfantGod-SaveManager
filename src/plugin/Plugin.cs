using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Graywill.InfantGodSaveManager
{
    [BepInPlugin("graywill.infantgod.savemanager", "幼神·多存档管理", "1.0.1")]
    [BepInProcess("Aistalt.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string DataDirectory;
        private static bool quitting;
        private void Awake()
        {
            Log = Logger;
            DataDirectory = System.IO.Path.GetDirectoryName(Info.Location);
            new Harmony("graywill.infantgod.savemanager").PatchAll(typeof(Plugin).Assembly);
            SceneManager.sceneLoaded += SceneLoaded;
            Application.quitting += Quit;
            if (Time.frameCount > 0 && SceneManager.GetActiveScene().isLoaded) EnsureHost();
            Logger.LogInfo("多存档管理 1.0.1 已注册：主菜单载入页 / 开始菜单普通保存 / 原生快速加载。");
        }
        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => EnsureHost();
        private static void EnsureHost()
        {
            if (quitting || SaveManagerHost.Instance != null) return;
            // 初始场景会清理加载器宿主，实际界面宿主在场景就绪后建立。
            var root = new GameObject("BepInEx_InfantGodSaveManager") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(root);
            root.AddComponent<SaveManagerHost>();
        }
        private static void Quit()
        {
            quitting = true;
            SceneManager.sceneLoaded -= SceneLoaded;
            Application.quitting -= Quit;
        }
    }

    internal static class NativeAccess
    {
        private static readonly System.Reflection.FieldInfo PanelUi = AccessTools.Field(typeof(FairyGUI.UIPanel), "_ui");
        internal static bool Alive(FairyGUI.GObject item) => item != null && !item.isDisposed &&
            item.displayObject != null && !item.displayObject.isDisposed && item.displayObject.cachedTransform != null;
        internal static FairyGUI.GComponent ExistingRoot(FairyGUI.UIPanel panel)
        {
            // ui getter 会惰性创建界面，扫描只允许读取已创建的对象。
            if (panel == null || !panel.isActiveAndEnabled || panel.container == null || panel.container.isDisposed) return null;
            var root = PanelUi.GetValue(panel) as FairyGUI.GComponent;
            return Alive(root) ? root : null;
        }
        internal static void LoadPackage(string name)
        {
            if (name == "LiveStreamWindow" && FairyGUI.UIPackage.GetByName("SongPlayer") == null) LoadPackage("SongPlayer");
            var package = FairyGUI.UIPackage.GetByName(name) ?? FairyGUI.UIPackage.AddPackage("FairyGUI/UI/" + name);
            foreach (var dependency in package.dependencies)
                if (dependency.TryGetValue("name", out var dependencyName) && FairyGUI.UIPackage.GetByName(dependencyName) == null)
                    LoadPackage(dependencyName);
            // 原生加载器在包已存在时会跳过生成类绑定，因此提前加载必须同步执行 Binder。
            var binder = AccessTools.TypeByName(name + "." + name + "Binder");
            if (binder != null) AccessTools.Method(binder, "BindAll")?.Invoke(null, null);
        }
        internal static object Field(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
        internal static object Call(object target, string name, params object[] args) => AccessTools.Method(target.GetType(), name).Invoke(target, args);
    }
}
