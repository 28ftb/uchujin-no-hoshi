using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Quests
{
    /// <summary>
    /// クエストの3D表示（プリミティブ）：掲示板、自発的なお手伝いの状況（荷物・落とし物・迷子・洗濯物）、
    /// 暮らしに残った変化（花・作品・模型）、行事の飾り（風船・屋台・灯り）。
    /// 状態から作り直すので、ゲストで RestoreState がプレイ中に呼ばれても冪等。
    /// </summary>
    internal sealed class QuestSceneView
    {
        private readonly QuestSystem sys;
        private GameObject root;
        private GameObject board;
        private readonly List<GameObject> paperObjs = new List<GameObject>();
        private readonly Dictionary<string, GameObject> helpObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, string> helpSigs = new Dictionary<string, string>();
        private readonly Dictionary<string, GameObject> markObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, GameObject> eventObjs = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, string> eventSigs = new Dictionary<string, string>();
        private string boardSig;
        private float time;

        private static readonly Color Brown = new Color(0.5f, 0.34f, 0.22f);

        public QuestSceneView(QuestSystem sys)
        {
            this.sys = sys;
        }

        public void Build()
        {
            var parent = Game.Instance != null ? Game.Instance.SceneRoot : null;
            root = new GameObject("Quests");
            if (parent != null) root.transform.SetParent(parent, false);
            BuildBoard();
            Refresh();
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            board = null;
            paperObjs.Clear();
            helpObjs.Clear();
            helpSigs.Clear();
            markObjs.Clear();
            eventObjs.Clear();
            eventSigs.Clear();
            boardSig = null;
        }

        public void Refresh()
        {
            if (root == null) return;
            RefreshPapers();
            RefreshHelps();
            RefreshMarks();
            RefreshEvents();
        }

        // ------------------------------------------------------------
        // 掲示板（§6.2）
        // ------------------------------------------------------------

        private void BuildBoard()
        {
            var pos = sys.BoardPosition();
            board = PrimitiveBuilder.Empty("BulletinBoard", root.transform, pos);
            var plaza = sys.PlazaPos();
            var look = plaza - pos;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) board.transform.rotation = Quaternion.LookRotation(look.normalized);
            PrimitiveBuilder.Part(board.transform, PrimitiveType.Cylinder, new Vector3(-1.1f, 0.9f, 0f), new Vector3(0.12f, 0.9f, 0.12f), QuestTuning.BoardWood, "PostL");
            PrimitiveBuilder.Part(board.transform, PrimitiveType.Cylinder, new Vector3(1.1f, 0.9f, 0f), new Vector3(0.12f, 0.9f, 0.12f), QuestTuning.BoardWood, "PostR");
            var panel = PrimitiveBuilder.Create(PrimitiveType.Cube, board.transform, new Vector3(0f, 1.5f, 0f), new Vector3(2.4f, 1.3f, 0.12f), new Color(0.7f, 0.55f, 0.38f), "Panel");
            PrimitiveBuilder.Part(board.transform, PrimitiveType.Cube, new Vector3(0f, 2.25f, 0f), new Vector3(2.7f, 0.12f, 0.4f), new Color(0.45f, 0.3f, 0.2f), "Roof");
            SimpleInteractable.Attach(panel, "掲示板を見る", "Look at the board", p => UIManager.Open(new BulletinBoardWindow(sys)));
        }

        private void RefreshPapers()
        {
            if (board == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var p in sys.S.board) sb.Append(p.id).Append(p.isNotice ? "n" : "r").Append(string.IsNullOrEmpty(p.takenBy) ? "" : "t");
            string sig = sb.ToString();
            if (sig == boardSig) return;
            boardSig = sig;
            foreach (var o in paperObjs) if (o != null) Object.Destroy(o);
            paperObjs.Clear();
            int i = 0;
            foreach (var p in sys.S.board)
            {
                if (!string.IsNullOrEmpty(p.takenBy) || i >= 8) continue;
                int col = i % 4, row = i / 4;
                var pos = new Vector3(-0.85f + col * 0.57f, 1.8f - row * 0.6f, -0.08f);
                var paperColor = p.isNotice ? new Color(1f, 0.85f, 0.8f) : QuestTuning.PaperColor;
                var go = PrimitiveBuilder.Part(board.transform, PrimitiveType.Cube, pos, new Vector3(0.45f, 0.5f, 0.02f), paperColor, "Paper");
                var r = sys.R(p.residentId);
                // 依頼主の顔（色）の小さな丸
                PrimitiveBuilder.Part(go.transform, PrimitiveType.Sphere, new Vector3(0.3f, 0.32f, -0.8f), new Vector3(0.25f, 0.22f, 1.5f), r != null ? r.MainColor : Color.gray, "Face");
                if (p.drewPicture && !string.IsNullOrEmpty(p.pictureItemId))
                {
                    var def = ItemRegistry.GetOrPlaceholder(p.pictureItemId);
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Sphere, new Vector3(0f, -0.15f, -0.8f), new Vector3(0.35f, 0.3f, 1.5f), def.Color, "Picture");
                }
                paperObjs.Add(go);
                i++;
            }
        }

        // ------------------------------------------------------------
        // 自発的なお手伝い（§7）
        // ------------------------------------------------------------

        private void RefreshHelps()
        {
            var alive = new HashSet<string>();
            string me = sys.LocalId;
            foreach (var h in sys.S.helps)
            {
                bool visible = !h.done && !(h.kind == HelpKind.LostItem && !string.IsNullOrEmpty(h.claimedBy));
                if (!visible) continue;
                alive.Add(h.id);
                string sig = h.kind + "|" + h.claimedBy + "|" + h.position;
                if (helpObjs.TryGetValue(h.id, out var existing) && existing != null && helpSigs.TryGetValue(h.id, out var old) && old == sig)
                {
                    bool otherHolds = !string.IsNullOrEmpty(h.claimedBy) && h.claimedBy != me && (h.kind == HelpKind.CarryLoad || h.kind == HelpKind.LostCreature);
                    existing.SetActive(!otherHolds);
                    continue;
                }
                if (existing != null) Object.Destroy(existing);
                var go = BuildHelp(h);
                helpObjs[h.id] = go;
                helpSigs[h.id] = sig;
            }
            var remove = new List<string>();
            foreach (var kv in helpObjs) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (helpObjs[id] != null) Object.Destroy(helpObjs[id]);
                helpObjs.Remove(id);
                helpSigs.Remove(id);
            }
        }

        private GameObject BuildHelp(HelpEvent h)
        {
            var go = PrimitiveBuilder.Empty("Help_" + h.kind, root.transform, sys.Ground(h.position));
            string id = h.id;
            switch (h.kind)
            {
                case HelpKind.CarryLoad:
                {
                    var box = PrimitiveBuilder.Create(PrimitiveType.Cube, go.transform, new Vector3(0f, 0.4f, 0f), new Vector3(0.8f, 0.7f, 0.8f), h.color, "Box");
                    PrimitiveBuilder.Part(box.transform, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(1.02f, 0.12f, 1.02f), h.color * 0.75f, "Band");
                    var si = SimpleInteractable.Attach(box, "荷物を持つ", "Pick up the box", p => sys.InteractHelp(sys.FindHelp(id), p));
                    si.canInteract = p =>
                    {
                        var x = sys.FindHelp(id);
                        return x != null && !x.done && (string.IsNullOrEmpty(x.claimedBy) || x.claimedBy == p.PlayerId);
                    };
                    // 荷物を置く場所（家の前の敷物／村の外れの荷車）。言葉なしで行き先が分かる
                    var dest = PrimitiveBuilder.Empty("Destination", go.transform, Vector3.zero);
                    dest.transform.position = sys.Ground(h.target);
                    if (h.reasonTag == "move_out")
                    {
                        PrimitiveBuilder.Part(dest.transform, PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 0.5f, 1f), new Color(0.6f, 0.45f, 0.3f), "Cart");
                        PrimitiveBuilder.Part(dest.transform, PrimitiveType.Cylinder, new Vector3(-0.6f, 0.3f, 0.55f), new Vector3(0.6f, 0.05f, 0.6f), Brown, "WheelL", new Vector3(90f, 0f, 0f));
                        PrimitiveBuilder.Part(dest.transform, PrimitiveType.Cylinder, new Vector3(0.6f, 0.3f, 0.55f), new Vector3(0.6f, 0.05f, 0.6f), Brown, "WheelR", new Vector3(90f, 0f, 0f));
                    }
                    else
                    {
                        PrimitiveBuilder.Part(dest.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.02f, 0f), new Vector3(1.6f, 0.02f, 1.6f), new Color(0.85f, 0.75f, 0.55f), "Mat");
                    }
                    break;
                }
                case HelpKind.LostItem:
                {
                    var def = ItemRegistry.GetOrPlaceholder(h.itemId);
                    var item = PrimitiveBuilder.Create(def.Shape == PrimitiveType.Quad ? PrimitiveType.Cube : def.Shape, go.transform, new Vector3(0f, 0.2f, 0f), Vector3.one * 0.3f, def.Color, "Lost");
                    PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Sphere, new Vector3(0f, 0.55f, 0f), Vector3.one * 0.08f, new Color(1f, 1f, 0.8f), "Sparkle");
                    SimpleInteractable.Attach(item, "拾う", "Pick up", p => sys.InteractHelp(sys.FindHelp(id), p));
                    break;
                }
                case HelpKind.LostCreature:
                {
                    var body = PrimitiveBuilder.Create(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0.35f, 0f), new Vector3(0.6f, 0.5f, 0.6f), h.color, "Creature");
                    PrimitiveBuilder.Part(body.transform, PrimitiveType.Sphere, new Vector3(-0.18f, 0.12f, 0.42f), Vector3.one * 0.14f, Color.black, "EyeL");
                    PrimitiveBuilder.Part(body.transform, PrimitiveType.Sphere, new Vector3(0.18f, 0.12f, 0.42f), Vector3.one * 0.14f, Color.black, "EyeR");
                    PrimitiveBuilder.Part(body.transform, PrimitiveType.Sphere, new Vector3(0f, -0.1f, 0.45f), new Vector3(0.12f, 0.06f, 0.06f), new Color(0.9f, 0.4f, 0.5f), "Mouth");
                    var si = SimpleInteractable.Attach(body, "そっと近づく", "Approach gently", p => sys.InteractHelp(sys.FindHelp(id), p));
                    si.canInteract = p =>
                    {
                        var x = sys.FindHelp(id);
                        return x != null && !x.done && string.IsNullOrEmpty(x.claimedBy);
                    };
                    break;
                }
                case HelpKind.Laundry:
                {
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(-1f, 0.8f, 0f), new Vector3(0.08f, 0.8f, 0.08f), QuestTuning.BoardWood, "PoleL");
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(1f, 0.8f, 0f), new Vector3(0.08f, 0.8f, 0.08f), QuestTuning.BoardWood, "PoleR");
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 1.55f, 0f), new Vector3(2f, 0.02f, 0.02f), Color.white, "Line");
                    var cloth = PrimitiveBuilder.Create(PrimitiveType.Cube, go.transform, new Vector3(-0.5f, 1.25f, 0f), new Vector3(0.45f, 0.55f, 0.05f), h.color, "Cloth1");
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0.1f, 1.3f, 0f), new Vector3(0.4f, 0.45f, 0.05f), Color.white, "Cloth2");
                    PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0.6f, 1.22f, 0f), new Vector3(0.35f, 0.6f, 0.05f), Color.Lerp(h.color, Color.white, 0.5f), "Cloth3");
                    PrimitiveBuilder.AddBoxCollider(go, new Vector3(0f, 1.2f, 0f), new Vector3(2.2f, 1.2f, 0.6f), true);
                    SimpleInteractable.Attach(go, "洗濯物を取り込む", "Take in the laundry", p => sys.InteractHelp(sys.FindHelp(id), p));
                    PrimitiveBuilder.RemoveCollider(cloth);
                    break;
                }
            }
            return go;
        }

        // ------------------------------------------------------------
        // 暮らしに残った変化（§8.2）
        // ------------------------------------------------------------

        private void RefreshMarks()
        {
            var alive = new HashSet<string>();
            foreach (var m in sys.S.marks)
            {
                alive.Add(m.id);
                if (markObjs.TryGetValue(m.id, out var go) && go != null) continue;
                markObjs[m.id] = BuildMark(m);
            }
            var remove = new List<string>();
            foreach (var kv in markObjs) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (markObjs[id] != null) Object.Destroy(markObjs[id]);
                markObjs.Remove(id);
            }
        }

        private GameObject BuildMark(LifeMark m)
        {
            var go = PrimitiveBuilder.Empty("LifeMark_" + m.kind, root.transform, sys.Ground(m.position));
            var t = go.transform;
            var c = m.color;
            switch (m.kind)
            {
                case "flowerpot":
                case "blue_flowers":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(0.45f, 0.2f, 0.45f), new Color(0.7f, 0.4f, 0.25f), "Pot");
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.57f;
                        var p = new Vector3(Mathf.Cos(a) * 0.12f, 0.6f + (i % 2) * 0.08f, Mathf.Sin(a) * 0.12f);
                        PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, p - new Vector3(0f, 0.15f, 0f), new Vector3(0.03f, 0.15f, 0.03f), new Color(0.3f, 0.7f, 0.3f), "Stem");
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, p + new Vector3(0f, 0.05f, 0f), Vector3.one * 0.16f, c, "Bloom");
                    }
                    break;
                case "forge_work":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 0.35f), new Color(0.3f, 0.3f, 0.33f), "Stand");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.62f, 0f), new Vector3(0.5f, 0.08f, 0.12f), new Color(0.7f, 0.7f, 0.75f), "Blade");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0.25f, 0.62f, 0f), Vector3.one * 0.12f, c, "Gem");
                    break;
                case "dish":
                case "food_stall":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.08f, 0.6f), new Color(0.75f, 0.55f, 0.35f), "Table");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f), new Vector3(0.1f, 0.4f, 0.1f), Brown, "Leg");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.46f, 0f), new Vector3(0.35f, 0.02f, 0.35f), Color.white, "Plate");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0f, 0.52f, 0f), new Vector3(0.22f, 0.12f, 0.22f), c, "Food");
                    if (m.kind == "food_stall") PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0f), new Vector3(1.1f, 0.06f, 0.8f), new Color(1f, 0.6f, 0.4f), "Awning");
                    break;
                case "crop_row":
                    for (int i = 0; i < 3; i++)
                    {
                        PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(i * 0.4f - 0.4f, 0.05f, 0f), new Vector3(0.35f, 0.1f, 0.35f), new Color(0.45f, 0.32f, 0.22f), "Soil");
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(i * 0.4f - 0.4f, 0.25f, 0f), Vector3.one * 0.2f, c, "Crop");
                    }
                    break;
                case "fish_trophy":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 0.5f, 0.06f), new Color(0.6f, 0.45f, 0.3f), "Plaque");
                    PrimitiveBuilder.Part(t, PrimitiveType.Capsule, new Vector3(0f, 0.9f, -0.06f), new Vector3(0.18f, 0.32f, 0.08f), c, "Fish", new Vector3(0f, 0f, 90f));
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.32f, 0f), new Vector3(0.06f, 0.32f, 0.06f), Brown, "Pole");
                    break;
                case "pet":
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0f, 0.25f, 0f), new Vector3(0.45f, 0.38f, 0.45f), Color.HSVToRGB(StableHash.Unit(sys.Seed, m.id), 0.45f, 0.95f), "Pet");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.7f, 0.03f, 0.7f), new Color(0.9f, 0.8f, 0.6f), "Bed");
                    break;
                case "rocket_model":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.3f, 0.6f), new Color(0.5f, 0.45f, 0.4f), "Pedestal");
                    PrimitiveBuilder.Part(t, PrimitiveType.Capsule, new Vector3(0f, 1.05f, 0f), new Vector3(0.3f, 0.45f, 0.3f), c, "Body");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0f, 1.2f, 0.14f), Vector3.one * 0.1f, new Color(0.5f, 0.85f, 1f), "Window");
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 2.094f;
                        PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 0.16f, 0.72f, Mathf.Sin(a) * 0.16f), new Vector3(0.04f, 0.2f, 0.14f), new Color(0.9f, 0.4f, 0.35f), "Fin", new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
                    }
                    break;
                case "gadget":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(0.5f, 0.5f, 0.5f), new Color(0.55f, 0.6f, 0.65f), "Case");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 0.15f, 0.12f), c, "Antenna");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0f, 0.8f, 0f), Vector3.one * 0.1f, new Color(1f, 0.4f, 0.3f), "Light");
                    break;
                default: // furniture / unpacked
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.4f), c, "Item");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.68f, 0f), new Vector3(0.4f, 0.16f, 0.3f), Color.Lerp(c, Color.white, 0.4f), "Top");
                    break;
            }
            return go;
        }

        // ------------------------------------------------------------
        // 行事の飾り（§13）
        // ------------------------------------------------------------

        private void RefreshEvents()
        {
            var alive = new HashSet<string>();
            int today = sys.Today;
            foreach (var ev in sys.S.events)
            {
                bool show = ev.day == today && ev.started && !ev.finished && !ev.cancelled;
                if (!show) continue;
                alive.Add(ev.id);
                string sig = ev.kind + "|" + ev.stalls.Count;
                if (eventObjs.TryGetValue(ev.id, out var go) && go != null && eventSigs.TryGetValue(ev.id, out var old) && old == sig) continue;
                if (go != null) Object.Destroy(go);
                eventObjs[ev.id] = BuildEvent(ev);
                eventSigs[ev.id] = sig;
            }
            var remove = new List<string>();
            foreach (var kv in eventObjs) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (eventObjs[id] != null) Object.Destroy(eventObjs[id]);
                eventObjs.Remove(id);
                eventSigs.Remove(id);
            }
        }

        private GameObject BuildEvent(VillageEvent ev)
        {
            var go = PrimitiveBuilder.Empty("Event_" + ev.kind, root.transform, sys.Ground(ev.position));
            var t = go.transform;
            switch (ev.kind)
            {
                case VillageEventKind.Birthday:
                case VillageEventKind.WelcomeParty:
                {
                    var cols = new[] { new Color(1f, 0.5f, 0.6f), new Color(0.5f, 0.8f, 1f), new Color(1f, 0.9f, 0.4f), new Color(0.6f, 1f, 0.6f) };
                    for (int i = 0; i < 4; i++)
                    {
                        var p = new Vector3(-1.5f + i, 0f, 1.5f);
                        PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, p + new Vector3(0f, 1f, 0f), new Vector3(0.02f, 1f, 0.02f), Color.white, "String");
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, p + new Vector3(0f, 2.2f + (i % 2) * 0.2f, 0f), new Vector3(0.45f, 0.55f, 0.45f), cols[i], "Balloon");
                    }
                    if (ev.kind == VillageEventKind.WelcomeParty)
                        for (int i = 0; i < 7; i++) PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-1.8f + i * 0.6f, 2.6f - Mathf.Abs(i - 3) * 0.08f, 1.5f), new Vector3(0.25f, 0.25f, 0.02f), cols[i % 4], "Flag", new Vector3(0f, 0f, 45f));
                    break;
                }
                case VillageEventKind.MarketDay:
                {
                    for (int i = 0; i < ev.stalls.Count; i++)
                    {
                        var stall = ev.stalls[i];
                        var s = PrimitiveBuilder.Empty("Stall", t, Vector3.zero);
                        s.transform.position = sys.Ground(stall.position);
                        var dir = ev.position - stall.position;
                        dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f) s.transform.rotation = Quaternion.LookRotation(dir.normalized);
                        var r = sys.R(stall.residentId);
                        var awning = r != null ? Color.Lerp(r.MainColor, Color.white, 0.3f) : new Color(1f, 0.6f, 0.5f);
                        var table = PrimitiveBuilder.Create(PrimitiveType.Cube, s.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.9f, 0.8f), new Color(0.75f, 0.58f, 0.4f), "Table");
                        PrimitiveBuilder.Part(s.transform, PrimitiveType.Cube, new Vector3(0f, 2f, -0.1f), new Vector3(2.1f, 0.08f, 1.2f), awning, "Awning");
                        PrimitiveBuilder.Part(s.transform, PrimitiveType.Cylinder, new Vector3(-0.95f, 1.2f, -0.5f), new Vector3(0.06f, 0.8f, 0.06f), Brown, "Pole");
                        PrimitiveBuilder.Part(s.transform, PrimitiveType.Cylinder, new Vector3(0.95f, 1.2f, -0.5f), new Vector3(0.06f, 0.8f, 0.06f), Brown, "Pole");
                        for (int k = 0; k < stall.itemIds.Count; k++)
                        {
                            var def = ItemRegistry.GetOrPlaceholder(stall.itemIds[k]);
                            PrimitiveBuilder.Part(s.transform, def.Shape == PrimitiveType.Quad ? PrimitiveType.Cube : def.Shape, new Vector3(-0.55f + k * 0.55f, 1.05f, 0f), Vector3.one * 0.25f, def.Color, "Goods");
                        }
                        int idx = i;
                        var evId = ev.id;
                        SimpleInteractable.Attach(table, "屋台を見る", "Browse the stall", p => sys.OpenMarketStall(sys.FindEvent(evId), idx));
                    }
                    break;
                }
                case VillageEventKind.StarBandViewing:
                case VillageEventKind.NightSky:
                case VillageEventKind.FarewellParty:
                {
                    int n = ev.kind == VillageEventKind.FarewellParty ? 4 : 6;
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        var p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.2f;
                        PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, p + new Vector3(0f, 0.5f, 0f), new Vector3(0.06f, 0.5f, 0.06f), Brown, "LanternPole");
                        PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, p + new Vector3(0f, 1.1f, 0f), Vector3.one * 0.28f, ev.kind == VillageEventKind.FarewellParty ? new Color(1f, 0.8f, 0.5f) : new Color(0.7f, 0.85f, 1f), "Lantern");
                    }
                    if (ev.kind == VillageEventKind.FarewellParty)
                        for (int i = 0; i < 3; i++) PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(1.5f + i * 0.2f, 0.3f + i * 0.55f, -1.5f), new Vector3(0.6f, 0.5f, 0.6f), new Color(0.8f, 0.62f, 0.4f), "Box");
                    break;
                }
                case VillageEventKind.CookParty:
                {
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(3f, 0.1f, 1f), new Color(0.75f, 0.58f, 0.4f), "LongTable");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-1.3f, 0.22f, 0f), new Vector3(0.1f, 0.45f, 0.8f), Brown, "Leg");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(1.3f, 0.22f, 0f), new Vector3(0.1f, 0.45f, 0.8f), Brown, "Leg");
                    for (int i = 0; i < 5; i++)
                    {
                        PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(-1.2f + i * 0.6f, 0.52f, 0f), new Vector3(0.35f, 0.02f, 0.35f), Color.white, "Plate");
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-1.2f + i * 0.6f, 0.58f, 0f), new Vector3(0.22f, 0.12f, 0.22f), Color.HSVToRGB(0.05f + i * 0.08f, 0.6f, 0.95f), "Food");
                    }
                    break;
                }
            }
            return go;
        }

        // ------------------------------------------------------------
        // 毎フレーム：運んでいる荷物・ついてくる生き物・きらめき
        // ------------------------------------------------------------

        public void Tick(float dt)
        {
            if (root == null) return;
            time += dt;
            var p = sys.LocalPlayerState;
            foreach (var kv in helpObjs)
            {
                var go = kv.Value;
                if (go == null) continue;
                var h = sys.FindHelp(kv.Key);
                if (h == null) continue;
                switch (h.kind)
                {
                    case HelpKind.CarryLoad:
                    {
                        var box = go.transform.Find("Box");
                        if (box == null) break;
                        bool carrying = sys.LocalCarryHelpId == h.id && p != null && p.Avatar != null;
                        var col = box.GetComponent<Collider>();
                        // 運んでいる間は調べる対象にしない（住民に話しかけられるように）
                        if (col != null && col.enabled == carrying) col.enabled = !carrying;
                        if (carrying)
                        {
                            // 頭の上に抱えて運ぶ
                            box.position = p.Position + Vector3.up * 2.1f + p.Forward * 0.1f;
                            box.rotation = p.Avatar.rotation;
                        }
                        else
                        {
                            box.localPosition = new Vector3(0f, 0.4f, 0f);
                            // 住民が持ち上げようとして困っている揺れ
                            box.localRotation = Quaternion.Euler(0f, 0f, string.IsNullOrEmpty(h.claimedBy) ? Mathf.Sin(time * 3f) * 4f : 0f);
                        }
                        break;
                    }
                    case HelpKind.LostItem:
                    {
                        var lost = go.transform.Find("Lost");
                        if (lost != null) lost.Rotate(0f, 50f * dt, 0f);
                        var sp = go.transform.Find("Sparkle");
                        if (sp != null) sp.localScale = Vector3.one * (0.06f + 0.04f * Mathf.Abs(Mathf.Sin(time * 4f)));
                        break;
                    }
                    case HelpKind.LostCreature:
                    {
                        var body = go.transform.Find("Creature");
                        if (body == null) break;
                        if (sys.LocalGuideHelpId == h.id && p != null && p.Avatar != null)
                        {
                            // 後ろをちょこちょこついてくる
                            var target = p.Position - p.Forward * 1.3f;
                            var cur = body.position;
                            var next = Vector3.Lerp(cur, sys.Ground(target) + Vector3.up * 0.35f, 1f - Mathf.Exp(-4f * dt));
                            next.y = sys.Ground(next).y + 0.35f + Mathf.Abs(Mathf.Sin(time * 8f)) * 0.15f;
                            body.position = next;
                            var look = target - cur;
                            look.y = 0f;
                            if (look.sqrMagnitude > 0.01f) body.rotation = Quaternion.LookRotation(look.normalized);
                        }
                        else
                        {
                            // 迷子：その場でうろうろ
                            body.localPosition = new Vector3(Mathf.Sin(time * 0.7f) * 1.2f, 0.35f + Mathf.Abs(Mathf.Sin(time * 5f)) * 0.08f, Mathf.Cos(time * 0.5f) * 0.8f);
                            body.localRotation = Quaternion.Euler(0f, time * 40f, 0f);
                        }
                        break;
                    }
                    case HelpKind.Laundry:
                    {
                        var c1 = go.transform.Find("Cloth1");
                        if (c1 != null) c1.localRotation = Quaternion.Euler(Mathf.Sin(time * 2.3f) * 12f, 0f, 0f);
                        break;
                    }
                }
            }
            foreach (var kv in eventObjs)
            {
                if (kv.Value == null) continue;
                var t = kv.Value.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.name == "Balloon") c.localPosition += new Vector3(0f, Mathf.Sin(time * 1.5f + i) * 0.002f, 0f);
                }
            }
        }
    }
}
