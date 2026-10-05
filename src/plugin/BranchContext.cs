using System;
using System.Collections.Generic;
using System.IO;
using InfantGod.Core.DialogueSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Yarn.Unity;

namespace Graywill.InfantGodSaveManager
{
    [Serializable]
    internal sealed class BranchRecord
    {
        public string node, title, speaker, line, lineId, lastChoice, choiceNode;
    }

    internal sealed class BranchContext : IDisposable
    {
        private DialogueLogicManager bound;
        private static Dictionary<string, string> labels;
        private static BranchRecord current = new BranchRecord();

        internal void Bind()
        {
            var manager = UnityEngine.Object.FindObjectOfType<DialogueLogicManager>();
            if (manager == bound) return;
            Unbind(); bound = manager;
            if (bound == null) return;
            bound.OnLineStarted += LineStarted;
            bound.OnOptionSelected += OptionSelected;
        }
        private void LineStarted(LocalizedLine line)
        {
            current.line = line?.TextWithoutCharacterName.Text ?? "";
            current.lineId = line?.TextID ?? "";
            current.speaker = line?.CharacterName ?? "";
            current.node = bound?.CurrentNode;
        }
        private void OptionSelected(DialogueOption option)
        {
            current.lastChoice = option?.Line?.TextWithoutCharacterName.Text ?? "";
            current.choiceNode = bound?.CurrentNode;
        }
        internal static BranchRecord Capture(string actualNode)
        {
            var manager = UnityEngine.Object.FindObjectOfType<DialogueLogicManager>();
            string node = actualNode ?? manager?.CurrentNode ?? "";
            // 上一条台词可能属于已经结束的对话，仅在当前 line ID 一致时显示它。
            bool sameLine = manager != null && manager.CurrentLineID == current.lineId;
            return new BranchRecord
            {
                node = node, title = Title(node), speaker = sameLine ? current.speaker : "",
                line = sameLine ? current.line : "", lineId = sameLine ? current.lineId : "",
                lastChoice = current.lastChoice, choiceNode = current.choiceNode
            };
        }
        internal static void Restore(BranchRecord saved) => current = saved ?? new BranchRecord();
        internal static string Title(string node)
        {
            if (string.IsNullOrEmpty(node)) return "对话外 · 当前游戏状态";
            return TryTitle(node, out var title) ? title : "当前剧情片段";
        }
        internal static bool TryTitle(string node, out string title)
        {
            title = null;
            if (string.IsNullOrEmpty(node)) return false;
            if (labels == null)
            {
                var path = Path.Combine(Plugin.DataDirectory, "scene-labels.json");
                try { labels = File.Exists(path) ? JObject.Parse(File.ReadAllText(path))["nodes"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>() : new Dictionary<string, string>(); }
                catch (Exception exception) { labels = new Dictionary<string, string>(); Plugin.Log.LogWarning("剧情标题映射未加载：" + exception.Message); }
            }
            if (labels.TryGetValue(node, out title)) return true;
            int suffix = node.IndexOf(".Auto_", StringComparison.Ordinal);
            return suffix > 0 && labels.TryGetValue(node.Substring(0, suffix), out title);
        }
        private void Unbind()
        {
            if (bound != null) { bound.OnLineStarted -= LineStarted; bound.OnOptionSelected -= OptionSelected; }
            bound = null;
        }
        public void Dispose() => Unbind();
    }
}
