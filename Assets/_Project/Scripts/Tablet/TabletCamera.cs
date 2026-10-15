using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// タブレットのカメラ（タブレット仕様 §7.4, §11.3）。
    /// タブレットを閉じて構図を決め、[Enter]（または撮影ボタン）で Camera.main から RenderTexture に描いて PNG を保存する。
    /// 撮影時に画面に写っていた住民IDを事実として記録し、住民辞典のページに貼る。名前・感情・文字の意味は解析しない。
    /// </summary>
    internal sealed class TabletCamera
    {
        private readonly TabletSystem sys;
        private CameraViewfinderWindow window;
        private string returnApp;

        public bool Active { get; private set; }
        public string TargetResidentId { get; private set; }
        public float FlashUntil { get; private set; }

        public TabletCamera(TabletSystem sys) { this.sys = sys; }

        public void Begin(string residentId, string returnAppId)
        {
            if (Active) return;
            TargetResidentId = residentId;
            returnApp = returnAppId;
            sys.Close();
            Active = true;
            window = new CameraViewfinderWindow(this);
            UIManager.Open(window);
        }

        public void Tick()
        {
            if (!Active) return;
            if (window == null || !window.IsOpen)
            {
                Active = false;
                return;
            }
            if (!UIManager.TextFieldFocused && GameInput.KeyDown(TabletTuning.CameraShutterKey)) Shoot();
        }

        /// <summary>撮影をやめる（reopen=true ならタブレットに戻る）。</summary>
        public void Cancel(bool reopen)
        {
            if (!Active) return;
            Active = false;
            var w = window;
            window = null;
            if (w != null && w.IsOpen) UIManager.Close(w);
            if (reopen) sys.Open(returnApp);
        }

        /// <summary>ファインダーが Esc などで閉じられた。</summary>
        internal void OnWindowClosed(CameraViewfinderWindow w)
        {
            if (w != window) return;
            window = null;
            if (!Active) return;
            Active = false;
            sys.Open(returnApp);
        }

        public void Shoot()
        {
            if (!Active) return;
            var cam = Camera.main;
            if (cam == null)
            {
                UIManager.Toast(Loc.T("カメラが使えません", "The camera is unavailable"));
                return;
            }
            int w = TabletTuning.PhotoWidth;
            int h = Mathf.Max(1, Mathf.RoundToInt(w * (float)Screen.height / Mathf.Max(1, Screen.width)));
            Texture2D tex = null;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
            if (tex == null)
            {
                UIManager.Toast(Loc.T("うまく撮れませんでした", "The photo failed"));
                return;
            }

            var visible = FindVisibleResidents(cam);
            var player = Game.Instance?.LocalPlayer;
            var place = TabletText.PlaceOf(player?.CurrentAreaId, player != null ? player.Position : cam.transform.position);
            var photo = new PhotoInfo
            {
                id = Ids.New(),
                day = sys.Today,
                minute = sys.NowMinute,
                areaId = player?.CurrentAreaId,
                placeJa = place.ja,
                placeEn = place.en,
                residentIds = visible
            };
            string world = SafeName(sys.Know.worldId ?? Game.Instance?.Planet?.worldId ?? "planet");
            photo.fileName = "photos/" + world + "/" + photo.id + ".png";
            try
            {
                var full = Path.Combine(Application.persistentDataPath, photo.fileName);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tablet] 写真を保存できません: " + e.Message);
                UIManager.Toast(Loc.T("写真をファイルに保存できませんでした（この回だけ表示されます）", "Could not save the photo file (shown this session only)"));
            }
            TabletGui.RememberPhotoTexture(photo.id, tex);
            sys.Know.photos.Add(photo);

            // 資料ボックスへ（事実：いつ・どこで・だれが写っていたか）
            var mat = new TabletMaterial
            {
                id = "photo:" + photo.id,
                kind = TabletMaterialKind.Photo,
                titleJa = "写真",
                titleEn = "Photo",
                factJa = visible.Count > 0 ? $"{place.ja}で撮った写真（住民が{visible.Count}人写っている）" : $"{place.ja}で撮った写真",
                factEn = visible.Count > 0 ? $"Photo taken at {place.en} ({visible.Count} resident(s) in it)" : $"Photo taken at {place.en}",
                day = photo.day,
                minuteOfDay = photo.minute,
                linkedResidentId = visible.Count == 1 ? visible[0] : null
            };
            mat.relatedIds.AddRange(visible);
            sys.AddMaterial(mat);
            foreach (var rid in visible)
            {
                var page = sys.GetOrCreatePage(rid);
                if (!page.photoIds.Contains(photo.id)) page.photoIds.Add(photo.id);
                sys.AddObservation(page, "photo", $"写真を撮った（{place.ja}）", $"Took a photo ({place.en})", null);
            }

            FlashUntil = UnityEngine.Time.unscaledTime + 0.25f;
            if (!string.IsNullOrEmpty(TargetResidentId) && !visible.Contains(TargetResidentId))
                UIManager.Toast(Loc.Format("写真を撮りました（{0} は写っていないようです）", "Photo taken ({0} doesn't seem to be in it)", sys.ResidentLabel(TargetResidentId)));
            else UIManager.Toast(Loc.T("📷 写真を撮りました", "📷 Photo taken"));

            Active = false;
            var wnd = window;
            window = null;
            if (wnd != null && wnd.IsOpen) UIManager.Close(wnd);
            sys.Open(MaterialsApp.Id);
            sys.MaterialsApp.ShowMaterial(mat.id);
        }

        /// <summary>画面に写っていて、遮られていない住民（事実として記録する）。</summary>
        private static List<string> FindVisibleResidents(Camera cam)
        {
            var list = new List<string>();
            var dir = Game.Instance?.Service<IResidentDirectory>();
            if (dir == null) return list;
            var avatar = Game.Instance.LocalPlayer?.Avatar;
            float m = TabletTuning.PhotoViewportMargin;
            foreach (var r in dir.ActiveResidents)
            {
                if (r == null) continue;
                var p = r.Position + Vector3.up * 0.8f;
                var vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0.2f || vp.z > TabletTuning.PhotoResidentMaxDistance) continue;
                if (vp.x < m || vp.x > 1f - m || vp.y < m || vp.y > 1f - m) continue;
                var from = cam.transform.position;
                var d = p - from;
                float dist = d.magnitude;
                if (dist > 0.5f && Physics.Raycast(from, d / dist, out var hit, dist - 0.4f, ~0, QueryTriggerInteraction.Ignore))
                {
                    bool own = r.Transform != null && hit.transform.IsChildOf(r.Transform);
                    bool isPlayer = avatar != null && hit.transform.IsChildOf(avatar);
                    if (!own && !isPlayer) continue;
                }
                list.Add(r.Id);
            }
            return list;
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "planet";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            var r = new string(chars);
            return r.Length > 16 ? r.Substring(0, 16) : r;
        }
    }

    /// <summary>撮影中の小さな操作窓（モーダルではないので、歩いたりカメラを回したりして構図を決められる）。</summary>
    internal sealed class CameraViewfinderWindow : UIWindow
    {
        private readonly TabletCamera camera;

        public CameraViewfinderWindow(TabletCamera camera) { this.camera = camera; }

        public override string Title => Loc.T("📷 カメラ", "📷 Camera");
        public override bool IsModal => false;
        public override Vector2 Size => new Vector2(680, 160);
        public override Vector2? Position => new Vector2((UIStyles.VirtualWidth - 680f) * 0.5f, 16f);

        public override void OnClose()
        {
            camera.OnWindowClosed(this);
        }

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.Format("歩いたりカメラを回したりして構図を決め、[{0}] で撮影。Esc でタブレットに戻ります。", "Frame your shot, then press [{0}]. Esc returns to the tablet.", TabletTuning.CameraShutterKey), UIStyles.LabelSmall);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("📷 撮影", "📷 Shoot"), UIStyles.Button, GUILayout.Width(200), GUILayout.Height(44))) camera.Shoot();
            if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Width(160), GUILayout.Height(44))) camera.Cancel(true);
            GUILayout.EndHorizontal();
        }
    }
}
