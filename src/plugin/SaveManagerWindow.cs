using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using FairyGUI;
using InfantGod.Core.SaveSystem;
using InfantGod.Core.OSSystem;
using UnityEngine;

namespace Graywill.InfantGodSaveManager
{
    internal sealed class SaveManagerWindow : IDisposable
    {
        internal const string Prefix = "InfantGodSaveManager_";
        private readonly SaveManagerHost host;
        private GComponent mainMenuOwner;
        private Window window;
        private GComponent pane;
        private GGraph background, divider, accent;
        private GTextField heading, caption, slotCaption, pointCaption, status, selectedInfo;
        private GTextInput nameInput, pointName, pointNote;
        private GList slotsList, pointsList;
        private GButton add, rename, save, load, delete, close, mode;
        private SaveSlot[] slots = Array.Empty<SaveSlot>();
        private List<SavePoint> points = new List<SavePoint>();
        private bool showNative;
        private readonly Dictionary<GLoader, Thumbnail> thumbnails = new Dictionary<GLoader, Thumbnail>();
        private readonly Dictionary<int, int> manualCounts = new Dictionary<int, int>();
        private int selectedSlot, selectedPoint;
        private string confirmAction;
        private int confirmSlot = -1, confirmPoint = -1;
        private float layoutWidth, layoutHeight;
        private static float Unit => GRoot.inst == null ? 1 : Mathf.Clamp(Mathf.Min(GRoot.inst.width / 2560f, GRoot.inst.height / 1440f), 0.65f, 1.6f);
        private static readonly Color Paper = new Color32(229, 225, 181, 255);
        private static readonly Color Ink = new Color32(42, 48, 46, 255);
        private static readonly Color Quiet = new Color32(112, 116, 97, 255);
        private static readonly Color Orange = new Color32(202, 106, 54, 255);

        internal SaveManagerWindow(SaveManagerHost host) { this.host = host; }
        internal bool IsOpen => NativeAccess.Alive(window) && window.isShowing;

        internal void EnsureEntry()
        {
            if (SlotStore.Ready(SlotStore.System)) MenuIntegration.BindExisting();
        }

