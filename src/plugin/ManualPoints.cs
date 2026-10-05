using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core;
using InfantGod.Core.RewindSystem;
using InfantGod.Core.SaveSystem;
using Newtonsoft.Json;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    [Serializable]
    internal sealed class ManualPoint
    {
        public int schemaVersion = 1;
        public string id, snapshot, title, note, savedAtUtc;
        public string parentPointId, parentPointTitle;
        public bool parentPointDeleted;
        public int slotId, day, hour, minute;
        public bool hasPosition;
        public BranchRecord branch;
    }

    internal static class ManualPoints
    {
        internal static string Root => Path.Combine(SlotStore.SaveDirectory(), "Graywill.SaveManager");
        internal static string SlotPath(int slot) => Path.Combine(Root, slot.ToString(CultureInfo.InvariantCulture));
        private static string MetadataPath(ManualPoint point) => Path.Combine(SlotPath(point.slotId), point.id + ".json");
        internal static string ThumbnailPath(ManualPoint point) => Path.Combine(SlotPath(point.slotId), point.id + ".png");
        internal static ManualPoint PendingLoad;
        internal static string OriginPointId;

        internal static List<ManualPoint> List(int slot)
        {
            var items = new List<ManualPoint>();
            string path = SlotPath(slot);
            if (!Directory.Exists(path)) return items;
            foreach (string file in Directory.EnumerateFiles(path, "*.json"))
            {
                try
                {
                    var item = JsonConvert.DeserializeObject<ManualPoint>(File.ReadAllText(file));
                    if (item != null && item.slotId == slot && Guid.TryParseExact(item.id, "N", out _) &&
                        Path.GetFileNameWithoutExtension(file) == item.id && item.snapshot == SnapshotName(slot, item.id)) items.Add(item);
                }
                catch (Exception exception) { Plugin.Log.LogWarning("手动存档点元数据无法读取：" + file + "；" + exception.Message); }
            }
            return items.OrderByDescending(point => point.savedAtUtc, StringComparer.Ordinal).ToList();
        }
        private static string SnapshotName(int slot, string id) => "__graywill_manual_" + slot + "_" + id;

        internal static ManualPoint Capture(int slot, string title, string note)
        {
            var system = SlotStore.System;
            system.InitializeSystemReferences();
            var point = new ManualPoint { id = Guid.NewGuid().ToString("N"), slotId = slot, title = title?.Trim(), note = note?.Trim(), savedAtUtc = DateTime.UtcNow.ToString("o") };
            point.snapshot = SnapshotName(slot, point.id);
            var parent = List(slot).FirstOrDefault(candidate => candidate.id == OriginPointId);
            point.parentPointId = parent?.id; point.parentPointTitle = parent?.title;
            var time = UnityEngine.Object.FindObjectOfType<GameTimeManager>();
            point.day = time.GetCurrentDay(); point.hour = time.GetCurrentHour(); point.minute = time.GetCurrentMinute();
            string nativePath;
            bool hasPosition;
            bool captured = RewindService.SaveSnapshot(point.snapshot, out nativePath, out hasPosition);
            if (!captured || !RewindService.HasSnapshotGameState(point.snapshot))
            {
                RewindService.DeleteSnapshot(point.snapshot);
                throw new IOException("原生对话快照或游戏状态没有保存成功：" + nativePath);
            }
            point.hasPosition = hasPosition;
            string snapshotFile, error;
            var snapshot = RewindService.ReadSnapshot(point.snapshot, out snapshotFile, out error);
            if (snapshot == null) throw new IOException("已写入的对话快照无法读取：" + error);
            point.branch = BranchContext.Capture(snapshot.vm?.nodeName);
            if (string.IsNullOrWhiteSpace(point.title)) point.title = point.branch.title;
            Directory.CreateDirectory(SlotPath(slot));
            // 元数据最后写入；只有原生快照与游戏状态均成功的点才出现在列表中。
            try
            {
                CaptureThumbnail(ThumbnailPath(point));
                File.WriteAllText(MetadataPath(point), JsonConvert.SerializeObject(point, Formatting.Indented));
            }
            catch
            {
                RewindService.DeleteSnapshot(point.snapshot);
                if (File.Exists(ThumbnailPath(point))) File.Delete(ThumbnailPath(point));
                throw;
            }
            var slotInfo = system.GetSlotInfo(slot);
            slotInfo.currentDay = point.day; slotInfo.lastSaveTime = point.savedAtUtc;
            system.SetActiveSlot(slot); SlotStore.Persist(system, slot);
            OriginPointId = point.id;
            return point;
        }

        private static void CaptureThumbnail(string path)
        {
            Texture2D frame = null, thumbnail = null;
            RenderTexture resized = null;
            var previous = RenderTexture.active;
            try
            {
                frame = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                frame.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); frame.Apply();
                int width = 480, height = Math.Max(1, Mathf.RoundToInt(width * (float)Screen.height / Screen.width));
                resized = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(frame, resized); RenderTexture.active = resized;
                thumbnail = new Texture2D(width, height, TextureFormat.RGB24, false);
                thumbnail.ReadPixels(new Rect(0, 0, width, height), 0, 0); thumbnail.Apply();
                File.WriteAllBytes(path, thumbnail.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (resized != null) RenderTexture.ReleaseTemporary(resized);
                if (frame != null) UnityEngine.Object.Destroy(frame);
                if (thumbnail != null) UnityEngine.Object.Destroy(thumbnail);
            }
        }

        internal static void Delete(ManualPoint point)
        {
            RewindService.DeleteSnapshot(point.snapshot);
            File.Delete(MetadataPath(point));
            if (File.Exists(ThumbnailPath(point))) File.Delete(ThumbnailPath(point));
            foreach (var child in List(point.slotId).Where(candidate => candidate.parentPointId == point.id))
            {
                // 保留实际来源的名称，并明确来源点已删除，不把子分支错误改接到其他点。
                child.parentPointDeleted = true;
                File.WriteAllText(MetadataPath(child), JsonConvert.SerializeObject(child, Formatting.Indented));
            }
        }

        internal static IEnumerator Load(SaveSystem system, ManualPoint point, Action<bool> onDone)
        {
            bool success = false;
            try
            {
                yield return RewindService.LoadRoutine(point.snapshot, message => Plugin.Log.LogInfo(message), (loaded, message) =>
                { success = loaded; if (!loaded) Plugin.Log.LogWarning(message); }, true);
                if (success)
                {
                    system.SetActiveSlot(point.slotId);
                    BranchContext.Restore(point.branch);
                    OriginPointId = point.id;
                    Plugin.Log.LogInfo("独立手动点恢复完成：" + point.title + "，槽位 " + (point.slotId + 1));
                }
            }
            finally
            {
                PendingLoad = null;
                onDone?.Invoke(success);
                if (SaveManagerHost.Instance != null) SaveManagerHost.Instance.ManualLoadFinished(success);
            }
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "LoadQuickSaveForSlot")]
    internal static class ManualPointLoadPatch
    {
        private static bool Prefix(SaveSystem __instance, int slotId, Action<bool> onDone, ref IEnumerator __result)
        {
            var point = ManualPoints.PendingLoad;
            if (point == null || point.slotId != slotId) return true;
            // 复用 GameManager 的跨场景准备和 Day Start 完成边界，只替换本次选中的恢复来源。
            // 不修改槽位的 quickSave 字段，玩家的 F5/F9 记录完整保留。
            __result = ManualPoints.Load(__instance, point, onDone);
            return false;
        }
    }

    [HarmonyPatch(typeof(InfantGod.Core.SceneManagement.GameManager), "MarkAsNewGame")]
    internal static class NewGameOriginPatch
    {
        private static void Postfix() { ManualPoints.OriginPointId = null; BranchContext.Restore(null); }
    }
}
