using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Weather
{
    // 天気の記録アプリ（AppId "weather"）：プレイヤーが実際に体験した天候の履歴（時間・天候仕様 §14）。
    // 住民の天気予報（マネピの伝言）と見比べて宇宙語を推理するための材料。予報の和訳は出さない。
    // SaveScope.PlayerPlanetKnowledge（星ごと・プレイヤーごと）。

    [Serializable]
    public class WeatherLogEntry
    {
        public int day;
        public int minute;
        public int dayPart;
        public WeatherState state;
    }

    [Serializable]
    public class WeatherDayNote
    {
        public int day;
        public string note;
    }

    [Serializable]
    public class WeatherLogData
    {
        public List<WeatherLogEntry> entries = new List<WeatherLogEntry>();
        public List<WeatherDayNote> notes = new List<WeatherDayNote>();

        public string NoteOf(int day)
        {
            foreach (var n in notes) if (n.day == day) return n.note;
            return null;
        }

        public void SetNote(int day, string text)
        {
            foreach (var n in notes)
                if (n.day == day)
                {
                    n.note = text;
                    return;
                }
            notes.Add(new WeatherDayNote { day = day, note = text });
        }

        public WeatherLogEntry Last => entries.Count > 0 ? entries[entries.Count - 1] : null;

        public void Prune(int currentDay)
        {
            entries.RemoveAll(e => e.day < currentDay - WeatherTuning.LogKeepDays);
            notes.RemoveAll(n => n.day < currentDay - WeatherTuning.LogKeepDays && string.IsNullOrEmpty(n.note));
        }
    }

    public sealed class WeatherLogApp : ITabletApp
    {
        private readonly WeatherSystem sys;
        private Vector2 scroll;

        public WeatherLogApp(WeatherSystem sys) { this.sys = sys; }

        public string AppId => "weather";
        public string Title => Loc.T("天気の記録", "Weather log");
        public int Order => 40;
        public string IconGlyph => Loc.T("天", "W");
        public Color IconColor => new Color(0.55f, 0.75f, 1f);
        public bool IsAvailable => true;
        public void OnOpen() { }

        public static string Glyph(WeatherState s)
        {
            if (s.Has(AlienPhenomenon.StarBand)) return "✦";
            if (s.Has(AlienPhenomenon.LightRain)) return "✧";
            if (s.Has(AlienPhenomenon.ReverseRain)) return "⇡";
            if (s.Has(AlienPhenomenon.StarWind)) return "≈";
            if (s.Has(AlienPhenomenon.CrystalHaze)) return "◇";
            if (s.Has(AlienPhenomenon.BloomTide)) return "✿";
            if (s.IsRaining) return "☂";
            if (s.IsFoggy) return "〰";
            if (s.sky >= SkyState.Cloudy) return "☁";
            return "☀";
        }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            var log = sys.History;
            var game = Game.Instance;
            var player = game != null ? game.LocalPlayer : null;
            if (player != null && game.Time != null)
            {
                var area2 = game.Areas.Get(player.CurrentAreaId);
                bool outside = area2 == null || area2.ShowWeather;
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0} {1}", "{0} {1}", game.Time.FormatDate(), game.Time.FormatClock()), UIStyles.LabelBold);
                GUILayout.Label(outside ? Glyph(sys.GetAt(player.Position)) + "  " + sys.GetAt(player.Position).Summary() : Loc.T("（屋内からは空が見えない）", "(You can't see the sky from here)"), UIStyles.Label);
                GUILayout.EndVertical();
            }
            GUILayout.Label(Loc.T("自分が外で体験した天気の記録です。住民の言葉（伝言の予報など）と見比べてみよう。",
                "Weather you experienced outdoors. Compare it with what residents say (forecast messages, etc.)."), UIStyles.LabelDim);
            scroll = GUILayout.BeginScrollView(scroll);
            if (log.entries.Count == 0) GUILayout.Label(Loc.T("まだ記録がありません。", "No records yet."), UIStyles.LabelWrap);
            int currentDay = -1;
            for (int i = log.entries.Count - 1; i >= 0; i--)
            {
                var e = log.entries[i];
                if (e.day != currentDay)
                {
                    if (currentDay != -1) GUILayout.Space(8);
                    currentDay = e.day;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Loc.Format("{0}日目", "Day {0}", e.day), UIStyles.LabelBold);
                    GUILayout.FlexibleSpace();
                    int day = e.day;
                    if (GUILayout.Button(Loc.T("メモ", "Note"), UIStyles.ButtonSmall, GUILayout.Width(90)))
                        UIManager.InputText(Loc.Format("{0}日目のメモ", "Notes for day {0}", day),
                            Loc.T("その日に聞いた言葉や気づいたことを自由に書けます。", "Write words you heard or anything you noticed that day."),
                            log.NoteOf(day) ?? "", s => log.SetNote(day, s ?? ""), true);
                    GUILayout.EndHorizontal();
                    var note = log.NoteOf(e.day);
                    if (!string.IsNullOrEmpty(note)) GUILayout.Label(Loc.T("メモ：", "Note: ") + note, UIStyles.LabelWrap);
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{e.minute / 60:00}:{e.minute % 60:00}", UIStyles.LabelDim, GUILayout.Width(70));
                GUILayout.Label(DayPartUtil.Label((DayPart)e.dayPart), UIStyles.LabelDim, GUILayout.Width(80));
                GUILayout.Label(Glyph(e.state) + "  " + e.state.Summary(), UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
