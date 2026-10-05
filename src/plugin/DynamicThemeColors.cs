using System.Collections;
using System.Reflection;
using FairyGUI;
using HarmonyLib;
using InfantGod.Core.UI;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal static class DynamicThemeColors
    {
        private static readonly FieldInfo OriginalObjectColors = AccessTools.Field(typeof(UIThemeManager), "originalObjectColors");

        internal static void Update(GObject item, Color sourceColor)
        {
            var theme = UIThemeManager.Instance;
            if (theme == null) return;
            // 原作在列表重绘后按控件首次颜色重新套用主题；复用卡片必须同步当前状态的源颜色。
            ((IDictionary)OriginalObjectColors.GetValue(theme))[item] = sourceColor;
            // 立即使用当前调色板，点击切换与下一次滚动重绘保持同一配色。
            theme.ApplyTo(item);
        }
    }
}
