using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.World
{
    /// <summary>
    /// タブレットの地図アプリ（村仕様 §13【追加提案】、AppId "map"、M キーで開く）。
    /// ・上から見た地図。探索した範囲だけ表示される（それ以外は霧）。
    /// ・自分の拠点（家・農場・自分の店・不時着地）は自分の言葉で表示。それ以外の建物は形だけ。
    /// ・看板で見た宇宙語の文字は、見た場所に表示される（意味・用途は教えない：村仕様 §13.2）。
    /// ・自由なピンとメモ（名前は後から変更できる）。
    /// </summary>
    public sealed class MapApp : ITabletApp
    {
        private readonly WorldSystem world;
        private float zoom = 1f;
        private Vector2 center;
        private bool centered;
        private bool placingPin;
        private string selectedPin;
        private int tab;
        private Vector2 sideScroll;
        private bool dragging;
        private Vector2 dragStart;
        private Vector2 centerStart;

        public static readonly Color[] PinColors =
        {
            new Color(1f, 0.4f, 0.4f), new Color(1f, 0.75f, 0.3f), new Color(0.45f, 0.9f, 0.45f),
            new Color(0.4f, 0.75f, 1f), new Color(0.8f, 0.5f, 1f), new Color(1f, 1f, 1f)
        };

        public MapApp(WorldSystem world)
        {
            this.world = world;
        }

        public string AppId => "map";
        public string Title => Loc.T("地図", "Map");
        public int Order => 20;
        public string IconGlyph => Loc.T("地", "M");
        public Color IconColor => new Color(0.45f, 0.8f, 0.55f);
        public bool IsAvailable => world != null && world.Layout != null;

        public void OnOpen()
        {
            centered = false;
            placingPin = false;
        }

        private MapKnowledge K => world.Knowledge;

        public void Draw(Rect area)
        {
            if (world == null || world.Layout == null || world.MapTexture == null)
            {
                GUI.Label(area, Loc.T("地図を読み込めません。", "The map is not available."), UIStyles.Label);
                return;
            }
            var player = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            bool playerOutside = player != null && player.CurrentAreaId == AreaIds.Overworld;
            if (!centered)
            {
                center = playerOutside ? new Vector2(player.Position.x, player.Position.z) : Vector2.zero;
                centered = true;
            }
            float side = Mathf.Min(area.height - 50f, area.width * 0.64f);
            var mapRect = new Rect(area.x, area.y + 44f, side, side);
            var panelRect = new Rect(mapRect.xMax + 14f, area.y, area.width - side - 14f, area.height);

            // ツールバー
            var bar = new Rect(area.x, area.y, side, 38f);
            if (GUI.Button(new Rect(bar.x, bar.y, 44, 36), "+", UIStyles.Button)) zoom = Mathf.Min(6f, zoom * 1.4f);
            if (GUI.Button(new Rect(bar.x + 48, bar.y, 44, 36), "−", UIStyles.Button)) zoom = Mathf.Max(1f, zoom / 1.4f);
            if (GUI.Button(new Rect(bar.x + 96, bar.y, 120, 36), Loc.T("現在地", "Me"), UIStyles.Button) && playerOutside)
                center = new Vector2(player.Position.x, player.Position.z);
            if (GUI.Button(new Rect(bar.x + 220, bar.y, 170, 36), placingPin ? Loc.T("置く場所を選ぶ…", "Click the map…") : Loc.T("ピンを置く", "Place pin"),
                    placingPin ? UIStyles.ButtonSelected : UIStyles.Button))
                placingPin = !placingPin;
            GUI.Label(new Rect(bar.x + 400, bar.y + 6, side - 400, 30), Loc.Format("探索 {0:0}%", "Explored {0:0}%", K.ExploredRatio() * 100f), UIStyles.LabelDim);

            DrawMap(mapRect, player, playerOutside);
            HandleMapInput(mapRect);
            DrawPanel(panelRect);
        }

        // ---------------- 地図本体 ----------------

        private float HalfView => WorldTuning.Half / zoom;

        private void ClampCenter()
        {
            float h = WorldTuning.Half - HalfView;
            center.x = Mathf.Clamp(center.x, -h, h);
            center.y = Mathf.Clamp(center.y, -h, h);
        }

        private Vector2 ToScreen(Rect r, float x, float z)
        {
            float hv = HalfView;
            float u = (x - (center.x - hv)) / (hv * 2f);
            float v = (z - (center.y - hv)) / (hv * 2f);
            return new Vector2(r.x + u * r.width, r.y + (1f - v) * r.height);
        }

        private Vector2 ToWorld(Rect r, Vector2 screen)
        {
            float hv = HalfView;
            float u = (screen.x - r.x) / r.width;
            float v = 1f - (screen.y - r.y) / r.height;
            return new Vector2(center.x - hv + u * hv * 2f, center.y - hv + v * hv * 2f);
        }

        private void DrawMap(Rect r, PlayerState player, bool playerOutside)
        {
            ClampCenter();
            float hv = HalfView;
            var uv = new Rect((center.x - hv + WorldTuning.Half) / WorldTuning.WorldSize, (center.y - hv + WorldTuning.Half) / WorldTuning.WorldSize,
                hv * 2f / WorldTuning.WorldSize, hv * 2f / WorldTuning.WorldSize);
            UIStyles.FillRect(r, new Color(0.05f, 0.06f, 0.1f, 1f));
            GUI.DrawTextureWithTexCoords(r, world.MapTexture, uv);
            if (world.FogTexture != null) GUI.DrawTextureWithTexCoords(r, world.FogTexture, uv);

            GUI.BeginGroup(r);
            var local = new Rect(0, 0, r.width, r.height);
            var layout = world.Layout;
            var small = new GUIStyle(UIStyles.LabelSmall) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            var alien = new GUIStyle(UIStyles.LabelAlien) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = false };

            // 自分の拠点（主人公自身の生活・ストーリーの拠点：村仕様 §13.1）
            var farm = layout.FarmRect;
            if (K.IsExploredAt(farm.center.x, farm.center.y))
            {
                var a = ToScreen(local, farm.xMin, farm.yMax);
                var b = ToScreen(local, farm.xMax, farm.yMin);
                UIStyles.FillRect(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), new Color(0.55f, 0.35f, 0.2f, 0.35f));
                Tag(ToScreen(local, farm.center.x, farm.center.y), Loc.T("畑", "Farm"), small, new Color(1f, 0.9f, 0.7f));
            }
            foreach (var f in layout.Facilities)
            {
                if (!K.IsDiscovered(f.id)) continue;
                var p = ToScreen(local, f.position.x, f.position.z);
                if (!local.Contains(p)) continue;
                switch (f.type)
                {
                    case FacilityType.PlayerHouse:
                        Dot(p, 14f, new Color(1f, 0.55f, 0.45f));
                        Tag(p + new Vector2(0, -18), Loc.T("家", "Home"), small, Color.white);
                        break;
                    case FacilityType.PlayerShop:
                        Dot(p, 12f, new Color(1f, 0.8f, 0.35f));
                        Tag(p + new Vector2(0, -18), Loc.T("自分の店", "My shop"), small, Color.white);
                        break;
                    case FacilityType.RocketSite:
                        Tag(p, "★", new GUIStyle(small) { fontSize = 26 }, new Color(1f, 0.85f, 0.4f));
                        Tag(p + new Vector2(0, -22), Loc.T("宇宙船", "My ship"), small, Color.white);
                        break;
                    case FacilityType.Streetlight:
                    case FacilityType.Bench:
                    case FacilityType.Plaza:
                        break;
                    default:
                        Dot(p, f.type == FacilityType.ResidentHouse ? 7f : 10f, new Color(0.45f, 0.32f, 0.26f, 0.95f));
                        break;
                }
            }
            // 看板で見た宇宙語（見た場所に表示）
            foreach (var s in K.seen)
            {
                var p = ToScreen(local, s.x, s.z);
                if (!local.Contains(p)) continue;
                Dot(p, 5f, UIStyles.AlienText);
                string label = string.IsNullOrEmpty(s.guess) ? s.surface : s.surface + "（" + s.guess + "）";
                Tag(p + new Vector2(0, -14), label, alien, UIStyles.AlienText);
            }
            // ピン
            foreach (var pin in K.pins)
            {
                var p = ToScreen(local, pin.x, pin.z);
                if (!local.Contains(p)) continue;
                var c = PinColors[Mathf.Abs(pin.color) % PinColors.Length];
                Dot(p, pin.id == selectedPin ? 16f : 12f, Color.black);
                Dot(p, pin.id == selectedPin ? 12f : 9f, c);
                if (!string.IsNullOrEmpty(pin.name)) Tag(p + new Vector2(0, 16), pin.name, small, c);
            }
            // 他のプレイヤー（マルチ）
            if (Game.Instance != null)
                foreach (var other in Game.Instance.Players.All)
                {
                    if (other.IsLocal || other.CurrentAreaId != AreaIds.Overworld || other.Avatar == null) continue;
                    var p = ToScreen(local, other.Position.x, other.Position.z);
                    Dot(p, 10f, new Color(0.6f, 0.85f, 1f));
                    Tag(p + new Vector2(0, -15), other.DisplayName, small, new Color(0.7f, 0.9f, 1f));
                }
            // 自分
            if (playerOutside)
            {
                var p = ToScreen(local, player.Position.x, player.Position.z);
                var fwd = player.Forward;
                float ang = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
                var m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang, p);
                var st = new GUIStyle(UIStyles.LabelBold) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.black;
                GUI.Label(new Rect(p.x - 15, p.y - 15, 32, 32), "▲", st);
                st.normal.textColor = new Color(1f, 0.95f, 0.3f);
                GUI.Label(new Rect(p.x - 16, p.y - 16, 32, 32), "▲", st);
                GUI.matrix = m;
            }
            GUI.EndGroup();
            // 方角
            GUI.Label(new Rect(r.xMax - 40, r.y + 4, 36, 30), Loc.T("北", "N"), UIStyles.LabelBold);
        }

        private static void Dot(Vector2 p, float size, Color c)
        {
            UIStyles.FillRect(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);
        }

        private static void Tag(Vector2 p, string text, GUIStyle style, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            var st = new GUIStyle(style);
            st.normal.textColor = c;
            var size = st.CalcSize(new GUIContent(text));
            var rr = new Rect(p.x - size.x * 0.5f - 3, p.y - size.y * 0.5f, size.x + 6, size.y);
            UIStyles.FillRect(rr, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(rr, text, st);
        }

        private void HandleMapInput(Rect r)
        {
            var e = Event.current;
            if (e == null) return;
            switch (e.type)
            {
                case EventType.ScrollWheel:
                    if (r.Contains(e.mousePosition))
                    {
                        zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? 1f / 1.2f : 1.2f), 1f, 6f);
                        e.Use();
                    }
                    break;
                case EventType.MouseDown:
                    if (!r.Contains(e.mousePosition) || e.button != 0) break;
                    if (placingPin)
                    {
                        var w = ToWorld(r, e.mousePosition);
                        placingPin = false;
                        AddPin(w);
                        e.Use();
                        break;
                    }
                    // ピンの選択
                    string hit = null;
                    foreach (var pin in K.pins)
                        if ((ToScreen(r, pin.x, pin.z) - e.mousePosition).sqrMagnitude < 14f * 14f) hit = pin.id;
                    if (hit != null)
                    {
                        selectedPin = hit;
                        tab = 0;
                        e.Use();
                        break;
                    }
                    dragging = true;
                    dragStart = e.mousePosition;
                    centerStart = center;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (dragging)
                    {
                        var d = e.mousePosition - dragStart;
                        float scale = HalfView * 2f / r.width;
                        center = centerStart + new Vector2(-d.x * scale, d.y * scale);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    dragging = false;
                    break;
            }
        }

        private void AddPin(Vector2 w)
        {
            var pin = new MapPin
            {
                id = "pin." + (++K.pinCounter),
                x = w.x,
                z = w.y,
                color = K.pinCounter % PinColors.Length,
                day = Game.Instance != null && Game.Instance.Time != null ? Game.Instance.Time.Day : 0,
                name = "",
                note = ""
            };
            K.pins.Add(pin);
            selectedPin = pin.id;
            tab = 0;
            UIManager.InputText(Loc.T("ピンの名前", "Pin name"), Loc.T("この場所に好きな名前を付けられます（後で変更できます）。", "Give this place any name you like (you can change it later)."), "",
                s => pin.name = s ?? "");
        }

        // ---------------- 右の欄 ----------------

        private void DrawPanel(Rect r)
        {
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("ピン", "Pins"), tab == 0 ? UIStyles.ButtonSelected : UIStyles.Button)) tab = 0;
            if (GUILayout.Button(Loc.T("看板の文字", "Signs"), tab == 1 ? UIStyles.ButtonSelected : UIStyles.Button)) tab = 1;
            if (GUILayout.Button("?", tab == 2 ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Width(48))) tab = 2;
            GUILayout.EndHorizontal();
            sideScroll = GUILayout.BeginScrollView(sideScroll);
            if (tab == 0) DrawPins();
            else if (tab == 1) DrawSigns();
            else DrawHelp();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawPins()
        {
            if (K.pins.Count == 0)
            {
                GUILayout.Label(Loc.T("まだピンがありません。「ピンを置く」を押して地図をクリックすると、好きな場所に名前とメモを残せます。",
                    "No pins yet. Press \"Place pin\" and click the map to leave a name and a note."), UIStyles.LabelWrap);
                return;
            }
            MapPin remove = null;
            foreach (var pin in K.pins)
            {
                bool sel = pin.id == selectedPin;
                var c = PinColors[Mathf.Abs(pin.color) % PinColors.Length];
                var st = new GUIStyle(sel ? UIStyles.ButtonSelected : UIStyles.Button) { alignment = TextAnchor.MiddleLeft };
                st.normal.textColor = c;
                string name = string.IsNullOrEmpty(pin.name) ? Loc.T("（名前なし）", "(no name)") : pin.name;
                if (GUILayout.Button("● " + name, st))
                {
                    selectedPin = pin.id;
                    center = new Vector2(pin.x, pin.z);
                }
                if (!sel) continue;
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}日目に置いたピン", "Placed on day {0}", pin.day), UIStyles.LabelDim);
                GUILayout.Label(string.IsNullOrEmpty(pin.note) ? Loc.T("（メモなし）", "(no note)") : pin.note, UIStyles.LabelWrap);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.T("名前", "Rename"), UIStyles.ButtonSmall))
                    UIManager.InputText(Loc.T("ピンの名前", "Pin name"), "", pin.name, s => pin.name = s ?? "");
                if (GUILayout.Button(Loc.T("メモ", "Note"), UIStyles.ButtonSmall))
                    UIManager.InputText(Loc.T("メモ", "Note"), Loc.T("この場所について自由に書けます。", "Write anything about this place."), pin.note, s => pin.note = s ?? "", true);
                if (GUILayout.Button(Loc.T("色", "Color"), UIStyles.ButtonSmall)) pin.color = (pin.color + 1) % PinColors.Length;
                if (GUILayout.Button(Loc.T("消す", "Delete"), UIStyles.ButtonSmall)) remove = pin;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            if (remove != null)
            {
                var target = remove;
                UIManager.Confirm(Loc.T("ピンを消す", "Delete pin"), Loc.T("このピンを消しますか？", "Delete this pin?"), () =>
                {
                    K.pins.Remove(target);
                    if (selectedPin == target.id) selectedPin = null;
                });
            }
        }

        private void DrawSigns()
        {
            if (K.seen.Count == 0)
            {
                GUILayout.Label(Loc.T("看板の文字はまだ記録していません。村の看板や道標に近づくと、書かれている文字を地図に書き留めます。",
                    "No signs recorded yet. Walk up to signs and signposts to note their words on the map."), UIStyles.LabelWrap);
                return;
            }
            foreach (var s in K.seen)
            {
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.Label(s.surface, UIStyles.LabelAlien);
                GUILayout.Label((s.signpost ? Loc.T("道標", "Signpost") : Loc.T("看板", "Sign")) + " · " + Loc.Format("{0}日目に見た", "seen on day {0}", s.day), UIStyles.LabelDim);
                if (!string.IsNullOrEmpty(s.guess)) GUILayout.Label(Loc.T("自分の推測：", "My guess: ") + s.guess, UIStyles.LabelWrap);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.T("地図で見る", "Show"), UIStyles.ButtonSmall))
                {
                    center = new Vector2(s.x, s.z);
                    zoom = Mathf.Max(zoom, 2.5f);
                }
                var target = s;
                if (GUILayout.Button(Loc.T("推測を書く", "Write guess"), UIStyles.ButtonSmall))
                    UIManager.InputText(Loc.T("推測", "Guess"), Loc.T("この言葉が何を指すか、自分の考えを書き添えられます。", "Write what you think this word refers to."), target.guess, v => target.guess = v ?? "");
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
        }

        private void DrawHelp()
        {
            GUILayout.Label(Loc.T(
                "・歩いた場所だけが地図に現れます。\n・見つけた建物は形で表示されます。\n・看板や道標の文字は、見た場所に書き留められます。\n・ピンを置いて、好きな名前とメモを残せます。\n・ドラッグで移動、ホイールで拡大縮小。",
                "- Only places you have walked appear on the map.\n- Buildings you found are shown as shapes.\n- Words on signs are noted where you saw them.\n- Place pins with your own names and notes.\n- Drag to pan, scroll to zoom."), UIStyles.LabelWrap);
        }
    }

    /// <summary>タブレットが無い場合の予備（M キーで地図を開く）。</summary>
    public sealed class MapWindow : UIWindow
    {
        private readonly MapApp app;
        public MapWindow(MapApp app) { this.app = app; }
        public override string Title => app.Title;
        public override Vector2 Size => new Vector2(1360, 880);
        public override bool PausesTimeInSolo => true;
        public override void OnOpen() => app.OnOpen();
        public override void DrawContents(Rect content) => app.Draw(content);
    }
}
