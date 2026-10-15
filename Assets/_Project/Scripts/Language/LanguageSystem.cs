using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Language
{
    /// <summary>
    /// 宇宙語システム（本作の核。宇宙語仕様 v0.2 / 企画仕様 §5〜§8, §11 / タブレット仕様 §3, §13）。
    ///
    /// ・PreparePlanet：星のシードと言語生成バージョンから、音韻・語彙・文法・数詞・宇宙文字を決定的に生成する。
    /// ・Render：意味データ（MeaningFrame）をその星の文法で宇宙語の文へ変換する（日本語の単語置換はしない）。
    /// ・Speak：頭上に吹き出し（宇宙語＋表情・身振りの記号）を出し、聞こえる距離なら会話ログへ記録して UtteranceSpokenEvent を発行。
    /// ・Replay：記録の原本（text / tokens）をそのまま再表示する（後から別の宇宙語に変わらない）。
    /// ・会話ログ（最近 300 件）は PlayerPlanetKnowledge スコープ（星ごとの個人の知識）。
    /// ・マルチ：ホストとゲストは同じシード＋同じ生成バージョンで同じ言語を作る。ホストで話された発話（relayToGuests=true）は
    ///   一時メッセージ（チャンネル "speech.relay"、セーブしない）で意味データのままゲストへ届き、ゲスト側で同じ規則で変換して吹き出し・記録になる。
    /// ・内部の意味（internalId / meaning）は一切表示しない。正解の自動翻訳はしない。
    /// </summary>
    public sealed class LanguageSystem : GameSystemBase, ISpeechService
    {
        // ---- 【仮数値】 ----
        /// <summary>聞こえる距離（m）。吹き出しもこの距離内でだけ見える（見えた発話＝記録された発話）。</summary>
        public const float HearingRadius = 14f;
        public const float BroadcastRadius = 60f;
        public const float SignRadius = 9f;
        public const int MaxLogRecords = 300;
        public const int MaxUnheardRecords = 400;
        /// <summary>発話の中継に使う一時メッセージのチャンネル。</summary>
        public const string SpeechRelayChannel = "speech.relay";
        /// <summary>吹き出しの表示時間：基本＋1語ごと＋1文字ごと（秒）。</summary>
        public const float BubbleBaseSeconds = 2.4f;
        public const float BubblePerToken = 0.32f;
        public const float BubblePerChar = 0.05f;
        public const float BubbleMinSeconds = 3f;
        public const float BubbleMaxSeconds = 14f;
        public const float SignBubbleSeconds = 10f;
        public const float OverheardDurationFactor = 0.85f;
        /// <summary>星の名前の概念ID。</summary>
        public const string PlanetNameConcept = "NAME:__planet";

        public static LanguageSystem Instance { get; private set; }

        public override string SaveKey => "language";
        public override int DailyUpdateOrder => 50;

        private LanguageDefinition lang;
        private SentenceRenderer renderer;
        private readonly List<UtteranceRecord> log = new List<UtteranceRecord>();
        private readonly Dictionary<string, UtteranceRecord> logIndex = new Dictionary<string, UtteranceRecord>();
        /// <summary>まだ聞いていない記録（CreateRecord・聞こえない距離の Speak）。GetRecord で引けるように一時保持。</summary>
        private readonly List<UtteranceRecord> unheard = new List<UtteranceRecord>();
        private readonly Dictionary<string, UtteranceRecord> unheardIndex = new Dictionary<string, UtteranceRecord>();
        private readonly SpeechBubbleOverlay overlay = new SpeechBubbleOverlay();
        private readonly List<SpeechBubble> heardScratch = new List<SpeechBubble>();
        private SpeechVoice voice;
        private LanguageSettingsApp settingsApp;

        // マルチ：言語の指紋の不一致の警告を1回にする
        private string remoteFingerprint;

        public LanguageSystem()
        {
            Instance = this;
            voice = new SpeechVoice(this);
        }

        /// <summary>現在の星の言語定義（開発者向け・タブレット等の表示補助用。内部の正解を含むので UI に直接出さないこと）。</summary>
        public LanguageDefinition Language
        {
            get
            {
                EnsureLanguage();
                return lang;
            }
        }

        public SentenceRenderer Renderer
        {
            get
            {
                EnsureLanguage();
                return renderer;
            }
        }

        public SpeechBubbleOverlay Overlay => overlay;

        // ------------------------------------------------------------
        // ライフサイクル
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            WorldAuthority.RegisterTransientHandler(SpeechRelayChannel, OnSpeechRelayReceived);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            // 先に準備された他システム（World など）が語を求めて既に同じ星の言語を作っていれば、それを使い続ける
            bool reuse = lang != null && lang.PlanetSeed == ctx.Seed && lang.Version == Math.Max(1, Math.Min(ctx.Planet.languageGenVersion, LanguageDefinition.LatestVersion));
            if (!reuse) lang = LanguageDefinition.Generate(ctx.Seed, ctx.Planet.languageGenVersion);
            renderer = new SentenceRenderer(lang);
            remoteFingerprint = null;
            ClearLogs();
            // 他システムが PreparePlanet 中に名前などを参照できるよう、星の名前を先に作っておく
            lang.Lexicon.Get(PlanetNameConcept);
        }

        public override void BuildScene()
        {
            voice.Build(Game.SceneRoot);
        }

        public override void OnPlayStarted()
        {
            UIManager.RegisterOverlay(overlay);
            PregenerateDynamicWords();
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                if (settingsApp == null) settingsApp = new LanguageSettingsApp();
                try { tablet.RegisterApp(settingsApp); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public override void Tick(float deltaTime)
        {
            float now = Time.unscaledTime;
            overlay.RemoveExpired(now);
            // 表示中の吹き出しに近づいたら、その時点で聞いたことにする
            var player = LocalPlayer;
            if (player != null && overlay.Bubbles.Count > 0)
            {
                // MarkHeard はイベントを発行し、購読側が同期的に Speak する可能性があるので、写しを回す
                heardScratch.Clear();
                foreach (var b in overlay.Bubbles)
                {
                    if (b.Logged) continue;
                    if (b.Subtitle || (b.WorldPoint - player.Position).sqrMagnitude <= b.Radius * b.Radius) heardScratch.Add(b);
                }
                foreach (var b in heardScratch) MarkHeard(b);
                heardScratch.Clear();
            }
#if UNITY_EDITOR
            if (GameInput.KeyDown(KeyCode.F12)) LanguageDebugWindow.Toggle(this);
#endif
        }

        public override void Teardown()
        {
            UIManager.UnregisterOverlay(overlay);
            overlay.Clear();
            voice.Dispose();
            ClearLogs();
            lang = null;
            renderer = null;
        }

        private void ClearLogs()
        {
            log.Clear();
            logIndex.Clear();
            unheard.Clear();
            unheardIndex.Clear();
        }

        /// <summary>
        /// 動的な語（アイテム・生物・住民名）を決まった順で先に作る。
        /// どの順で話されても同じ綴りになるよう、ホストとゲストで同じ集合を同じ順に処理する。
        /// </summary>
        private void PregenerateDynamicWords()
        {
            if (lang == null) return;
            var ids = new List<string>();
            foreach (var def in ItemRegistry.All) if (def != null) ids.Add(def.EffectiveConceptId);
            var creatures = Game.Service<ICreatureService>();
            if (creatures != null)
            {
                try { foreach (var s in creatures.AllSpeciesIds) ids.Add(Concepts.Species(s)); }
                catch (Exception e) { Debug.LogException(e); }
            }
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null)
            {
                try
                {
                    foreach (var r in dir.ActiveResidents) ids.Add(Concepts.Name(r.Id));
                    foreach (var r in dir.FormerResidents) ids.Add(Concepts.Name(r.Id));
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            ids.Sort(string.CompareOrdinal);
            foreach (var id in ids) lang.Lexicon.Get(id);
        }

        private void EnsureLanguage()
        {
            if (lang != null) return;
            var planet = Game != null ? Game.Planet : null;
            lang = planet != null ? LanguageDefinition.Generate(planet.seed, planet.languageGenVersion) : LanguageDefinition.Generate(1UL, PlanetInfo.CurrentLanguageGenVersion);
            renderer = new SentenceRenderer(lang);
        }

        // ------------------------------------------------------------
        // ISpeechService：変換
        // ------------------------------------------------------------

        public RenderedUtterance Render(Utterance utterance)
        {
            EnsureLanguage();
            int before = lang.Lexicon.Bumps.Count;
            var r = renderer.Render(utterance);
            if (lang.Lexicon.Bumps.Count != before && IsAuthority) MarkDirty();
            return r;
        }

        public string RenderWord(string conceptId)
        {
            EnsureLanguage();
            return lang.Word(conceptId);
        }

        public string ResidentName(string residentId) => RenderWord(Concepts.Name(residentId));

        public string PlanetName => RenderWord(PlanetNameConcept);

        public string RenderNumber(int n)
        {
            EnsureLanguage();
            return renderer.RenderNumber(n);
        }

        /// <summary>
        /// 宇宙人の書き文字（看板・手紙・掲示用）。読み方（発音表記）とは別の表記（タブレット仕様 §3）。
        /// </summary>
        public string RenderScript(RenderedUtterance rendered)
        {
            EnsureLanguage();
            return rendered == null ? string.Empty : lang.Script.Text(rendered.text);
        }

        public string RenderScript(UtteranceRecord record)
        {
            EnsureLanguage();
            return record == null ? string.Empty : lang.Script.Text(record.text);
        }

        // ------------------------------------------------------------
        // ISpeechService：話す・記録する
        // ------------------------------------------------------------

        public UtteranceRecord Speak(Utterance utterance, Transform speaker, SpeechChannel channel, string speakerKind = "resident")
        {
            return Speak(utterance, speaker, channel, speakerKind, true);
        }

        /// <summary>
        /// 話す。relayToGuests=false なら、ホストでもゲストへ中継しない（各端末がそれぞれ生成する独り言・立ち話など、二重表示を避けたい発話用）。
        /// </summary>
        public UtteranceRecord Speak(Utterance utterance, Transform speaker, SpeechChannel channel, string speakerKind, bool relayToGuests)
        {
            if (utterance == null) return null;
            Vector3? pos = speaker != null ? speaker.position : (Vector3?)null;
            var rec = SpeakInternal(utterance, speaker, pos, channel, speakerKind, false);
            // ホスト：ゲストにも届ける（意味データのまま。ゲスト側で同じ規則で変換する）
            if (rec != null && relayToGuests && WorldAuthority.Role == NetRole.Host) SendRelay(utterance, speakerKind, channel, pos);
            return rec;
        }

        private UtteranceRecord SpeakInternal(Utterance utterance, Transform speaker, Vector3? position, SpeechChannel channel, string speakerKind, bool isRelay)
        {
            EnsureLanguage();
            var rendered = Render(utterance);
            var rec = BuildRecord(utterance, rendered, channel, speakerKind, position ?? (LocalPlayer != null ? LocalPlayer.Position : Vector3.zero));
            RememberUnheard(rec);
            var b = new SpeechBubble
            {
                Record = rec,
                Anchor = speaker,
                Position = position ?? Vector3.zero,
                Subtitle = speaker == null && !position.HasValue,
                Channel = channel,
                Radius = RadiusOf(channel),
                Born = Time.unscaledTime,
                Until = Time.unscaledTime + DurationOf(rec),
                AnchorHeight = AnchorHeightOf(speaker),
                Accent = AccentOf(utterance.speakerId),
                IsRelay = isRelay,
                Script = IsScriptChannel(channel, speakerKind)
            };
            overlay.Add(b);
            // 今この瞬間に聞こえる距離なら即座に記録（呼び出し側がすぐ音声カード化できるように）
            var player = LocalPlayer;
            if (b.Subtitle || (player != null && (b.WorldPoint - player.Position).sqrMagnitude <= b.Radius * b.Radius)) MarkHeard(b);
            return rec;
        }

        public UtteranceRecord CreateRecord(Utterance utterance, SpeechChannel channel, string speakerKind, Vector3 position)
        {
            if (utterance == null) return null;
            EnsureLanguage();
            var rendered = Render(utterance);
            var rec = BuildRecord(utterance, rendered, channel, speakerKind, position);
            RememberUnheard(rec);
            return rec;
        }

        public bool ShowScriptReading => LanguageSettings.ShowSignReading;

        public void Replay(UtteranceRecord record, Transform at)
        {
            if (record == null) return;
            var shown = record.channel == SpeechChannel.Sign || record.channel == SpeechChannel.Letter ? record.channel : SpeechChannel.ManepiPlayback;
            Replay(record, at, shown);
        }

        public void Replay(UtteranceRecord record, Transform at, SpeechChannel shownAs)
        {
            if (record == null) return;
            var b = new SpeechBubble
            {
                Record = record, // 原本をそのまま表示する（再変換しない）
                Anchor = at,
                Position = at != null ? at.position : Vector3.zero,
                Subtitle = at == null,
                Channel = shownAs,
                Radius = RadiusOf(SpeechChannel.ManepiPlayback),
                Born = Time.unscaledTime,
                Until = Time.unscaledTime + DurationOf(record),
                AnchorHeight = AnchorHeightOf(at),
                Accent = AccentOf(record.speakerId),
                IsReplay = true,
                Script = IsScriptChannel(record.channel, record.speakerKind)
            };
            overlay.Add(b);
            var player = LocalPlayer;
            if (b.Subtitle || (player != null && (b.WorldPoint - player.Position).sqrMagnitude <= b.Radius * b.Radius)) MarkHeard(b);
        }

        public IReadOnlyList<UtteranceRecord> RecentRecords => log;

        public UtteranceRecord GetRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return null;
            if (logIndex.TryGetValue(recordId, out var r)) return r;
            if (unheardIndex.TryGetValue(recordId, out r)) return r;
            return null;
        }

        // ------------------------------------------------------------
        // 記録
        // ------------------------------------------------------------

        private UtteranceRecord BuildRecord(Utterance u, RenderedUtterance rendered, SpeechChannel channel, string speakerKind, Vector3 position)
        {
            var clock = Clock;
            var rec = new UtteranceRecord
            {
                recordId = Ids.New(),
                speakerId = u.speakerId,
                speakerKind = string.IsNullOrEmpty(speakerKind) ? "resident" : speakerKind,
                text = rendered.text,
                tokens = rendered.tokens,
                expression = u.expression,
                channel = channel,
                contextTag = u.contextTag,
                day = clock != null ? clock.Day : 0,
                minuteOfDay = clock != null ? clock.MinuteOfDay : 0,
                areaId = LocalPlayer != null ? LocalPlayer.CurrentAreaId : null,
                posX = position.x,
                posY = position.y,
                posZ = position.z,
                meaning = CloneUtterance(u)
            };
            if (u.gestures != null)
                foreach (var g in u.gestures)
                    if (g != null) rec.gestures.Add(new GestureCue(g.gesture, g.target, g.number));
            if (u.relatedIds != null)
                foreach (var id in u.relatedIds)
                    if (!string.IsNullOrEmpty(id) && !rec.relatedIds.Contains(id)) rec.relatedIds.Add(id);
            foreach (var g in rec.gestures)
                if (!string.IsNullOrEmpty(g.target) && !rec.relatedIds.Contains(g.target)) rec.relatedIds.Add(g.target);
            var weather = Game != null ? Game.Service<IWeatherService>() : null;
            if (weather != null)
            {
                try { rec.weatherSummary = weather.GetAt(position).Summary(); }
                catch (Exception) { rec.weatherSummary = null; }
            }
            return rec;
        }

        private static Utterance CloneUtterance(Utterance u)
        {
            if (u == null) return null;
            try { return JsonUtility.FromJson<Utterance>(JsonUtility.ToJson(u)); }
            catch (Exception) { return u; }
        }

        private void RememberUnheard(UtteranceRecord rec)
        {
            if (rec == null || unheardIndex.ContainsKey(rec.recordId)) return;
            unheard.Add(rec);
            unheardIndex[rec.recordId] = rec;
            while (unheard.Count > MaxUnheardRecords)
            {
                unheardIndex.Remove(unheard[0].recordId);
                unheard.RemoveAt(0);
            }
        }

        /// <summary>ローカルプレイヤーがその吹き出しを聞いた：会話ログに入れ、（再生でなければ）イベントを発行し、声を鳴らす。</summary>
        private void MarkHeard(SpeechBubble b)
        {
            if (b == null || b.Logged) return;
            b.Logged = true;
            var rec = b.Record;
            if (rec == null) return;
            if (LocalPlayer != null) rec.areaId = LocalPlayer.CurrentAreaId;
            bool isNew = AddToLog(rec);
            Vector3? at = b.Subtitle ? (Vector3?)null : b.WorldPoint;
            try { voice.Play(rec, at, b.IsReplay); }
            catch (Exception e) { Debug.LogException(e); }
            if (isNew && !b.IsReplay) EventBus.Publish(new UtteranceSpokenEvent { Record = rec });
        }

        private bool AddToLog(UtteranceRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.recordId) || logIndex.ContainsKey(rec.recordId)) return false;
            if (unheardIndex.Remove(rec.recordId)) unheard.Remove(rec);
            log.Add(rec);
            logIndex[rec.recordId] = rec;
            while (log.Count > MaxLogRecords)
            {
                logIndex.Remove(log[0].recordId);
                log.RemoveAt(0);
            }
            return true;
        }

        // ------------------------------------------------------------
        // 吹き出しの見た目の補助
        // ------------------------------------------------------------

        private static bool IsScriptChannel(SpeechChannel channel, string speakerKind)
        {
            return channel == SpeechChannel.Sign || channel == SpeechChannel.Letter || speakerKind == "sign" || speakerKind == "letter";
        }

        private static float RadiusOf(SpeechChannel channel)
        {
            switch (channel)
            {
                case SpeechChannel.Broadcast: return BroadcastRadius;
                case SpeechChannel.Sign: return SignRadius;
                case SpeechChannel.Letter: return SignRadius;
                default: return HearingRadius;
            }
        }

        private static float DurationOf(UtteranceRecord rec)
        {
            int tokens = rec.tokens != null ? rec.tokens.Count : 0;
            int chars = rec.text != null ? rec.text.Length : 0;
            float d = BubbleBaseSeconds + tokens * BubblePerToken + chars * BubblePerChar;
            if (rec.channel == SpeechChannel.Overheard) d *= OverheardDurationFactor;
            d = Mathf.Clamp(d, BubbleMinSeconds, BubbleMaxSeconds);
            if (rec.channel == SpeechChannel.Sign || rec.channel == SpeechChannel.Letter) d = Mathf.Max(d, SignBubbleSeconds);
            return d;
        }

        private static float AnchorHeightOf(Transform t)
        {
            if (t == null) return 2.2f;
            float top = 0f;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                float y = r.bounds.max.y - t.position.y;
                if (!any || y > top) top = y;
                any = true;
            }
            if (!any) return 2.2f;
            return Mathf.Clamp(top + 0.35f, 0.8f, 6f);
        }

        private Color AccentOf(string speakerId)
        {
            var dir = Game != null ? Game.Service<IResidentDirectory>() : null;
            var info = dir != null && !string.IsNullOrEmpty(speakerId) ? dir.Get(speakerId) : null;
            if (info != null) return info.MainColor;
            return UIStyles.Accent;
        }

        /// <summary>画面座標が吹き出しの上か（道具のクリックと重ならないようにする判定。コアへの追加要望を参照）。</summary>
        public static bool IsPointerOverBubble(Vector2 screenPosition) => SpeechBubbleOverlay.IsPointerOver(screenPosition);

        // ------------------------------------------------------------
        // マルチ：発話の中継（ホスト → ゲスト、一時メッセージ "speech.relay"）
        // ------------------------------------------------------------

        private void SendRelay(Utterance u, string speakerKind, SpeechChannel channel, Vector3? pos)
        {
            var e = new SpeechRelayEntry
            {
                speakerId = u.speakerId,
                speakerKind = speakerKind,
                channel = channel,
                hasPosition = pos.HasValue,
                x = pos?.x ?? 0f,
                y = pos?.y ?? 0f,
                z = pos?.z ?? 0f,
                utterance = u
            };
            string json;
            try { json = JsonUtility.ToJson(e); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }
            WorldAuthority.BroadcastTransient(SpeechRelayChannel, json);
        }

        /// <summary>ゲスト：ホストから中継された発話を受け取り、自分の規則で変換して吹き出し・記録にする。</summary>
        private void OnSpeechRelayReceived(string fromPlayerId, string json)
        {
            // ホストの発話だけを受け付ける（ホストに届いたゲストからの物は無視：発話はホスト権威）
            if (!WorldAuthority.IsGuest || string.IsNullOrEmpty(json)) return;
            if (Game == null || Game.State != GameState.Playing) return;
            SpeechRelayEntry e;
            try { e = JsonUtility.FromJson<SpeechRelayEntry>(json); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }
            if (e?.utterance == null) return;
            Transform anchor = null;
            if (!string.IsNullOrEmpty(e.speakerId))
            {
                var dir = Game.Service<IResidentDirectory>();
                var info = dir != null ? dir.Get(e.speakerId) : null;
                if (info != null) anchor = info.Transform;
                if (anchor == null)
                {
                    var ps = Game.Players != null ? Game.Players.Get(e.speakerId) : null;
                    if (ps != null) anchor = ps.Avatar;
                }
            }
            Vector3? pos = e.hasPosition ? new Vector3(e.x, e.y, e.z) : (Vector3?)null;
            var ch = RelayChannelFor(e.channel, e.utterance.addresseeId);
            SpeakInternal(e.utterance, anchor, anchor != null ? anchor.position : pos, ch, e.speakerKind, true);
        }

        /// <summary>
        /// 中継された会話の扱い：自分（ローカルのプレイヤー）に向けた言葉なら Direct、それ以外は Overheard。
        /// 呼びかけ・看板・手紙・マネピの再現は元のまま（届く距離や見た目が変わらないように）。
        /// </summary>
        private SpeechChannel RelayChannelFor(SpeechChannel channel, string addresseeId)
        {
            if (channel != SpeechChannel.Direct && channel != SpeechChannel.Overheard) return channel;
            string me = Game != null ? Game.LocalPlayerId : WorldAuthority.LocalPlayerId;
            return !string.IsNullOrEmpty(addresseeId) && addresseeId == me ? SpeechChannel.Direct : SpeechChannel.Overheard;
        }

        // ------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    EnsureLanguage();
                    var st = new LanguagePlanetState
                    {
                        languageGenVersion = lang.Version,
                        fingerprint = lang.Fingerprint
                    };
                    foreach (var b in lang.Lexicon.Bumps) st.bumps.Add(new StringStringPair(b.key, b.value));
                    return JsonUtility.ToJson(st);
                }
                case SaveScope.PlayerPlanetKnowledge:
                {
                    if (!string.IsNullOrEmpty(playerId) && Game != null && playerId != Game.LocalPlayerId) return null;
                    var st = new ConversationLogState();
                    st.records.AddRange(log);
                    return JsonUtility.ToJson(st);
                }
                default:
                    return null;
            }
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    LanguagePlanetState st;
                    try { st = JsonUtility.FromJson<LanguagePlanetState>(json); }
                    catch (Exception e) { Debug.LogException(e); return; }
                    if (st == null) return;
                    EnsureLanguage();
                    if (st.languageGenVersion != 0 && st.languageGenVersion != lang.Version)
                        Debug.LogWarning($"[Language] 保存された言語生成バージョン {st.languageGenVersion} と現在の生成 {lang.Version} が異なります");
                    if (!string.IsNullOrEmpty(st.fingerprint) && st.fingerprint != lang.Fingerprint && st.fingerprint != remoteFingerprint)
                    {
                        remoteFingerprint = st.fingerprint;
                        Debug.LogWarning($"[Language] 言語の指紋が一致しません（保存/ホスト {st.fingerprint} / 現在 {lang.Fingerprint}）。生成アルゴリズムが変わった可能性があります。生成バージョンを上げてください。");
                    }
                    // 古いセーブ・旧版の状態にある中継バッファ（"relaySerial" / "relay"）は JsonUtility が無視する（中継は一時メッセージへ移った）
                    lang.Lexicon.ApplyBumps(st.bumps);
                    break;
                }
                case SaveScope.PlayerPlanetKnowledge:
                {
                    ConversationLogState st;
                    try { st = JsonUtility.FromJson<ConversationLogState>(json); }
                    catch (Exception e) { Debug.LogException(e); return; }
                    log.Clear();
                    logIndex.Clear();
                    if (st?.records == null) return;
                    foreach (var r in st.records)
                    {
                        if (r == null || string.IsNullOrEmpty(r.recordId) || logIndex.ContainsKey(r.recordId)) continue;
                        log.Add(r);
                        logIndex[r.recordId] = r;
                    }
                    while (log.Count > MaxLogRecords)
                    {
                        logIndex.Remove(log[0].recordId);
                        log.RemoveAt(0);
                    }
                    break;
                }
            }
        }
    }
}
