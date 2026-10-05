using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FairyGUI;
using HarmonyLib;
using InfantGod.UI.WindowSystem;

namespace Graywill.InfantGodSaveManager
{
    internal static class PanelRecovery
    {
        private sealed class Geometry
        {
            internal GObject Item;
            internal float X, Y, Width, Height, ScaleX, ScaleY, Rotation, Alpha;
            internal Geometry(GObject item)
            {
                Item = item; X = item.x; Y = item.y; Width = item.width; Height = item.height;
                ScaleX = item.scaleX; ScaleY = item.scaleY; Rotation = item.rotation; Alpha = item.alpha;
            }
            internal void Restore(bool restorePosition = true)
            {
                if (!NativeAccess.Alive(Item)) return;
                Item.SetSize(Width, Height, true); Item.SetScale(ScaleX, ScaleY);
                if (restorePosition) Item.SetXY(X, Y);
                Item.rotation = Rotation; Item.alpha = Alpha;
            }
        }
        private sealed class Baseline { internal Geometry Root; internal readonly List<Geometry> Objects = new List<Geometry>(); }
        private static readonly ConditionalWeakTable<GComponent, Baseline> Defaults = new ConditionalWeakTable<GComponent, Baseline>();

        internal static void BeforeCapture(NPCPanelManager manager, GComponent panel)
        {
            if (!NativeAccess.Alive(panel)) return;
            var defaults = (IDictionary)NativeAccess.Field(manager, "panelDefaultStates");
            if (defaults.Contains(panel))
            {
                // 池中面板的当前状态不能成为新的默认状态，先恢复首次记录。
                NativeAccess.Call(manager, "RestorePanelDefaultState", panel);
                return;
            }
            if (Defaults.TryGetValue(panel, out _)) return;
            var baseline = new Baseline { Root = new Geometry(panel) };
            CaptureChildren(panel, baseline);
            Defaults.Add(panel, baseline);
        }
        private static void CaptureChildren(GComponent parent, Baseline baseline)
        {
            foreach (var child in parent.GetChildren())
            {
                // 仅记录包内既有对象；运行时的立绘、心形和图片列表内容由原生逻辑管理。
                baseline.Objects.Add(new Geometry(child));
                if (child is GComponent component && !(child is GList)) CaptureChildren(component, baseline);
            }
        }
        internal static void AfterRestore(GComponent panel)
        {
            if (panel == null || !Defaults.TryGetValue(panel, out var baseline)) return;
            // 父面板的位置属于当前角色列表，不复用池对象以前的槽位坐标。
            // 先恢复父面板尺寸，再恢复子组件，避免尺寸关系把子组件的基准位置重复偏移。
            baseline.Root.Restore(false);
            foreach (var geometry in baseline.Objects) geometry.Restore();
        }

        internal static void PlaceHostFirst(NPCPanelManager manager)
        {
            var list = NativeAccess.Field(manager, "speakerList") as GList;
            if (!NativeAccess.Alive(list)) return;
            // 原生角色识别以 GetCurrentAistaltCharacter 为准，不从贴图或名称文本猜角色。
            string host = NativeAccess.Call(manager, "GetCurrentAistaltCharacter") as string;
            if (string.IsNullOrEmpty(host)) return;
            var panel = manager.GetPanelForCharacter(host);
            if (!NativeAccess.Alive(panel) || panel.parent != list) return;
            var active = NativeAccess.Field(manager, "activeCharacters") as List<string>;
            int activeIndex = active?.IndexOf(host) ?? -1;
            if (activeIndex > 0) { active.RemoveAt(activeIndex); active.Insert(0, host); }
            // 移动实际已绑定的主持人面板，NPC 相对顺序与各角色内容完整保留。
            if (list.GetChildIndex(panel) > 0) list.SetChildIndex(panel, 0);
        }
    }

    [HarmonyPatch(typeof(NPCPanelManager), "CapturePanelDefaultState")]
    internal static class CapturePanelPatch
    {
        private static void Prefix(NPCPanelManager __instance, GComponent panel) => PanelRecovery.BeforeCapture(__instance, panel);
    }
    [HarmonyPatch(typeof(NPCPanelManager), "RestorePanelDefaultState")]
    internal static class RestorePanelPatch { private static void Postfix(GComponent panel) => PanelRecovery.AfterRestore(panel); }

    [HarmonyPatch(typeof(NPCPanelManager), "RefreshSpeakerLayout")]
    internal static class RestoreSpeakerOrderPatch
    {
        private static void Prefix(NPCPanelManager __instance) => PanelRecovery.PlaceHostFirst(__instance);
    }
}
