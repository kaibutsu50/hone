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
    // sandbox の Assets/Hone/hone.manifest.json はコミット済みの状態なので使わない
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
            world.sortingOrder = 7;

            var changes = HoneSync.Run(new[] { screen, world }, Dir);

            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            Assert.That(textSettings, Is.Not.Null);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            foreach (var panel in new[] { screen, world })
            {
                Assert.That(panel.themeStyleSheet, Is.SameAs(theme));
                Assert.That(panel.textSettings, Is.SameAs(textSettings));
            }
            Assert.That(world.sortingOrder, Is.EqualTo(7));
            Assert.That(changes, Is.EqualTo(3));
        }

        [Test]
        public void B_SecondRunChangesNothingAndLogsNoChanges()
        {
            CopyFont("Roboto-Regular.ttf");
            WriteManifest($"[{{ \"lang\": \"x\", \"family\": \"X\", \"files\": [\"{Dir}/Roboto-Regular.ttf\"] }}]");
            var panel = CreatePanelSettings("Screen");
            HoneSync.Run(new[] { panel }, Dir);
            var before = ReadAll();

            LogAssert.Expect(LogType.Log, new Regex("変更なし"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(ReadAll(), Is.EqualTo(before));
        }

        [Test]
        public void C_WithoutManifestCreatesNothing()
        {
            var panel = CreatePanelSettings("Screen");
            // Editor で作った PanelSettings には、作った時点で Unity が theme を入れることがある。値ではなく変化しないことを見る
            var theme = panel.themeStyleSheet;
            var textSettings = panel.textSettings;

            LogAssert.Expect(LogType.Error, new Regex("hone.manifest.json がありません"));
            var changes = HoneSync.Run(new[] { panel }, Dir);

            Assert.That(changes, Is.EqualTo(0));
            Assert.That(File.Exists(TextSettingsPath), Is.False);
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
            }
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            Assert.That(textSettings.fallbackFontAssets.ConvertAll(f => AssetDatabase.GetAssetPath(f)),
                Is.EqualTo(System.Array.ConvertAll(expected, n => $"{Dir}/{n}.asset")));
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
