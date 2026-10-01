using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Hone.Core.Editor
{
    // hone.manifest.json（CLI が書く台帳）を読み、CLI が作れない Unity のアセットを作って割り当てる。
    // 触るのは PanelSettings の themeStyleSheet と textSettings、HonePanelTextSettings.asset、フォントの隣の FontAsset だけ
    public static class HoneSync
    {
        public const string DefaultDirectory = "Assets/Hone";
        const string ManifestFileName = "hone.manifest.json";
        const string TextSettingsFileName = "HonePanelTextSettings.asset";
        const string LogPrefix = "[Hone] Sync: ";

        // Unity の Font Asset の作成メニュー（Create > UI Toolkit > Text > Font Asset > SDF）と同じ値（6000.7）。
        // multi atlas だけはメニュー（無効）と違い有効にする。CJK は 1 枚の atlas に収まらない
        const int SamplingPointSize = 90;
        const int AtlasPadding = 9;
        const int AtlasSize = 1024;

        [Serializable]
        class Manifest
        {
            public string theme;
            public FontEntry[] fonts;
        }

        // files は Unity プロジェクトのルートからの相対パス（Assets/...）
        [Serializable]
        class FontEntry
        {
            public string[] files;
        }

        [MenuItem("Hone/Sync")]
        static void SyncFromMenu()
        {
            if (!File.Exists(ManifestPath(DefaultDirectory)))
            {
                EditorUtility.DisplayDialog("Hone Sync", MissingManifestMessage(DefaultDirectory), "OK");
                return;
            }
            // Packages/ の PanelSettings は書き換えられないので、Assets/ だけを探す
            var panels = AssetDatabase.FindAssets("t:PanelSettings", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<PanelSettings>)
                .Where(p => p != null)
                .ToList();
            if (panels.Count == 0)
            {
                EditorUtility.DisplayDialog("Hone Sync", "PanelSettings がありません", "OK");
                return;
            }
            if (panels.Count == 1)
            {
                Run(panels);
                return;
            }
            HoneSyncWindow.Open(panels);
        }

        // 変更した件数を返す。manifest が無い・読めないときは何もせずにエラーを出し、0 を返す
        public static int Run(IEnumerable<PanelSettings> targets, string directory = DefaultDirectory)
        {
            var manifestPath = ManifestPath(directory);
            if (!File.Exists(manifestPath))
            {
                Debug.LogError(LogPrefix + MissingManifestMessage(directory));
                return 0;
            }
            Manifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{LogPrefix}{manifestPath} を JSON として読めませんでした（{e.Message}）");
                return 0;
            }
            var theme = string.IsNullOrEmpty(manifest?.theme) ? null : AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(manifest.theme);
            if (theme == null)
            {
                Debug.LogError($"{LogPrefix}{manifestPath} の theme が指す .tss を読めません: {manifest?.theme}");
                return 0;
            }
            var textSettingsPath = $"{directory}/{TextSettingsFileName}";
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(textSettingsPath);
            if (textSettings == null && File.Exists(textSettingsPath))
            {
                // CreateAsset は同じパスの既存ファイルを消してから作るので、別の種類のアセットを上書きしない
                Debug.LogError($"{LogPrefix}{textSettingsPath} が PanelTextSettings ではありません");
                return 0;
            }

            var changes = 0;
            if (textSettings == null)
            {
                textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(textSettings, textSettingsPath);
                Log($"作成: {textSettingsPath}");
                changes++;
            }

            foreach (var panel in targets)
            {
                var panelPath = AssetDatabase.GetAssetPath(panel);
                var panelChanged = false;
                if (panel.themeStyleSheet != theme)
                {
                    panel.themeStyleSheet = theme;
                    Log($"設定: {panelPath} の Theme Style Sheet を {manifest.theme} にしました");
                    panelChanged = true;
                }
                else
                {
                    Log($"設定済みのためスキップ: {panelPath} の Theme Style Sheet");
                }
                if (panel.textSettings != textSettings)
                {
                    panel.textSettings = textSettings;
                    Log($"設定: {panelPath} の Text Settings を {textSettingsPath} にしました");
                    panelChanged = true;
                }
                else
                {
                    Log($"設定済みのためスキップ: {panelPath} の Text Settings");
                }
                if (panelChanged)
                {
                    EditorUtility.SetDirty(panel);
                    changes++;
                }
            }

            var textSettingsChanged = false;
            foreach (var entry in manifest.fonts ?? Array.Empty<FontEntry>())
            {
                foreach (var fontPath in entry.files ?? Array.Empty<string>())
                {
                    var fontAsset = LoadOrCreateFontAsset(fontPath, ref changes);
                    if (fontAsset == null)
                        continue;
                    var fontAssetPath = AssetDatabase.GetAssetPath(fontAsset);
                    textSettings.fallbackFontAssets ??= new List<FontAsset>();
                    if (textSettings.fallbackFontAssets.Contains(fontAsset))
                    {
                        Log($"fallback に登録済みのためスキップ: {fontAssetPath}");
                        continue;
                    }
                    textSettings.fallbackFontAssets.Add(fontAsset);
                    Log($"fallback に追加: {fontAssetPath}");
                    textSettingsChanged = true;
                }
            }
            if (textSettingsChanged)
            {
                EditorUtility.SetDirty(textSettings);
                changes++;
            }

            if (changes == 0)
            {
                Log("変更なし");
                return 0;
            }
            AssetDatabase.SaveAssets();
            Log($"完了（{changes} 件のアセットを変更）");
            return changes;
        }

        // <フォントと同じディレクトリ>/<フォントのファイル名> SDF.asset。あれば作らずにそれを使う
        static FontAsset LoadOrCreateFontAsset(string fontPath, ref int changes)
        {
            var name = Path.GetFileNameWithoutExtension(fontPath);
            var directory = Path.GetDirectoryName(fontPath)?.Replace('\\', '/');
            var assetPath = $"{directory}/{name} SDF.asset";
            if (File.Exists(assetPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
                if (existing == null)
                    Debug.LogError($"{LogPrefix}{assetPath} が FontAsset ではありません");
                else
                    Log($"既に存在するためスキップ: {assetPath}");
                return existing;
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogError($"{LogPrefix}フォントを読めません: {fontPath}");
                return null;
            }
            // ATG は Static の FontAsset に対応しないので Dynamic で作る
            var fontAsset = FontAsset.CreateFontAsset(font, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                Debug.LogError($"{LogPrefix}FontAsset を作れませんでした: {fontPath}");
                return null;
            }
            // atlas と material は、本体を CreateAsset した後にサブアセットとして足す（保存前に足さないと参照が外れる）
            AssetDatabase.CreateAsset(fontAsset, assetPath);
            fontAsset.atlasTextures[0].name = $"{name} Atlas";
            fontAsset.material.name = $"{name} Atlas Material";
            AssetDatabase.AddObjectToAsset(fontAsset.atlasTextures[0], fontAsset);
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            Log($"作成: {assetPath}");
            changes++;
            return fontAsset;
        }

        static string ManifestPath(string directory) => $"{directory}/{ManifestFileName}";

        static string MissingManifestMessage(string directory) =>
            $"{ManifestPath(directory)} がありません。先に `npx @kaibutsu50/hone init` を実行してください";

        static void Log(string message) => Debug.Log(LogPrefix + message);
    }

    // PanelSettings が複数あるときに、Sync の対象を選ばせる。既定はどれも選ばない
    // （検証用など、Hone の theme を当てたくない PanelSettings があり得るため）
    sealed class HoneSyncWindow : EditorWindow
    {
        List<PanelSettings> panels;
        bool[] selected;
        Vector2 scroll;

        public static void Open(List<PanelSettings> panels)
        {
            var window = CreateInstance<HoneSyncWindow>();
            window.titleContent = new GUIContent("Hone Sync");
            window.panels = panels;
            window.selected = new bool[panels.Count];
            window.ShowModalUtility();
        }

        void OnGUI()
        {
            if (panels == null)
            {
                Close();
                return;
            }
            EditorGUILayout.LabelField("Sync する PanelSettings を選んでください", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (var i = 0; i < panels.Count; i++)
                selected[i] = EditorGUILayout.ToggleLeft(AssetDatabase.GetAssetPath(panels[i]), selected[i]);
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("キャンセル"))
                    Close();
                using (new EditorGUI.DisabledScope(!selected.Any(s => s)))
                {
                    if (GUILayout.Button("Sync"))
                    {
                        var targets = panels.Where((_, i) => selected[i]).ToList();
                        Close();
                        HoneSync.Run(targets);
                    }
                }
            }
        }
    }
}