        internal void Open(bool saving = false, GComponent owner = null)
        {
            if (!NativeAccess.Alive(window)) Create();
            mainMenuOwner = owner;
            selectedSlot = Math.Max(0, SlotStore.System.GetActiveSlotId());
            showNative = !saving && ManualPoints.List(selectedSlot).Count == 0;
            UpdateMode();
            selectedPoint = 0; ResetConfirmation();
            window.Show(); Layout(); Refresh();
            heading.text = saving ? "普通保存" : "载入游戏";
            Status(host.InGame ? "每个手动点独立保存。快速加载 (F9) 读取当前游玩线最近一次普通保存或快速保存。" : "选择游玩线与存档卡片，继续当时的剧情。Esc 返回主菜单。");
        }
        internal void Close()
        {
            if (NativeAccess.Alive(window)) window.Hide();
            if (NativeAccess.Alive(mainMenuOwner))
            {
                var controller = mainMenuOwner.GetController("Status");
                if (controller != null && controller.selectedIndex == 1) controller.selectedIndex = 0;
            }
            mainMenuOwner = null;
            ResetConfirmation();
        }
        internal void HideForCapture()
        {
            if (NativeAccess.Alive(window)) window.Hide();
        }
        internal void ShowAfterCapture()
        {
            if (NativeAccess.Alive(window)) window.Show();
        }
        private void Create()
        {
            window = new Window { name = Prefix + "Window", modal = true, sortingOrder = 30000 };
            pane = new GComponent { name = Prefix + "Panel", opaque = true };
            window.contentPane = pane;
            background = new GGraph { touchable = false }; pane.AddChild(background);
            accent = new GGraph { touchable = false }; pane.AddChild(accent);
            divider = new GGraph { touchable = false }; pane.AddChild(divider);
            heading = Label("存档管理", 22, Paper); pane.AddChild(heading);
            caption = Label("SAVE / LOAD", 14, new Color32(176, 181, 153, 255)); pane.AddChild(caption);
            slotCaption = Label("游玩线 / 存档槽", 16, Ink); pane.AddChild(slotCaption);
            pointCaption = Label("独立手动存档点", 16, Ink); pane.AddChild(pointCaption);
            selectedInfo = Label("", 16, Quiet); pane.AddChild(selectedInfo);
            status = Label("", 14, Quiet); pane.AddChild(status);
            slotsList = CreateNativeList("Slots"); pane.AddChild(slotsList);
            pointsList = CreateNativeList("Points"); pane.AddChild(pointsList);
            slotsList.itemRenderer = RenderSlot; pointsList.itemRenderer = RenderPoint;
            pointsList.layout = ListLayoutType.FlowHorizontal; pointsList.columnGap = 12; pointsList.lineGap = 12;
            slotsList.SetVirtual(); pointsList.SetVirtual();
            slotsList.onClickItem.Add(context =>
            {
                var row = context.data as GObject;
                if (!(row?.data is int id)) return;
                selectedSlot = id; selectedPoint = 0; ResetConfirmation(); Refresh();
            });
            pointsList.onClickItem.Add(context =>
            {
                var row = context.data as GObject;
                if (!(row?.data is int id)) return;
                selectedPoint = id; pointsList.selectedIndex = id;
                ResetConfirmation(); UpdateSelection(); RepaintPointSelection();
            });
            nameInput = Input("游玩线名称"); pane.AddChild(nameInput);
            pointName = Input("存档点名称（可选，留空使用当前剧情标题）"); pane.AddChild(pointName);
            pointNote = Input("备注（可选）"); pane.AddChild(pointNote);
            add = Button("＋ 新增槽", Add); pane.AddChild(add);
            rename = Button("重命名", Rename); pane.AddChild(rename);
            save = Button("新建存档点", Save); pane.AddChild(save);
            load = Button("读取此点", Load); pane.AddChild(load);
            delete = Button("删除此点", Delete); pane.AddChild(delete);
            mode = Button("切换：快存 / 自动", SwitchMode); pane.AddChild(mode);
            close = Button("关闭", Close); pane.AddChild(close);
            layoutWidth = -1;
        }
        private static GList CreateNativeList(string name)
        {
            NativeAccess.LoadPackage("LiveStreamWindow");
            var template = UIPackage.CreateObject("LiveStreamWindow", "DialogueLog").asCom;
            var list = template.GetChild("List_LogItem").asList;
            template.RemoveChild(list); template.Dispose();
            list.name = Prefix + name;
            list.RemoveChildrenToPool(); list.selectionMode = ListSelectionMode.Single;
            list.lineGap = 5;
            return list;
        }

        internal void Layout()
        {
            if (!NativeAccess.Alive(window) || !window.isShowing) return;
            float u = Unit;
            float width = GRoot.inst.width - 80 * u;
            float height = GRoot.inst.height - 80 * u;
            if (width == layoutWidth && height == layoutHeight) { UpdateActions(); return; }
            layoutWidth = width; layoutHeight = height;
            window.SetSize(width, height); pane.SetSize(width, height); window.Center();
            background.DrawRect(width, height, Mathf.RoundToInt(2 * u), Ink, Paper);
            accent.DrawRect(width, 100 * u, 0, Ink, Ink);
            Put(heading, 36 * u, 25 * u, 480 * u, 60 * u);
            Put(caption, width - 480 * u, 38 * u, 250 * u, 40 * u);
            PlaceButton(close, width - 188 * u, 24 * u, 152 * u, 54 * u);
            float left = 440 * u;
            Put(slotCaption, 36 * u, 128 * u, left - 24 * u, 44 * u);
            float rightX = left + 72 * u, rightWidth = width - rightX - 36 * u;
            Put(pointCaption, rightX, 128 * u, rightWidth - 338 * u, 44 * u);
            PlaceButton(mode, width - 342 * u, 120 * u, 306 * u, 52 * u);
            float reserve = host.InGame ? 324 * u : 212 * u;
            Put(slotsList, 30 * u, 192 * u, left, height - 500 * u);
            Put(pointsList, rightX - 6 * u, 192 * u, rightWidth + 6 * u, height - 192 * u - reserve);
            pointsList.columnCount = rightWidth >= 1800 * u ? 3 : rightWidth >= 1050 * u ? 2 : 1;
            pointsList.columnGap = Mathf.RoundToInt(24 * u); pointsList.lineGap = Mathf.RoundToInt(18 * u); slotsList.lineGap = Mathf.RoundToInt(12 * u);
            divider.SetXY(left + 54 * u, 128 * u); divider.DrawRect(u, height - 194 * u, 0, Quiet, Quiet);
            Put(selectedInfo, rightX, height - reserve + 10 * u, rightWidth, 42 * u);
            pointName.visible = pointNote.visible = save.visible = host.InGame;
            Put(pointName, rightX, height - 258 * u, rightWidth, 54 * u);
            Put(pointNote, rightX, height - 190 * u, rightWidth, 54 * u);
            Put(nameInput, 36 * u, height - 258 * u, left - 12 * u, 54 * u);
            PlaceButton(add, 36 * u, height - 190 * u, 208 * u, 54 * u);
            PlaceButton(rename, 260 * u, height - 190 * u, left - 236 * u, 54 * u);
            float y = height - 112 * u;
            PlaceButton(save, rightX, y, 244 * u, 58 * u);
            PlaceButton(load, rightX + (host.InGame ? 270 * u : 0), y, 218 * u, 58 * u);
            PlaceButton(delete, rightX + (host.InGame ? 514 * u : 244 * u), y, 218 * u, 58 * u);
            Put(status, 36 * u, height - 44 * u, width - 72 * u, 40 * u);
            slotsList.RefreshVirtualList(); pointsList.RefreshVirtualList();
            UpdateActions();
        }

