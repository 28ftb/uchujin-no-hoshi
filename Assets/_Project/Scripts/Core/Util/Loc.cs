using System;
using System.Collections.Generic;

namespace Uchujin.Core
{
    public enum UILanguage
    {
        Japanese,
        English
    }

    /// <summary>
    /// UI表示言語のローカライズ。宇宙語とは完全に独立している（宇宙語は星ごとの架空言語で、UI言語では変わらない）。
    /// コード中のUI文字列は Loc.T("日本語", "English") の形で両言語を併記する。
    /// 将来、外部テーブル（CSV等）に移行するためのキー登録機能も用意している。
    /// </summary>
    public static class Loc
    {
        public static UILanguage Current = UILanguage.Japanese;
        public static event Action LanguageChanged;

        private static readonly Dictionary<string, string[]> table = new Dictionary<string, string[]>();

        public static void SetLanguage(UILanguage lang)
        {
            if (Current == lang) return;
            Current = lang;
            LanguageChanged?.Invoke();
        }

        /// <summary>日本語・英語の文字列から現在のUI言語の方を返す。</summary>
        public static string T(string ja, string en)
        {
            if (Current == UILanguage.English && !string.IsNullOrEmpty(en)) return en;
            return ja ?? en ?? string.Empty;
        }

        public static string Format(string ja, string en, params object[] args)
        {
            return string.Format(T(ja, en), args);
        }

        public static void Register(string key, string ja, string en)
        {
            table[key] = new[] { ja, en };
        }

        public static string Key(string key)
        {
            if (table.TryGetValue(key, out var v)) return T(v[0], v[1]);
            return key;
        }
    }

    /// <summary>日英ペアの文字列。データ定義用。</summary>
    [Serializable]
    public struct LocText
    {
        public string ja;
        public string en;

        public LocText(string ja, string en)
        {
            this.ja = ja;
            this.en = en;
        }

        public override string ToString() => Loc.T(ja, en);

        public bool IsEmpty => string.IsNullOrEmpty(ja) && string.IsNullOrEmpty(en);
    }
}
