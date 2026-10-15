using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 星（Planet スコープ）に保存・同期する言語の状態。ホストが正本。
    /// ・言語生成バージョンと指紋：ホストとゲストで同じ宇宙語になっているかの確認（宇宙語仕様 §14.3、マルチ仕様 §13）。
    /// ・bumps：動的な語どうしの偶然の衝突を回避した語（全員で同じ綴りにするため）。
    /// ・発話の中継は一時メッセージ（"speech.relay"）へ移した。旧版のセーブにある "relaySerial" / "relay" は読み込み時に無視される
    ///   （JsonUtility は型に無いフィールドを読み飛ばす）。
    /// </summary>
    [Serializable]
    public class LanguagePlanetState
    {
        public int languageGenVersion;
        public string fingerprint;
        public List<StringStringPair> bumps = new List<StringStringPair>();
    }

    /// <summary>
    /// ホスト→ゲストの発話中継（一時メッセージ "speech.relay" の中身。セーブしない）。
    /// 意味データを送り、ゲスト側で同じ規則で変換・表示・記録する。宛先は utterance.addresseeId。
    /// </summary>
    [Serializable]
    public class SpeechRelayEntry
    {
        public string speakerId;
        public string speakerKind;
        public SpeechChannel channel;
        public bool hasPosition;
        public float x, y, z;
        public Utterance utterance;
    }

    /// <summary>会話ログ（PlayerPlanetKnowledge スコープ：星ごとの個人の知識）。</summary>
    [Serializable]
    public class ConversationLogState
    {
        public List<UtteranceRecord> records = new List<UtteranceRecord>();
    }

    /// <summary>
    /// 宇宙語の表示・声の設定（端末ごと。PlayerPrefs）。タブレットの「声と文字」アプリで変更できる。
    /// </summary>
    public static class LanguageSettings
    {
        private const string KeyVoice = "uchujin.lang.voice";
        private const string KeyVolume = "uchujin.lang.voiceVolume";
        private const string KeySignReading = "uchujin.lang.signReading";
        private const string KeyBubbleScale = "uchujin.lang.bubbleScale";
        private const string KeySymbols = "uchujin.lang.symbols";

        private static bool loaded;
        private static bool voice = true;
        private static float volume = 0.35f;
        private static bool signReading = true;
        private static float bubbleScale = 1f;
        private static bool symbols = true;

        /// <summary>簡単な「声」（音節ごとの短い音）を鳴らすか（宇宙語仕様 §11.2 の提案。オフ可）。</summary>
        public static bool VoiceEnabled { get { Load(); return voice; } set { voice = value; Save(); } }
        public static float VoiceVolume { get { Load(); return volume; } set { volume = Mathf.Clamp01(value); Save(); } }
        /// <summary>看板・手紙の宇宙文字に読み方（発音表記）を併記するか。</summary>
        public static bool ShowSignReading { get { Load(); return signReading; } set { signReading = value; Save(); } }
        public static float BubbleScale { get { Load(); return bubbleScale; } set { bubbleScale = Mathf.Clamp(value, 0.7f, 1.6f); Save(); } }
        /// <summary>吹き出しに表情・身振りの記号を出すか。</summary>
        public static bool ShowSymbols { get { Load(); return symbols; } set { symbols = value; Save(); } }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                voice = PlayerPrefs.GetInt(KeyVoice, 1) == 1;
                volume = PlayerPrefs.GetFloat(KeyVolume, 0.35f);
                signReading = PlayerPrefs.GetInt(KeySignReading, 1) == 1;
                bubbleScale = PlayerPrefs.GetFloat(KeyBubbleScale, 1f);
                symbols = PlayerPrefs.GetInt(KeySymbols, 1) == 1;
            }
            catch (Exception) { }
        }

        private static void Save()
        {
            try
            {
                PlayerPrefs.SetInt(KeyVoice, voice ? 1 : 0);
                PlayerPrefs.SetFloat(KeyVolume, volume);
                PlayerPrefs.SetInt(KeySignReading, signReading ? 1 : 0);
                PlayerPrefs.SetFloat(KeyBubbleScale, bubbleScale);
                PlayerPrefs.SetInt(KeySymbols, symbols ? 1 : 0);
            }
            catch (Exception) { }
        }
    }
}
