using System;
using System.Linq;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core.OSSystem;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.RewindSystem;
using InfantGod.Core.UI;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal static class MenuIntegration
    {
        internal const string SaveButtonName = SaveManagerWindow.Prefix + "NormalSave";
        internal static void BindExisting()
        {
            var os = UnityEngine.Object.FindObjectOfType<OSManager>();
            var workspace = os?.GetWorkspaceRoot();
            if (!NativeAccess.Alive(workspace)) return;
            var taskbar = workspace.GetChild("TaskBar")?.asCom;
            var menu = taskbar?.GetChild("TaskBar_Menu")?.asCom;
            var list = menu?.GetChild("Menu_List")?.asList;
            if (list != null) Bind(os, list);
        }

        internal static void Bind(OSManager os, GList menuList)
        {
            if (!NativeAccess.Alive(menuList)) return;
            var quickLoad = NativeAccess.Field(os, "_workspaceQuickLoadButton") as GButton;
            if (NativeAccess.Alive(quickLoad)) SetCaption(quickLoad, "快速加载 (F9)");
            if (menuList.GetChild(SaveButtonName) != null) return;
            var quickSave = NativeAccess.Field(os, "_workspaceQuickSaveButton") as GButton;
            if (!NativeAccess.Alive(quickSave) || string.IsNullOrEmpty(quickSave.resourceURL)) return;
            var normal = UIPackage.CreateObjectFromURL(quickSave.resourceURL)?.asButton;
            if (normal == null) return;
            normal.name = SaveButtonName;
            SetCaption(normal, "普通保存");
            normal.onClick.Clear();
            normal.onClick.Add(context =>
            {
                context.StopPropagation();
                var parent = menuList.parent;
                var expand = parent?.GetController("ExpandMenu"); if (expand != null) expand.selectedIndex = 0;
                expand = parent?.parent?.GetController("ExpandMenu"); if (expand != null) expand.selectedIndex = 0;
                SaveManagerHost.Instance?.OpenSavePage();
            });
            float previousHeight = menuList.height;
            var menu = menuList.parent;
            float previousBottom = menu.y + menu.height;
            menuList.AddChildAt(normal, Math.Min(4, menuList.numChildren));
            menuList.ResizeToFit(menuList.numChildren);
            float extra = menuList.height - previousHeight;
            if (extra > 0)
            {
                // 以原菜单实测尺寸扩展，保持菜单底边位置；原有快存/快读索引保持不变。
                menu.height += extra; menu.y = previousBottom - menu.height;
            }
        }
        private static void SetCaption(GButton button, string text)
        {
            button.title = text;
            var caption = button.GetChild("Start");
            if (caption != null) caption.text = text;
        }
    }

    [HarmonyPatch(typeof(OSManager), "BindWorkspaceQuickSaveButtons")]
    internal static class BindSaveMenuPatch
    {
        private static void Postfix(OSManager __instance, GList menuList) => MenuIntegration.Bind(__instance, menuList);
    }

    [HarmonyPatch(typeof(FGUIMainMenuController), "OnStatusControllerChanged")]
    internal static class ReplaceLoadPagePatch
    {
        private static bool Prefix(FGUIMainMenuController __instance)
        {
            var root = NativeAccess.Field(__instance, "mainMenuUI") as GComponent;
            if (root?.GetController("Status")?.selectedIndex != 1 || SaveManagerHost.Instance == null) return true;
            var oldList = root.GetChild("List_SaveSlot"); if (oldList != null) oldList.visible = false;
            var oldBack = root.GetChild("Btn_Return2Hub"); if (oldBack != null) oldBack.visible = false;
            SaveManagerHost.Instance.OpenLoadPage(root);
            return false;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "RequestQuickLoad")]
    internal static class LatestSaveQuickLoadPatch
    {
        private static bool Prefix(SaveSystem __instance)
        {
            var host = SaveManagerHost.Instance;
            if (host == null || host.Working || !SlotStore.Ready(__instance) || SlotStore.Busy(__instance)) return true;
            int slot = __instance.GetActiveSlotId();
            var latest = ManualPoints.List(slot)
                .Where(point => RewindService.HasSnapshotGameState(point.snapshot) && DateTimeOffset.TryParse(point.savedAtUtc, out _))
                .OrderByDescending(point => DateTimeOffset.Parse(point.savedAtUtc)).FirstOrDefault();
            if (latest == null) return true;
            var quick = __instance.GetSlotInfo(slot)?.quickSave;
            if (quick != null && RewindService.HasSnapshotGameState(quick.rewindName) &&
                DateTimeOffset.TryParse(quick.savedAtUtc, out var quickTime) && quickTime >= DateTimeOffset.Parse(latest.savedAtUtc)) return true;
            host.Load(slot, new SavePoint { Day = latest.day, Manual = latest, Title = latest.title });
            return false;
        }
    }
}
