using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Hone.Sandbox.Experiments
{
    // Issue #13（テーマとテキスト設定の前提の検証）の実験用。判定はこのログ（[ThemeExpProbe]）を正とし、スクリーンショットは補助に使う。
    //   case 1: .tss の @import の絶対パス（Abs）と相対パス（Rel）で、別ディレクトリの USS の :root 変数がコンポーネント USS の var() から読めるか
    //   case 2: 既定テーマのみの Button に Focus() したとき、見た目が変わるか（resolvedStyle とスクリーンショットのピクセル差）
    //   case 3: 何も指定しない Label が使う FontAsset の atlas population mode（内部 API を reflection で読む。実験用）
    //   case 4: 何も指定しない Label に日本語を入れたときの描画、警告、フレーム時間
    // Player では -themeexp-out <dir> を渡すとスクリーンショットを <dir> に保存して終了する。
    // 測定の前提が崩れたとき（root 未取得、要素の欠落、撮影失敗）は LogError にし、-themeexp-out 指定時は終了コード 1 で終了する。
    public class ThemeExpProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const float MaxWaitSeconds = 30f;
        const int SettleFrames = 10;
        const int BaselineFrames = 30;
        const int RegionMargin = 4;
        const int AfterFrames = 10;
        static readonly Color32 ThemedColor = new Color32(0, 160, 80, 255);
        static readonly Color32 UnresolvedColor = new Color32(220, 38, 38, 255);

        [SerializeField] PanelRenderer m_Abs;
        [SerializeField] PanelRenderer m_Rel;
        [SerializeField] PanelRenderer m_Default;

        VisualElement m_AbsRoot;
        VisualElement m_RelRoot;
        VisualElement m_DefaultRoot;
        bool m_Failed;
        int m_LogCount;
        int m_WarningCount;
        int m_ErrorCount;
        readonly List<string> m_Messages = new List<string>();

        void Awake()
        {
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            m_Abs.RegisterUIReloadCallback((pr, root, version) => m_AbsRoot = root);
            m_Rel.RegisterUIReloadCallback((pr, root, version) => m_RelRoot = root);
            m_Default.RegisterUIReloadCallback((pr, root, version) => m_DefaultRoot = root);
            Application.logMessageReceived += OnLog;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition.StartsWith("[ThemeExpProbe]"))
                return;
            m_LogCount++;
            if (type == LogType.Warning)
                m_WarningCount++;
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                m_ErrorCount++;
            if (m_Messages.Count < 20)
                m_Messages.Add($"{type}: {condition}");
        }

        IEnumerator Start()
        {
            Log($"isEditor={Application.isEditor} unity={Application.unityVersion} platform={Application.platform} dev={Debug.isDebugBuild}");

            var hasOut = TryGetArg("-themeexp-out", out var outDir);
            if (hasOut && outDir == null)
            {
                Debug.LogError("[ThemeExpProbe] -themeexp-out needs a path");
                Application.Quit(1);
                yield break;
            }

            var frames = 0;
            while (frames < MaxWaitFrames && !RootsReady())
            {
                frames++;
                yield return null;
            }
            // スプラッシュのフェード中は画面全体の色が変わり、スクリーンショットの差分が測れない
            var deadline = Time.realtimeSinceStartup + MaxWaitSeconds;
            while (!UnityEngine.Rendering.SplashScreen.isFinished && Time.realtimeSinceStartup < deadline)
                yield return null;
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            Log($"roots abs={m_AbsRoot != null} rel={m_RelRoot != null} default={m_DefaultRoot != null} waitedFrames={frames}");

            if (m_AbsRoot == null || m_RelRoot == null || m_DefaultRoot == null)
            {
                Fail("UI reload callback was not invoked for every PanelRenderer");
            }
            else
            {
                ReportCase1("abs", m_AbsRoot);
                ReportCase1("rel", m_RelRoot);
                ReportCase3();
                yield return Case2(outDir);
                yield return Case4(outDir);
            }

            Log($"unexpected logs total={m_LogCount} warnings={m_WarningCount} errors={m_ErrorCount}");
            foreach (var m in m_Messages)
                Log($"  log: {m}");

            if (hasOut)
                Application.Quit(m_Failed ? 1 : 0);
        }

        bool RootsReady() => m_AbsRoot != null && m_RelRoot != null && m_DefaultRoot != null;

        void Fail(string message)
        {
            Debug.LogError($"[ThemeExpProbe] {message}");
            m_Failed = true;
        }

        static void Log(string message) => Debug.Log($"[ThemeExpProbe] {message}");

        // ---- case 1 ----
        void ReportCase1(string panel, VisualElement root)
        {
            var themed = root.Q<VisualElement>("themed");
            var applied = root.Q<VisualElement>("applied");
            var undef = root.Q<VisualElement>("undefined");
            if (themed == null || applied == null || undef == null)
            {
                Fail($"case1 {panel}: element not found (themed={themed != null} applied={applied != null} undefined={undef != null})");
                return;
            }

            var t = (Color32)themed.resolvedStyle.backgroundColor;
            var a = (Color32)applied.resolvedStyle.backgroundColor;
            var u = (Color32)undef.resolvedStyle.backgroundColor;
            // applied は .exp-probe だけ（変数を使わない）。UnresolvedColor なら component USS は効いている。
            // themed が ThemedColor なら「:root の変数が component USS の var() から読めた」。
            // 未解決の var() は赤に戻らず、プロパティが初期値（transparent）になる。
            var uss = Same(a, UnresolvedColor) ? "component uss applied" : "COMPONENT_USS_NOT_APPLIED";
            var verdict = Same(t, ThemedColor) ? "VAR_RESOLVED" : "VAR_UNRESOLVED";
            Log($"CASE1 panel={panel} applied={a} themed={t} undefined={u} verdict={verdict} ({uss})");
        }

        static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        // ---- case 3 ----
        void ReportCase3()
        {
            var plain = m_DefaultRoot.Q<Label>("plain");
            if (plain == null)
            {
                Fail("case3: label 'plain' not found");
                return;
            }

            var def = plain.resolvedStyle.unityFontDefinition;
            Log($"CASE3 plain.resolvedStyle.unityFontDefinition fontAsset={(def.fontAsset != null ? def.fontAsset.name : "null")} font={(def.font != null ? def.font.name : "null")}");

            // 以下は内部 API を reflection で読む。PanelSettings.textSettings が null のときに使われる既定値を辿る。
            var settings = m_Default.panelSettings;
            Log($"CASE3 panelSettings.textSettings={(settings.textSettings != null ? settings.textSettings.name : "null")}");
            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var ts = typeof(PanelTextSettings).GetProperty("defaultPanelTextSettings", F)?.GetValue(null) as TextSettings;
            if (ts == null)
            {
                Fail("case3: PanelTextSettings.defaultPanelTextSettings not available");
                return;
            }

            Log($"CASE3 defaultPanelTextSettings.defaultFontAssetPath={ts.defaultFontAssetPath} fallbackFontAssets={(ts.fallbackFontAssets == null ? "null" : ts.fallbackFontAssets.Count.ToString())}");
            DescribeFont("CASE3 TextSettings.GetDefaultFont()", typeof(TextSettings).GetMethod("GetDefaultFont", F)?.Invoke(ts, null) as FontAsset);
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var legacyAsset = typeof(TextSettings).GetMethod("GetLegacyRuntimeFontAsset", F)?.Invoke(ts, new object[] { legacy, false }) as FontAsset;
            DescribeFont($"CASE3 TextSettings.GetLegacyRuntimeFontAsset({(legacy != null ? legacy.name : "null")})", legacyAsset);
        }

        static void DescribeFont(string label, FontAsset fa)
        {
            if (fa == null)
            {
                Log($"{label}=null");
                return;
            }

            var fallbacks = new List<string>();
            if (fa.fallbackFontAssetTable != null)
                foreach (var f in fa.fallbackFontAssetTable)
                    fallbacks.Add(f != null ? $"{f.faceInfo.familyName}({f.atlasPopulationMode})" : "null");
            Log($"{label}: name={fa.name} atlasPopulationMode={fa.atlasPopulationMode} sourceFontFile={(fa.sourceFontFile != null ? fa.sourceFontFile.name : "null")} family={fa.faceInfo.familyName} style={fa.faceInfo.styleName} renderMode={fa.atlasRenderMode} fallbacks=[{string.Join(", ", fallbacks)}]");
        }

        // ---- case 2 ----
        IEnumerator Case2(string outDir)
        {
            var btn = m_DefaultRoot.Q<Button>("btn");
            var btn2 = m_DefaultRoot.Q<Button>("btn2");
            if (btn == null || btn2 == null)
            {
                Fail("case2: button not found");
                yield break;
            }

            yield return new WaitForEndOfFrame();
            var before = Capture();
            yield return null;
            yield return new WaitForEndOfFrame();
            var beforeAgain = Capture();
            LogButton("unfocused", btn);
            var noise = Diff(before, beforeAgain);
            Log($"CASE2 noise(unfocused vs unfocused) diffPixels={noise.Count}");
            // panel 座標（左上原点）の worldBound を画面のピクセルに直す。テクスチャの行は下から数える
            var panelBound = btn.panel.visualTree.worldBound;
            var scale = before.width / panelBound.width;
            var wb = btn.worldBound;
            var region = new RectInt(
                Mathf.Max(0, Mathf.FloorToInt(wb.x * scale) - RegionMargin),
                Mathf.Max(0, before.height - Mathf.CeilToInt(wb.yMax * scale) - RegionMargin),
                Mathf.CeilToInt(wb.width * scale) + 2 * RegionMargin,
                Mathf.CeilToInt(wb.height * scale) + 2 * RegionMargin);
            Log($"CASE2 scale={scale:F3} btn region(bottom-left origin, px)={region}");

            btn.Focus();
            // フォーカス変更は非同期なので、数 frame 待ってから測る
            for (var i = 0; i < 3; i++)
                yield return null;
            yield return new WaitForEndOfFrame();
            var focused = Capture();
            var focusedElement = btn.panel.focusController.focusedElement;
            Log($"CASE2 focusedElement={(focusedElement is VisualElement ve ? ve.name : focusedElement?.ToString() ?? "null")} isBtn={ReferenceEquals(focusedElement, btn)}");
            if (!ReferenceEquals(focusedElement, btn))
                Fail("case2: btn did not receive focus");
            LogButton("focused", btn);
            LogButton("control(btn2)", btn2);

            var d = Diff(before, focused);
            Log($"CASE2 diff(unfocused vs focused) whole screen diffPixels={d.Count} bbox=({d.MinX},{d.MinY})-({d.MaxX},{d.MaxY}) screen={before.width}x{before.height}");
            var dRegion = Diff(before, focused, region);
            var nRegion = Diff(before, beforeAgain, region);
            Log($"CASE2 diff inside btn region: focused vs unfocused={dRegion.Count} px, unfocused vs unfocused(noise)={nRegion.Count} px; outside btn region: {d.Count - dRegion.Count} px");
            if (d.Count > 0)
            {
                // テクスチャの行は下から数える。左上原点に直して btn.worldBound（panel 座標）と比べる。
                Log($"CASE2 diff bbox top-left origin=({d.MinX},{before.height - 1 - d.MaxY})-({d.MaxX},{before.height - 1 - d.MinY}) btn.worldBound={btn.worldBound}");
            }

            if (outDir != null)
            {
                Save(outDir, "case2-unfocused.png", before);
                Save(outDir, "case2-focused.png", focused);
            }
            Destroy(before);
            Destroy(beforeAgain);
            Destroy(focused);
        }

        static void LogButton(string state, Button b)
        {
            var s = b.resolvedStyle;
            Log($"CASE2 {state}: bg={(Color32)s.backgroundColor} color={(Color32)s.color} border(top)={(Color32)s.borderTopColor}/{s.borderTopWidth} border(left)={(Color32)s.borderLeftColor}/{s.borderLeftWidth} radius={s.borderTopLeftRadius} tint={(Color32)s.unityBackgroundImageTintColor}");
        }

        // ---- case 4 ----
        IEnumerator Case4(string outDir)
        {
            var host = m_DefaultRoot.Q<VisualElement>("jp-host");
            if (host == null)
            {
                Fail("case4: jp-host not found");
                yield break;
            }

            // Profiler の UI Toolkit / TextCore 系マーカーを列挙して、あれば録る
            var recorders = StartRecorders();

            // ベースライン: 追加前のフレーム時間
            var baseline = new List<float>();
            for (var i = 0; i < BaselineFrames; i++)
            {
                yield return null;
                baseline.Add(Time.unscaledDeltaTime * 1000f);
            }
            baseline.Sort();
            Log($"CASE4 baseline frame ms median={baseline[baseline.Count / 2]:F2} max={baseline[baseline.Count - 1]:F2} frames={baseline.Count}");

            // 段 1: 日本語（未収録文字の初回）。段 2: 別の日本語。段 3: ASCII の新しい文字（対照）
            yield return AddLabelAndMeasure(host, "jp-1", "はじめる", recorders);
            yield return AddLabelAndMeasure(host, "jp-2", "設定を保存", recorders);
            yield return AddLabelAndMeasure(host, "ascii", "Zzz Qxj 987", recorders);

            yield return new WaitForEndOfFrame();
            if (outDir != null)
            {
                var full = Capture();
                Save(outDir, "case4-full.png", full);
                Destroy(full);
            }

            foreach (var r in recorders)
                r.Value.Dispose();
        }

        IEnumerator AddLabelAndMeasure(VisualElement host, string name, string text, List<KeyValuePair<string, ProfilerRecorder>> recorders)
        {
            var label = new Label(text) { name = name };
            label.style.color = new Color(0.98f, 0.98f, 0.98f);
            label.style.fontSize = 24;
            var sw = Stopwatch.StartNew();
            host.Add(label);
            var samples = new List<string>();
            for (var i = 0; i < AfterFrames; i++)
            {
                yield return null;
                // yield return null の直後（次の frame の Update 後）。追加した frame の処理時間は 1 つ前の frame の値に載る
                var ms = Time.unscaledDeltaTime * 1000f;
                samples.Add($"+{i + 1}:{ms:F1}ms");
            }
            sw.Stop();
            Log($"CASE4 add '{name}' text='{text}' afterFrames(ms)=[{string.Join(", ", samples)}] wall(add..+{AfterFrames})={sw.ElapsedMilliseconds}ms layout={label.layout.size} resolvedFont={(label.resolvedStyle.unityFontDefinition.fontAsset != null ? label.resolvedStyle.unityFontDefinition.fontAsset.name : "null")}");
            LogRecorders(name, recorders);
        }

        // ProfilerRecorder は 1 frame 1 サンプル（SumAllSamplesInFrame）。ラベル追加後の AfterFrames frame での最大値のうち、0.1ms 以上のものだけ出す
        static void LogRecorders(string name, List<KeyValuePair<string, ProfilerRecorder>> recorders)
        {
            var lines = new List<string>();
            foreach (var kv in recorders)
            {
                var r = kv.Value;
                if (!r.Valid || r.Count == 0)
                    continue;
                long max = 0;
                for (var i = Math.Max(0, r.Count - AfterFrames); i < r.Count; i++)
                    max = Math.Max(max, r.GetSample(i).Value);
                if (max >= 100000)
                    lines.Add($"{kv.Key}={max / 1000000.0:F2}ms");
            }
            Log($"CASE4 markers >=0.1ms in the {AfterFrames} frames after '{name}' (of {recorders.Count}): {(lines.Count > 0 ? string.Join(", ", lines) : "none")}");
        }

        static List<KeyValuePair<string, ProfilerRecorder>> StartRecorders()
        {
            var result = new List<KeyValuePair<string, ProfilerRecorder>>();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var re = new Regex("Layout|Font|Text|Glyph|UIR|UIElements|Atlas", RegexOptions.IgnoreCase);
            var names = new List<string>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !d.Category.Name.StartsWith("UI") || !re.IsMatch(d.Name))
                    continue;
                var key = $"{d.Category.Name}/{d.Name}";
                names.Add(key);
                result.Add(new KeyValuePair<string, ProfilerRecorder>(key, new ProfilerRecorder(h, 60, ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame)));
            }
            Log($"CASE4 profiler markers matched={names.Count} available={handles.Count}");
            return result;
        }

        // ---- screenshot helpers ----
        static Texture2D Capture() => ScreenCapture.CaptureScreenshotAsTexture();

        readonly struct DiffResult
        {
            public readonly int Count;
            public readonly int MinX, MinY, MaxX, MaxY;

            public DiffResult(int count, int minX, int minY, int maxX, int maxY)
            {
                Count = count;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }

        static DiffResult Diff(Texture2D a, Texture2D b) => Diff(a, b, new RectInt(0, 0, a.width, a.height));

        static DiffResult Diff(Texture2D a, Texture2D b, RectInt region)
        {
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
            if (pa.Length != pb.Length)
                return new DiffResult(-1, 0, 0, 0, 0);
            int count = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (var i = 0; i < pa.Length; i++)
            {
                if (pa[i].r == pb[i].r && pa[i].g == pb[i].g && pa[i].b == pb[i].b)
                    continue;
                int x = i % a.width, y = i / a.width;
                if (!region.Contains(new Vector2Int(x, y)))
                    continue;
                count++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            return new DiffResult(count, minX, minY, maxX, maxY);
        }

        void Save(string dir, string file, Texture2D tex)
        {
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Fail($"failed to save screenshot '{file}': {e.Message}");
            }
        }

        // フラグが無ければ false。フラグがあって値が無い（末尾、または次が別のフラグ）ときは true で value = null
        static bool TryGetArg(string key, out string value)
        {
            value = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] != key)
                    continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    value = args[i + 1];
                return true;
            }
            return false;
        }
    }
}
