using System.IO;
using System.Text.RegularExpressions;
using Hone.Core.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Tests
{
    // 一時ディレクトリ（Assets/ 直下）に manifest と PanelSettings を作り、HoneSync.Run を直接呼ぶ。
    // sandbox の Assets/Hone/hone.manifest.json は、コミット済みのファイルを書き換えないよう使わない。
    // theme（Assets/Hone/HoneTheme.tss）とフォント（Sandbox/Fonts/RobotoMono-Regular.ttf）は sandbox の実ファイルを読む
    public class HoneSyncTests
    {
        const string Dir = "Assets/HoneSyncTestsTmp";
        const string ThemePath = "Assets/Hone/HoneTheme.tss";
        const string SourceFont = "Assets/Hone/Sandbox/Fonts/RobotoMono-Regular.ttf";
        const string TextSettingsPath = Dir + "/HonePanelTextSettings.asset";

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets", "HoneSyncTestsTmp");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Dir);
        }

        [Test]
        public void A_CreatesTextSettingsAndAssignsThemeAndTextSettingsToAllTargets()
        {
            WriteManifest("[]");
            var screen = CreatePanelSettings("Screen");
            var world = CreatePanelSettings("World");
            world.renderMode = PanelRenderMode.WorldSpace;
            world.sortingOrder = 7;
            EditorUtility.SetDirty(world);
            AssetDatabase.SaveAssets();

            var changes = HoneSync.Run(new[] { screen, world }, Dir);

            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            Assert.That(textSettings, Is.Not.Null);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            foreach (var panel in new[] { screen, world })
            {
                Assert.That(panel.themeStyleSheet, Is.SameAs(theme));
                Assert.That(panel.textSettings, Is.SameAs(textSettings));
                // メモリ上だけでなく保存されている
                Assert.That(EditorUtility.IsDirty(panel), Is.False);
            }
            Assert.That(world.sortingOrder, Is.EqualTo(7));
            Assert.That(world.renderMode, Is.EqualTo(PanelRenderMode.WorldSpace));
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void B_SecondRunChangesNothingAndLogsNoChanges()
        {
            CopyFont("RobotoMono-Regular.ttf");
            WriteManifest($"[{{ \"lang\": \"x\", \"family\": \"X\", \"files\": [\"{Dir}/RobotoMono-Regular.ttf\"] }}]");
            var panel = CreatePanelSettings("Screen");
            HoneSync.Run(new[] { panel }, Dir);
            var before = ReadAll();

            LogAssert.Expect(LogType.Log, new Regex("変更なし"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            Assert.That(textSettings.fallbackFontAssets.Count, Is.EqualTo(1));
            Assert.That(EditorUtility.IsDirty(textSettings), Is.False);
            Assert.That(EditorUtility.IsDirty(panel), Is.False);
        }

        [Test]
        public void C_WithoutManifestCreatesNothing()
        {
            var panel = CreatePanelSettings("Screen");
            // Editor で作った PanelSettings には、作った時点で Unity が theme を入れることがある。値ではなく変化しないことを見る
            var theme = panel.themeStyleSheet;
            var textSettings = panel.textSettings;
            var before = ReadAll();

            LogAssert.Expect(LogType.Error, new Regex("hone.manifest.json がありません"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
            Assert.That(panel.themeStyleSheet, Is.SameAs(theme));
            Assert.That(panel.textSettings, Is.SameAs(textSettings));
        }

        [Test]
        public void D_CreatesDynamicFontAssetPerFileAndAddsFallbacksInManifestOrder()
        {
            CopyFont("Zeta-Regular.ttf");
            CopyFont("Alpha-Regular.ttf");
            CopyFont("Alpha-Bold.ttf");
            WriteManifest(
                $"[{{ \"lang\": \"z\", \"family\": \"Zeta\", \"files\": [\"{Dir}/Zeta-Regular.ttf\"] }}," +
                $" {{ \"lang\": \"a\", \"family\": \"Alpha\", \"files\": [\"{Dir}/Alpha-Regular.ttf\", \"{Dir}/Alpha-Bold.ttf\"] }}]");
            var panel = CreatePanelSettings("Screen");

            HoneSync.Run(new[] { panel }, Dir);

            var expected = new[] { "Zeta-Regular SDF", "Alpha-Regular SDF", "Alpha-Bold SDF" };
            foreach (var name in expected)
            {
                var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/{name}.asset");
                Assert.That(fontAsset, Is.Not.Null, name);
                Assert.That(fontAsset.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic), name);
                Assert.That(fontAsset.isMultiAtlasTexturesEnabled, Is.True, name);
                // atlas と material がサブアセットとして保存されている（外れていると開き直した後に描画されない）
                Assert.That(AssetDatabase.IsSubAsset(fontAsset.atlasTextures[0]), Is.True, name);
                Assert.That(AssetDatabase.IsSubAsset(fontAsset.material), Is.True, name);
                Assert.That(EditorUtility.IsDirty(fontAsset), Is.False, name);
            }
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            Assert.That(textSettings.fallbackFontAssets.ConvertAll(f => AssetDatabase.GetAssetPath(f)),
                Is.EqualTo(System.Array.ConvertAll(expected, n => $"{Dir}/{n}.asset")));
            Assert.That(EditorUtility.IsDirty(textSettings), Is.False);
        }

        [Test]
        public void ExistingFontAssetNotInFallbackIsAppendedAfterUserFallbacks()
        {
            CopyFont("RobotoMono-Regular.ttf");
            CopyFont("User-Regular.ttf");
            WriteManifest($"[{{ \"lang\": \"u\", \"family\": \"U\", \"files\": [\"{Dir}/User-Regular.ttf\"] }}]");
            var panel = CreatePanelSettings("Screen");
            HoneSync.Run(new[] { panel }, Dir);
            var userFont = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/User-Regular SDF.asset");
            // 利用者が fallback を自分で並べ替えた状態から、manifest のフォントを足す
            WriteManifest($"[{{ \"lang\": \"x\", \"family\": \"X\", \"files\": [\"{Dir}/RobotoMono-Regular.ttf\"] }}]");
            HoneSync.Run(new[] { panel }, Dir);
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            var roboto = AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/RobotoMono-Regular SDF.asset");
            textSettings.fallbackFontAssets.Remove(roboto);
            EditorUtility.SetDirty(textSettings);
            AssetDatabase.SaveAssets();

            HoneSync.Run(new[] { panel }, Dir);

            Assert.That(textSettings.fallbackFontAssets, Is.EqualTo(new[] { userFont, roboto }));
            Assert.That(AssetDatabase.LoadAssetAtPath<FontAsset>($"{Dir}/RobotoMono-Regular SDF.asset"), Is.SameAs(roboto));
        }

        [Test]
        public void TextSettingsPathOccupiedByAnotherAssetIsNotOverwritten()
        {
            WriteManifest("[]");
            var panel = CreatePanelSettings("Screen");
            var other = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(other, TextSettingsPath);
            var textSettings = panel.textSettings;
            var before = ReadAll();

            LogAssert.Expect(LogType.Error, new Regex("PanelTextSettings ではありません"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
            Assert.That(panel.textSettings, Is.SameAs(textSettings));
        }

        [Test]
        public void UnreadableThemeChangesNothing()
        {
            File.WriteAllText($"{Dir}/hone.manifest.json", "{ \"version\": 1, \"theme\": \"Assets/Missing.tss\", \"fonts\": [] }\n");
            AssetDatabase.ImportAsset($"{Dir}/hone.manifest.json");
            var panel = CreatePanelSettings("Screen");
            var before = ReadAll();

            LogAssert.Expect(LogType.Error, new Regex("theme が指す .tss を読めません"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
        }

        [TestCase("", "が空です")]
        [TestCase("{ \"theme\": ", "JSON として読めませんでした")]
        public void EmptyOrInvalidManifestChangesNothing(string text, string message)
        {
            File.WriteAllText($"{Dir}/hone.manifest.json", text);
            AssetDatabase.ImportAsset($"{Dir}/hone.manifest.json");
            var panel = CreatePanelSettings("Screen");
            var before = ReadAll();

            LogAssert.Expect(LogType.Error, new Regex(message));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
        }

        static void WriteManifest(string fonts)
        {
            File.WriteAllText($"{Dir}/hone.manifest.json",
                $"{{ \"version\": 1, \"theme\": \"{ThemePath}\", \"fonts\": {fonts} }}\n");
            AssetDatabase.ImportAsset($"{Dir}/hone.manifest.json");
        }

        static PanelSettings CreatePanelSettings(string name)
        {
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(panel, $"{Dir}/{name}.asset");
            return panel;
        }

        static void CopyFont(string fileName)
        {
            Assert.That(AssetDatabase.CopyAsset(SourceFont, $"{Dir}/{fileName}"), Is.True);
        }

        // 一時ディレクトリの全ファイルの内容（.meta を含む）。変更がないことの判定に使う
        static string[] ReadAll()
        {
            var files = Directory.GetFiles(Dir);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            return System.Array.ConvertAll(files, f => f + "\n" + System.Convert.ToBase64String(File.ReadAllBytes(f)));
        }
    }
}
