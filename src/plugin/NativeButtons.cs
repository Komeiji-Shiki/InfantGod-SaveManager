using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FairyGUI;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal static class NativeButtons
    {
        internal const string Name = SaveManagerWindow.Prefix + "NativeButton";
        private static readonly ConditionalWeakTable<GButton, LayoutNode> Layouts = new ConditionalWeakTable<GButton, LayoutNode>();

        internal static GButton Create(string title, int fontSize, Action clicked)
        {
            NativeAccess.LoadPackage("MainMenu");
            var button = UIPackage.CreateObject("MainMenu", "Btn_MainMenu").asButton;
            button.name = Name;
            button.title = title;
            button.titleFontSize = fontSize;
            Layouts.Add(button, new LayoutNode(button));
            // 原生控制器负责普通、悬停和按下状态；这里只添加本按钮的操作。
            button.onClick.Add(context => { context.StopPropagation(); clicked(); });
            return button;
        }

        internal static void Place(GButton button, float x, float y, float width, float height)
        {
            button.SetXY(x, y);
            if (Layouts.TryGetValue(button, out var layout)) layout.Resize(width, height);
            else button.SetSize(width, height);
        }

        private sealed class LayoutNode
        {
            private readonly GObject item;
            private readonly float x, y, width, height;
            private readonly List<LayoutNode> children = new List<LayoutNode>();

            internal LayoutNode(GObject value)
            {
                item = value; x = value.x; y = value.y; width = value.width; height = value.height;
                if (value is GComponent component)
                    foreach (var child in component.GetChildren()) children.Add(new LayoutNode(child));
            }

            internal void Resize(float targetWidth, float targetHeight)
            {
                item.SetSize(targetWidth, targetHeight, true);
                float horizontal = width > 0 ? targetWidth / width : 1;
                float vertical = height > 0 ? targetHeight / height : 1;
                // 原按钮有未建立尺寸关系的内部边框，按资源原始几何同步布局，避免只缩外框。
                foreach (var child in children)
                {
                    child.item.SetXY(child.x * horizontal, child.y * vertical);
                    child.Resize(child.width * horizontal, child.height * vertical);
                }
            }
        }
    }
}