        internal void Refresh()
        {
            if (!NativeAccess.Alive(pane)) return;
            var system = SlotStore.System;
            if (!SlotStore.Ready(system)) return;
            // 直接使用已加载的原生槽对象，UI 只建立时间点的轻量索引。
            slots = system.GetAllSlots();
            selectedSlot = Mathf.Clamp(selectedSlot, 0, slots.Length - 1);
            points = SlotStore.Points(slots[selectedSlot]).Where(point => showNative ? point.Manual == null : point.Manual != null).ToList();
            manualCounts.Clear();
            foreach (var slot in slots) manualCounts[slot.slotId] = ManualPoints.List(slot.slotId).Count;
            selectedPoint = points.Count == 0 ? -1 : Mathf.Clamp(selectedPoint, 0, points.Count - 1);
            slotsList.numItems = slots.Length; pointsList.numItems = points.Count;
            slotsList.selectedIndex = selectedSlot;
            pointsList.selectedIndex = selectedPoint;
            slotsList.RefreshVirtualList(); pointsList.RefreshVirtualList();
            RepaintSlotSelection();
            nameInput.text = DisplayName(slots[selectedSlot]);
            UpdateSelection(); UpdateActions();
        }

        private void UpdateSelection()
        {
            var slot = slots.ElementAtOrDefault(selectedSlot);
            selectedInfo.text = slot == null ? "" : $"槽位 {selectedSlot + 1:00}  ·  {DisplayName(slot)}" +
                (slot.isActive ? "  ·  当前游玩线" : "") + (selectedPoint >= 0 && selectedPoint < points.Count ? $"  /  Day {points[selectedPoint].Day}" : "  /  尚无记录");
        }
        private void UpdateActions()
        {
            if (!NativeAccess.Alive(save)) return;
            save.enabled = host.InGame && !host.Working;
            bool hasSelection = selectedPoint >= 0 && selectedPoint < points.Count;
            load.enabled = hasSelection && !host.Working;
            delete.enabled = !showNative && hasSelection && !host.Working;
            add.enabled = rename.enabled = !host.Working;
        }
        internal void Status(string message) { if (NativeAccess.Alive(status)) status.text = message; }
        private void Add()
        {
            Run(() => { selectedSlot = SlotStore.Add(SlotStore.System); selectedPoint = -1; ResetConfirmation(); Refresh(); slotsList.ScrollToView(selectedSlot); Status("已新增空槽，可以保存当前进度。现有存档保持不变。"); });
        }
        private void Rename()
        {
            if (string.IsNullOrWhiteSpace(nameInput.text)) { Status("请输入存档槽名称。"); return; }
            Run(() => { SlotStore.Rename(SlotStore.System, selectedSlot, nameInput.text); ResetConfirmation(); Refresh(); Status("槽位名称已保存。"); });
        }
        private void Save()
        {
            showNative = false; UpdateMode();
            ResetConfirmation(); host.Save(selectedSlot, pointName.text, pointNote.text);
        }
        private void Load()
        {
            if (selectedPoint < 0 || selectedPoint >= points.Count) return;
            if (host.InGame && !Confirm("load", "读取后将离开当前未保存的进度。再次点击“读取此点”确认。")) return;
            var point = points[selectedPoint]; ResetConfirmation(); host.Load(selectedSlot, point);
        }
        private void Delete()
        {
            if (selectedPoint < 0 || selectedPoint >= points.Count || points[selectedPoint].Manual == null) return;
            if (!Confirm("delete", "只删除所选手动点及其缩略图。再次点击“删除此点”确认。")) return;
            Run(() => { ManualPoints.Delete(points[selectedPoint].Manual); ResetConfirmation(); Refresh(); Status("所选手动点已删除，后续分支保留原来源标记。"); });
        }
        private void SwitchMode() { showNative = !showNative; selectedPoint = 0; ResetConfirmation(); UpdateMode(); Refresh(); }
        private void UpdateMode()
        {
            pointCaption.text = showNative ? "快速存档 / 原生自动记录" : "独立手动存档点";
            mode.title = showNative ? "切换：手动存档点" : "切换：快存 / 自动";
        }
        private bool Confirm(string action, string text)
        {
            if (confirmAction == action && confirmSlot == selectedSlot && confirmPoint == selectedPoint) return true;
            confirmAction = action; confirmSlot = selectedSlot; confirmPoint = selectedPoint; Status(text); return false;
        }
        private void ResetConfirmation() { confirmAction = null; confirmSlot = confirmPoint = -1; }
        private void Run(Action action)
        {
            try { action(); }
            catch (Exception exception) { Plugin.Log.LogError(exception); Status("操作失败：" + exception.Message); }
        }
        private static string DisplayName(SaveSlot slot)
        {
            // 原版默认名字有编码错误，只修正显示，不重写已有文件中的用户命名。
            if (string.IsNullOrWhiteSpace(slot.slotName) || slot.slotName.StartsWith("瀛樻", StringComparison.Ordinal)) return "存档 " + (slot.slotId + 1);
            return slot.slotName;
        }
        private void RenderSlot(int index, GObject value)
        {
            var slot = slots[index];
            manualCounts.TryGetValue(slot.slotId, out int count);
            string subtitle = count + " 个手动点" + (slot.currentDay > 0 ? $"  ·  Day {slot.currentDay}" : "") + (SlotStore.HasQuickSave(slot) ? "  ·  有快存" : "");
            RenderRow(value, index, slotsList.width - 22, $"{index + 1:00}   {DisplayName(slot)}" + (slot.isActive ? "  ●" : ""), subtitle, index == selectedSlot);
        }
        private void RenderPoint(int index, GObject value)
        {
            var point = points[index];
            int columns = Math.Max(1, pointsList.columnCount);
            float width = (pointsList.width - 32 * Unit - (columns - 1) * 24 * Unit) / columns;
            // 虚拟列表重绘期间 selectedIndex 可暂时变为 -1，业务选档始终使用已确认的索引。
            RenderCard(value, index, width, point, index == selectedPoint);
        }
        private void RepaintPointSelection()
        {
            // 原生列表先改变选择，立即同步可见卡片，避免虚拟列表延后刷新留下旧底色。
            foreach (var child in pointsList.GetChildren())
                if (child.data is int index && index >= 0 && index < points.Count) RenderPoint(index, child);
        }
        private void RepaintSlotSelection()
        {
            // 切换游玩线时同步旧、新可见槽的底色，不等待虚拟列表下一次刷新。
            foreach (var child in slotsList.GetChildren())
                if (child.data is int index && index >= 0 && index < slots.Length) RenderSlot(index, child);
        }
        private void RenderCard(GObject value, int index, float width, SavePoint point, bool selected)
        {
            float u = Unit;
            var row = value.asCom; row.data = index;
            if (value is GButton nativeButton)
            {
                nativeButton.title = point.Title;
                nativeButton.changeStateOnClick = false;
                nativeButton.selected = selected;
            }
            foreach (var child in row.GetChildren()) child.visible = false;
            var fill = row.GetChild(Prefix + "CardFill") as GGraph;
            if (fill == null) { fill = new GGraph { name = Prefix + "CardFill", touchable = false }; row.AddChildAt(fill, 0); }
            fill.visible = true; fill.DrawRect(width, 350 * u, Mathf.RoundToInt(2 * u), selected ? Orange : Quiet, selected ? Ink : new Color32(218, 216, 175, 255));
            DynamicThemeColors.Update(fill, selected ? Ink : new Color32(218, 216, 175, 255));
            var title = CardLabel(row, "CardTitle", 17, selected ? Paper : Ink);
            var detail = CardLabel(row, "CardDetail", 13, selected ? Paper : Quiet);
            var branch = CardLabel(row, "CardBranch", 14, selected ? Paper : Ink);
            var trail = CardLabel(row, "CardTrail", 13, selected ? new Color32(196, 200, 162, 255) : Quiet);
            title.text = point.Title; detail.text = $"Day {point.Day:00}  ·  {point.Detail}";
            Put(title, 20 * u, 16 * u, width - 40 * u, 44 * u); Put(detail, 20 * u, 302 * u, width - 40 * u, 36 * u);
            float imageWidth = Math.Min(300 * u, width * 0.43f), contentX = imageWidth + 40 * u;
            var loader = row.GetChild(Prefix + "Thumbnail") as GLoader;
            if (loader == null) { loader = new GLoader { name = Prefix + "Thumbnail", touchable = false, fill = FillType.Scale, align = AlignType.Center, verticalAlign = VertAlignType.Middle }; row.AddChild(loader); }
            loader.visible = point.Manual != null;
            Put(loader, 20 * u, 76 * u, imageWidth, 176 * u);
            BindThumbnail(loader, point.Manual);
            if (point.Manual != null)
            {
                var record = point.Manual;
                branch.singleLine = false;
                branch.text = record.branch?.title ?? "当前剧情片段";
                if (!string.IsNullOrWhiteSpace(record.branch?.line)) branch.text += "\n" + (string.IsNullOrEmpty(record.branch.speaker) ? "" : record.branch.speaker + "：") + record.branch.line;
                if (!string.IsNullOrWhiteSpace(record.note)) branch.text += "\n备注：" + record.note;
                string choice = string.IsNullOrWhiteSpace(record.branch?.lastChoice) ? "" : "最近选择：" + record.branch.lastChoice;
                string parent = string.IsNullOrEmpty(record.parentPointId) ? "" : "来源：" + record.parentPointTitle + (record.parentPointDeleted ? "（已删除）" : "");
                trail.text = string.Join("\n", new[] { choice, parent }.Where(text => !string.IsNullOrWhiteSpace(text)));
                row.tooltips = record.title + "\n" + branch.text + "\n" + trail.text + "\n" + detail.text;
            }
            else
            {
                contentX = 20 * u; branch.singleLine = false;
                branch.text = point.Quick ? "原生 F5 快速存档\n保留独立的快速存档位置。" : "原生自动记录\n沿用游戏原本的章节或连线恢复位置。";
                trail.text = "";
                row.tooltips = branch.text + "\n" + detail.text;
            }
            trail.singleLine = false;
            Put(branch, contentX, 76 * u, width - contentX - 20 * u, 138 * u);
            Put(trail, contentX, 226 * u, width - contentX - 20 * u, 68 * u);
            row.SetSize(width, 350 * u);
        }
        private static GTextField CardLabel(GComponent row, string name, int size, Color color)
        {
            var label = row.GetChild(Prefix + name) as GTextField;
            if (label == null) { label = Label("", size, color); label.name = Prefix + name; row.AddChild(label); }
            label.visible = true; var format = label.textFormat; format.color = color; format.size = Mathf.RoundToInt(size * 1.8f * Unit); label.textFormat = format;
            DynamicThemeColors.Update(label, color);
            return label;
        }
        private void BindThumbnail(GLoader loader, ManualPoint point)
        {
            string path = point == null ? null : ManualPoints.ThumbnailPath(point);
            thumbnails.TryGetValue(loader, out var previous);
            if (previous?.Path == path) return;
            loader.texture = null; previous?.Dispose(); thumbnails.Remove(loader);
            if (path == null || !File.Exists(path)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(texture); return; }
                var image = new NTexture(texture); loader.texture = image;
                thumbnails[loader] = new Thumbnail { Path = path, Texture = texture, Image = image };
            }
            catch (Exception exception) { UnityEngine.Object.Destroy(texture); Plugin.Log.LogWarning("缩略图无法加载：" + exception.Message); }
        }
        private sealed class Thumbnail : IDisposable
        {
            internal string Path; internal Texture2D Texture; internal NTexture Image;
            public void Dispose() { Image?.Dispose(); if (Texture != null) UnityEngine.Object.Destroy(Texture); }
        }
        private static void RenderRow(GObject value, int index, float width, string title, string detail, bool selected)
        {
            float u = Unit;
            var row = value.asCom; row.data = index;
            if (value is GButton nativeButton)
            {
                nativeButton.title = title;
                nativeButton.changeStateOnClick = false;
                nativeButton.selected = selected;
            }
            foreach (var child in row.GetChildren()) child.visible = false;
            var fill = row.GetChild(Prefix + "Fill") as GGraph;
            if (fill == null) { fill = new GGraph { name = Prefix + "Fill", touchable = false }; row.AddChildAt(fill, 0); }
            fill.visible = true; fill.DrawRect(width, 122 * u, Mathf.RoundToInt(2 * u), selected ? Orange : Quiet, selected ? Ink : new Color32(218, 216, 175, 255));
            DynamicThemeColors.Update(fill, selected ? Ink : new Color32(218, 216, 175, 255));
            var first = row.GetChild(Prefix + "Title") as GTextField;
            if (first == null) { first = Label("", 17, Ink); first.name = Prefix + "Title"; row.AddChild(first); }
            var second = row.GetChild(Prefix + "Detail") as GTextField;
            if (second == null) { second = Label("", 14, Quiet); second.name = Prefix + "Detail"; row.AddChild(second); }
            first.visible = second.visible = true;
            first.text = title; second.text = detail;
            var format = first.textFormat; format.color = selected ? Paper : Ink; first.textFormat = format;
            format = second.textFormat; format.color = selected ? new Color32(195, 199, 159, 255) : Quiet; second.textFormat = format;
            DynamicThemeColors.Update(first, selected ? Paper : Ink);
            DynamicThemeColors.Update(second, selected ? new Color32(195, 199, 159, 255) : Quiet);
            Put(first, 20 * u, 16 * u, width - 40 * u, 44 * u); Put(second, 20 * u, 72 * u, width - 40 * u, 38 * u);
            row.SetSize(width, 122 * u);
        }
        private static GTextField Label(string text, int size, Color color)
        {
            var label = new GTextField { autoSize = AutoSizeType.None, text = text, touchable = false, singleLine = true };
            var format = label.textFormat; format.font = UIConfig.defaultFont; format.size = Mathf.RoundToInt(size * 1.8f * Unit); format.color = color; label.textFormat = format;
            return label;
        }
        private static GTextInput Input(string prompt)
        {
            var input = new GTextInput { name = Prefix + "Input", promptText = prompt, singleLine = true, border = 1, borderColor = Quiet };
            var format = input.textFormat; format.font = UIConfig.defaultFont; format.size = Mathf.RoundToInt(27 * Unit); format.color = Ink; input.textFormat = format;
            input.inputTextField.backgroundColor = new Color32(242, 236, 199, 255);
            return input;
        }
        private static GButton Button(string text, Action clicked)
        {
            return NativeButtons.Create(text, Mathf.RoundToInt(27 * Unit), clicked);
        }
        private static void Put(GObject value, float x, float y, float width, float height) { value.SetXY(x, y); value.SetSize(width, height); }
        private static void PlaceButton(GButton button, float x, float y, float width, float height)
        {
            NativeButtons.Place(button, x, y, width, height);
        }
        public void Dispose()
        {
            if (NativeAccess.Alive(window)) window.Dispose(); window = null; pane = null;
            foreach (var thumbnail in thumbnails.Values) thumbnail.Dispose(); thumbnails.Clear();
            mainMenuOwner = null; slots = Array.Empty<SaveSlot>(); points.Clear();
        }
    }
}
