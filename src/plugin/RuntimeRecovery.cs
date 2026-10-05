using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using InfantGod.Core.OSSystem;
using InfantGod.Core.RewindSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.UI.WindowSystem.Windows;
using Newtonsoft.Json;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    [Serializable]
    internal sealed class RuntimeRecord
    {
        public int version = 1;
        public List<HeartRecord> hearts = new List<HeartRecord>();
        public BranchRecord branch;
        public string manualOriginId;
    }
    [Serializable]
    internal sealed class HeartRecord
    {
        public string character;
        public int current, maximum;
    }

    internal static class RuntimeRecovery
    {
        internal const string VariableKey = "$Graywill_SaveManager_Runtime_v1";
        private static RuntimeRecord pending;
        private static readonly HashSet<string> restoredHearts = new HashSet<string>(StringComparer.Ordinal);

        internal static string Capture()
        {
            var record = new RuntimeRecord { branch = BranchContext.Capture(null), manualOriginId = ManualPoints.OriginPointId };
            var manager = UnityEngine.Object.FindObjectOfType<LiveStreamDialogueHPBarManager>();
            if (manager != null)
                foreach (DictionaryEntry pair in (IDictionary)NativeAccess.Field(manager, "hpBars"))
                    if ((bool)NativeAccess.Field(pair.Value, "IsActive"))
                        record.hearts.Add(new HeartRecord
                        {
                            character = (string)pair.Key,
                            current = (int)NativeAccess.Field(pair.Value, "CurrentHeart"),
                            maximum = (int)NativeAccess.Field(pair.Value, "MaxHeart")
                        });
            return JsonConvert.SerializeObject(record);
        }

        internal static void Begin(SaveData saveData)
        {
            pending = null;
            restoredHearts.Clear();
            if (saveData?.yarnVariables != null && saveData.yarnVariables.TryGetValue(VariableKey, out object value) && value is string json)
            {
                try { pending = JsonConvert.DeserializeObject<RuntimeRecord>(json); }
                catch (JsonException exception) { Plugin.Log.LogWarning("辩论恢复记录无法读取：" + exception.Message); }
            }
            BranchContext.Restore(pending?.branch);
            ManualPoints.OriginPointId = pending?.manualOriginId;
            var manager = UnityEngine.Object.FindObjectOfType<LiveStreamDialogueHPBarManager>();
            if (manager != null)
            {
                // 原清理流程没有清空独立血条状态，先移除上一次运行的角色和血量。
                manager.StopAllHPBars();
                ((IDictionary)NativeAccess.Field(manager, "hpBars")).Clear();
            }
        }

        internal static void RestoreHearts()
        {
            if (pending?.version != 1 || pending.hearts == null) return;
            var manager = UnityEngine.Object.FindObjectOfType<LiveStreamDialogueHPBarManager>();
            var speakers = UnityEngine.Object.FindObjectOfType<LiveStreamSpeakerManager>();
            if (manager == null || speakers == null) return;
            foreach (var record in pending.hearts)
            {
                if (string.IsNullOrEmpty(record.character) || restoredHearts.Contains(record.character)) continue;
                var panel = speakers.GetSpeakerPanel(record.character);
                if (!NativeAccess.Alive(panel)) continue;
                var state = NativeAccess.Call(manager, "GetOrCreateState", record.character);
                AccessTools.Field(state.GetType(), "MaxHeart").SetValue(state, record.maximum);
                AccessTools.Field(state.GetType(), "CurrentHeart").SetValue(state, record.current);
                AccessTools.Field(state.GetType(), "IsActive").SetValue(state, true);
                var controller = panel.GetController("ShowHPBar");
                if (controller != null) controller.selectedIndex = 1;
                NativeAccess.Call(manager, "InitializeHeartList", panel, record.maximum);
                NativeAccess.Call(manager, "ApplyHeartDisplay", record.character, state);
                // 显示恢复不调用 ShowHPBar，避免把当前血量重置为上限或改写已恢复的 Yarn 变量。
                restoredHearts.Add(record.character);
                Plugin.Log.LogInfo($"已恢复辩论血条：{record.character} {record.current}/{record.maximum}");
            }
        }

        internal static void RefreshViewer()
        {
            var os = UnityEngine.Object.FindObjectOfType<OSManager>();
            if (os == null) return;
            foreach (var controller in UnityEngine.Object.FindObjectsOfType<HeatBarController>())
            {
                var viewer = NativeAccess.Field(controller, "_viewerBar") as FairyGUI.GObject;
                if (!NativeAccess.Alive(viewer))
                {
                    AccessTools.Field(typeof(HeatBarController), "_workspaceAttached").SetValue(controller, false);
                    NativeAccess.Call(controller, "AttachToWorkspace");
                }
                NativeAccess.Call(controller, "CheckLiveStreamWindow", os);
                // 原生读档使用静默 SetSelectedPage，不会触发观众栏监听的 onChanged。
                NativeAccess.Call(controller, "HandleLiveStreamStatusChanged");
                NativeAccess.Call(controller, "InitializeViewerBar");
            }
        }

        internal static IEnumerator After(IEnumerator original, Action completed)
        {
            try { while (original.MoveNext()) yield return original.Current; }
            finally { (original as IDisposable)?.Dispose(); }
            completed();
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "CreateSaveData")]
    internal static class SaveRuntimePatch
    {
        private static void Postfix(SaveData __result)
        {
            if (__result == null) return;
            if (__result.yarnVariables == null) __result.yarnVariables = new Dictionary<string, object>();
            __result.yarnVariables[RuntimeRecovery.VariableKey] = RuntimeRecovery.Capture();
        }
    }

    [HarmonyPatch(typeof(RewindService), "CaptureSnapshot")]
    internal static class CaptureRewindPatch
    {
        private static void Postfix(RewindSnapshot __result)
        {
            if (__result == null) return;
            if (__result.variables == null) __result.variables = new List<VarEntry>();
            __result.variables.RemoveAll(entry => entry.name == RuntimeRecovery.VariableKey);
            __result.variables.Add(new VarEntry { name = RuntimeRecovery.VariableKey, type = "string", value = RuntimeRecovery.Capture() });
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "ApplySaveData")]
    internal static class BeginRestorePatch { private static void Prefix(SaveData saveData) => RuntimeRecovery.Begin(saveData); }

    [HarmonyPatch(typeof(SaveSystem), "ApplyYarnVariables")]
    internal static class FilterPrivateVariablePatch
    {
        private static void Prefix(ref Dictionary<string, object> variables)
        {
            if (variables == null || !variables.ContainsKey(RuntimeRecovery.VariableKey)) return;
            // 元数据仍保存在原 ES3 容器中，不注入剧情脚本的可见变量。
            variables = variables.Where(pair => pair.Key != RuntimeRecovery.VariableKey).ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "RestoreRuntimeStateAfterLoad")]
    internal static class RuntimeRestoredPatch
    {
        private static void Postfix(ref IEnumerator __result) => __result = RuntimeRecovery.After(__result, () =>
        { RuntimeRecovery.RefreshViewer(); RuntimeRecovery.RestoreHearts(); });
    }

    [HarmonyPatch(typeof(RewindService), "RestoreLiveStreamControllers")]
    internal static class RewindControllersPatch { private static void Postfix() => RuntimeRecovery.RefreshViewer(); }

    [HarmonyPatch(typeof(RewindService), "RebindSpeakersAfterRestore")]
    internal static class RewindSpeakersPatch
    {
        private static void Postfix(ref IEnumerator __result) => __result = RuntimeRecovery.After(__result, RuntimeRecovery.RestoreHearts);
    }

    [HarmonyPatch(typeof(LiveStreamSpeakerManager), "RestoreCastState")]
    internal static class CastRestoredPatch { private static void Postfix() => RuntimeRecovery.RestoreHearts(); }
}
