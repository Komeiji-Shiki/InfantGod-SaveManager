using System.Collections.Generic;
using FairyGUI;
using HarmonyLib;
using InfantGod.UI.WindowSystem;

namespace Graywill.InfantGodSaveManager
{
    [HarmonyPatch(typeof(NPCPanelManager), "RefreshSpeakerLayout")]
    internal static class SpeakerOrder
    {
        private static void Prefix(NPCPanelManager __instance)
        {
            var list = NativeAccess.Field(__instance, "speakerList") as GList;
            if (!NativeAccess.Alive(list)) return;
            // 原生角色识别以 GetCurrentAistaltCharacter 为准，不从贴图或名称文本猜角色。
            string host = NativeAccess.Call(__instance, "GetCurrentAistaltCharacter") as string;
            if (string.IsNullOrEmpty(host)) return;
            var panel = __instance.GetPanelForCharacter(host);
            if (!NativeAccess.Alive(panel) || panel.parent != list) return;
            var active = NativeAccess.Field(__instance, "activeCharacters") as List<string>;
            int activeIndex = active?.IndexOf(host) ?? -1;
            if (activeIndex > 0) { active.RemoveAt(activeIndex); active.Insert(0, host); }
            // 只调整真实角色的列表顺序；回收、动画、尺寸与子控件关系全部由原生流程处理。
            if (list.GetChildIndex(panel) > 0) list.SetChildIndex(panel, 0);
        }
    }
}
