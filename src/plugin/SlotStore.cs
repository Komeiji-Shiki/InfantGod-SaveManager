using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.RewindSystem;
using InfantGod.Core.SceneManagement;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal static class SlotStore
    {
        private static readonly FieldInfo Slots = AccessTools.Field(typeof(SaveSystem), "saveSlots");
        internal static SaveSystem System => UnityEngine.Object.FindObjectOfType<SaveSystem>();
        internal static int Count(SaveSystem system) => (Slots.GetValue(system) as SaveSlot[])?.Length ?? 3;
        internal static string SaveDirectory(SaveSystem system = null)
        {
            system = system ?? System;
            if (system == null) throw new InvalidOperationException("原生存档系统尚未就绪。");
            var settings = (ES3Settings)NativeAccess.Call(system, "GetES3Settings");
            settings.path = (string)NativeAccess.Call(system, "GetSlotFilePath", 0);
            // 统一使用原生 ES3 最终解析的目录，支持游戏的真实配置与隔离存档环境。
            return Path.GetDirectoryName(settings.FullPath);
        }

        internal static void Discover(SaveSystem system)
        {
            int count = 3;
            string path = SaveDirectory(system);
            if (Directory.Exists(path))
                foreach (string file in Directory.EnumerateFiles(path, "slot_*.es3"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file).Substring(5), out int id) && id >= 0 && id < int.MaxValue)
                        count = Math.Max(count, id + 1);
            Resize(system, count);
            Plugin.Log.LogInfo("已发现 " + count + " 个原生存档槽。");
        }

        private static void Resize(SaveSystem system, int count)
        {
            var previous = system.GetAllSlots();
            if (previous != null && count <= previous.Length) return;
            var slots = new SaveSlot[count];
            if (previous != null) Array.Copy(previous, slots, previous.Length);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == null) slots[i] = new SaveSlot(i, "存档 " + (i + 1));
            Slots.SetValue(system, slots);
        }

        internal static int Add(SaveSystem system)
        {
            int id = Count(system);
            Resize(system, checked(id + 1));
            // 空槽也写入原生文件，重新打开游戏时可从文件发现，无需额外槽数上限。
            Persist(system, id);
            return id;
        }

        internal static void Persist(SaveSystem system, int id) => NativeAccess.Call(system, "SaveSlotToFile", id);
        internal static void Rename(SaveSystem system, int id, string name)
        {
            system.GetSlotInfo(id).slotName = name.Trim();
            Persist(system, id);
        }

        internal static bool Busy(SaveSystem system)
        {
            if ((bool)NativeAccess.Field(system, "quickSaveOperationInProgress")) return true;
            var manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (manager != null && NativeAccess.Field(manager, "pendingLoadCoroutine") != null) return true;
            var scenes = UnityEngine.Object.FindObjectOfType<GameSceneManager>();
            return scenes != null && scenes.IsLoading;
        }
        internal static bool Ready(SaveSystem system) => system != null && (bool)NativeAccess.Field(system, "isInitialized");
        internal static bool HasQuickSave(SaveSlot slot) => slot?.quickSave != null &&
            !string.IsNullOrEmpty(slot.quickSave.rewindName) && RewindService.HasSnapshotGameState(slot.quickSave.rewindName);

        internal static List<SavePoint> Points(SaveSlot slot)
        {
            var points = new List<SavePoint>();
            if (slot == null) return points;
            foreach (var point in ManualPoints.List(slot.slotId))
                points.Add(new SavePoint { Day = point.day, Manual = point, Title = point.title, Detail = $"{point.hour:00}:{point.minute:00}  ·  " + LocalTime(point.savedAtUtc) });
            if (HasQuickSave(slot))
                points.Add(new SavePoint { Day = slot.quickSave.gameDay, Quick = true, Title = "即时存档", Detail = LocalTime(slot.quickSave.savedAtUtc) + (slot.quickSave.hasPosition ? "  ·  已保存对话位置" : "  ·  对话外状态") });
            var automatic = new List<SavePoint>();
            slot.EnsureMidDaySaveEntries();
            if (slot.midDaySaveEntries != null)
                foreach (var entry in slot.midDaySaveEntries)
                    if (entry.saveData != null) automatic.Add(FromData(entry.saveData, true, entry.sequence));
            if (slot.dailySaves != null)
                foreach (var entry in slot.dailySaves)
                    if (entry.Value != null) automatic.Add(FromData(entry.Value, false, -1));
            // 每日与连线记录组成同一时间线，缺失游戏时刻的旧记录排在同日已知时刻之后。
            points.AddRange(automatic.OrderByDescending(point => point.Day).ThenByDescending(point => point.GameMinutes).ThenByDescending(point => point.Sequence));
            return points;
        }
        private static SavePoint FromData(SaveData data, bool midDay, int sequence)
        {
            string kind = midDay ? (data.saveReason == "手动存档" ? "手动记录" : "连线前记录") : "每日记录";
            return new SavePoint
            {
                Day = data.gameDay, MidDay = midDay, Sequence = sequence,
                GameMinutes = data.hasTimeData ? data.gameHour * 60 + data.gameMinute : -1,
                Title = BranchContext.TryTitle(data.jumpToNode, out var title) ? title : kind,
                Detail = kind + "  ·  " + (data.hasTimeData ? $"{data.gameHour:00}:{data.gameMinute:00}  ·  " : "") + LocalTime(data.saveTime)
            };
        }
        internal static string LocalTime(string value) => DateTime.TryParse(value, out var date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : (value ?? "");
    }

    internal sealed class SavePoint
    {
        internal int Day, GameMinutes = -1, Sequence = -1;
        internal bool Quick, MidDay;
        internal string Title, Detail;
        internal ManualPoint Manual;
    }

    [HarmonyPatch(typeof(SaveSystem), "InitializeSaveSystem")]
    internal static class InitializeSlotsPatch { private static void Postfix(SaveSystem __instance) => SlotStore.Discover(__instance); }

    [HarmonyPatch(typeof(SaveSystem), "GetMaxSlots")]
    internal static class SlotCountPatch
    {
        private static bool Prefix(SaveSystem __instance, ref int __result) { __result = SlotStore.Count(__instance); return false; }
    }

    [HarmonyPatch(typeof(SaveSystem), "IsValidSlotId")]
    internal static class ValidSlotPatch
    {
        private static bool Prefix(SaveSystem __instance, int slotId, ref bool __result)
        { __result = slotId >= 0 && slotId < SlotStore.Count(__instance); return false; }
    }

    [HarmonyPatch]
    internal static class SlotLoopPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "LoadAllSlots", "HasAnySave", "ClearAllSaveData" })
                yield return AccessTools.Method(typeof(SaveSystem), name);
        }
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // 这三个方法中的常量 3 都是槽循环终点，其余存档和备份流程保留原实现。
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_I4_3)
                {
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SlotStore), nameof(SlotStore.Count)));
                }
                else yield return instruction;
            }
        }
    }
}
