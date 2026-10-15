using System;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Network
{
    /// <summary>
    /// 開発用：同じPCで2つ起動してテストするための起動引数。
    ///   -uchujinTestSlot 2   … このプロセスだけ別のプレイヤーIDを使う（PlayerPrefs は同じPCで共有されるため）。
    /// 仕組み：シーン読み込み前に PlayerPrefs のプレイヤーIDを一時的に差し替え、Game.Awake が読み終えた直後
    /// （NetworkSystem.RegisterContent）に元へ戻す。元のIDは書き換えない。
    /// ※ Unity エディタとビルドは PlayerPrefs の保存先が別なので、その組み合わせなら引数は不要（OS により異なる。要確認）。
    /// </summary>
    public static class NetDevArgs
    {
        private const string PrefKey = "uchujin.localPlayerId";
        private static bool overridden;
        private static string originalId;

        public static int TestSlot { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyTestSlot()
        {
            TestSlot = ParseIntArg("-uchujinTestSlot");
            if (TestSlot <= 1) return;
            originalId = PlayerPrefs.GetString(PrefKey, "");
            string baseId = string.IsNullOrEmpty(originalId) ? "dev" : originalId;
            PlayerPrefs.SetString(PrefKey, baseId + "-t" + TestSlot);
            overridden = true;
            Debug.Log($"[NetDevArgs] テスト用プレイヤーID（スロット {TestSlot}）で起動します");
        }

        /// <summary>Game.Awake がプレイヤーIDを読んだ後に呼ぶ（NetworkSystem.RegisterContent）。</summary>
        public static void RestoreOriginalPlayerIdPref()
        {
            if (!overridden) return;
            overridden = false;
            if (string.IsNullOrEmpty(originalId)) PlayerPrefs.DeleteKey(PrefKey);
            else PlayerPrefs.SetString(PrefKey, originalId);
            PlayerPrefs.Save();
        }

        private static int ParseIntArg(string name)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var v)) return v;
                }
            }
            catch
            {
                // 取得できない環境では無視
            }
            return 0;
        }
    }
}
