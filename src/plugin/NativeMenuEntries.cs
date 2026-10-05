using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FairyGUI;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal static class NativeMenuEntries
    {
        private static readonly ConditionalWeakTable<GList, MenuLayout> Layouts = new ConditionalWeakTable<GList, MenuLayout>();

        internal static void Change(GList list, Action change)
        {
            if (!NativeAccess.Alive(list) || !NativeAccess.Alive(list.parent)) return;
            var layout = Layouts.GetValue(list, value => new MenuLayout(value));
            change();
            layout.Refresh();
        }

        internal static void Refresh(GList list)
        {
            if (!NativeAccess.Alive(list) || !NativeAccess.Alive(list.parent)) return;
            Layouts.GetValue(list, value => new MenuLayout(value)).Refresh();
        }

        private sealed class MenuLayout
        {
            private readonly GList list;
            private readonly GComponent menu;
            private readonly float nativeMenuHeight, nativeListHeight;
            private readonly Controller expandController;
            private readonly GObject startButton;
            private readonly List<Backdrop> backdrops = new List<Backdrop>();
            private bool applying;

            internal MenuLayout(GList value)
            {
                list = value; menu = list.parent;
                expandController = menu.parent?.GetController("ExpandMenu") ?? menu.GetController("ExpandMenu");
                startButton = menu.parent?.GetChild("Start");
                nativeMenuHeight = AuthoredHeight(menu);
                nativeListHeight = AuthoredHeight(list);
                foreach (var child in menu.GetChildren())
                {
                    if (child == list) continue;
                    float height = AuthoredHeight(child);
                    // 原菜单的显示遮罩与大面积背景都要随内容扩展，不能只扩大透明的父容器。
                    if (child.displayObject == menu.mask || child.name == "Mask" ||
                        (height >= nativeListHeight && child.width >= list.width))
                        backdrops.Add(new Backdrop { Item = child, Height = height });
                }
                BindController(menu.GetController("ExpandMenu"));
                BindController(menu.GetController("Status"));
                BindController(menu.parent?.GetController("ExpandMenu"));
            }

            private void BindController(Controller controller)
            {
                if (controller != null) controller.onChanged.Add(Refresh);
            }

            internal void Refresh()
            {
                if (applying || !NativeAccess.Alive(list) || !NativeAccess.Alive(menu)) return;
                if (expandController == null) return;
                // 原生关闭态依靠控制器把菜单移出画面，不能把该位置作为展开后的底边锚点。
                // 显式关闭显示与命中，保证增加的条目也不会覆盖开始按钮。
                bool expanded = expandController.selectedIndex != 0;
                menu.visible = expanded;
                menu.touchable = expanded;
                if (!expanded || !NativeAccess.Alive(startButton)) return;
                applying = true;
                try
                {
                    // 原菜单使用按固定高度换列的布局，增加第六项后退出会被排到被裁切的第二列。
                    // 开始菜单始终保持单列，再按完整条目数量计算真实高度。
                    list.layout = ListLayoutType.SingleColumn;
                    list.columnCount = 1;
                    list.EnsureBoundsCorrect();
                    list.ResizeToFit(list.numChildren);
                    float contentHeight = list.height;
                    float growth = Mathf.Max(0, contentHeight - nativeListHeight);
                    float targetHeight = nativeMenuHeight + growth;
                    // 使用相对原生尺寸的总增量，多个独立 Mod 反复刷新也不会累计撑高。
                    menu.height = targetHeight;
                    foreach (var backdrop in backdrops)
                        if (NativeAccess.Alive(backdrop.Item)) backdrop.Item.height = backdrop.Height + growth;
                    list.height = contentHeight;
                    // 菜单只在展开态重定位，以开始按钮的实际顶边为底边；不缓存任何关闭态坐标。
                    float startTop = menu.parent.GlobalToLocal(startButton.LocalToGlobal(Vector2.zero)).y;
                    menu.yMin = startTop - menu.height;
                    list.EnsureBoundsCorrect();
                }
                finally { applying = false; }
            }

            private static float AuthoredHeight(GObject item) => item.initHeight > 0 ? item.initHeight : item.sourceHeight > 0 ? item.sourceHeight : item.height;
            private sealed class Backdrop { internal GObject Item; internal float Height; }
        }
    }
}
