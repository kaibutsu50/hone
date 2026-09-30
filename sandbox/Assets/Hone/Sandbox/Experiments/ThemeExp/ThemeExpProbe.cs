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
    //   case 3: 何も指定しない Label の FontAsset の atlas population mode。既定の TextSettings から辿った FontAsset（内部 API を reflection で読む。実験用）を、
    //           Label の resolvedStyle と並べて記録する。Label が実際に使う FontAsset を直接は特定しない
    //   case 4: 何も指定しない Label に日本語を入れたときの描画、警告、フレーム時間（描画の品質はスクリーンショットで人が見る）
    // Player では -themeexp-out <dir> を渡すとスクリーンショットを <dir> に保存して終了する。Editor の Play Mode では Application.Quit が効かないので終了しない。
    // 測定の前提が崩れたとき（root 未取得、要素の欠落、reflection の失敗、スクリーンショットのサイズ不一致や保存失敗、フォーカス未取得、
    // スプラッシュ待ちのタイムアウト、未処理の例外、Error のログ、120 秒の超過）は LogError にし、-themeexp-out 指定時は終了コード 1 で終了する。
    // 最後に必ず "RESULT OK|FAIL" の行を出す。この行が無いログは、途中で止まったものとして扱う。
    public class ThemeExpProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const float MaxWaitSeconds = 30f;
        const float MaxRunSeconds = 120f;
        const int SettleFrames = 10;
        const int BaselineFrames = 30;
        const int BaselineSkipFrames = 5; // 直前のスクリーンショット保存の処理が乗るので、先頭は捨てる
        const int RegionMargin = 4;
        const int AfterFrames = 10;
        const int MaxMessages = 20;
        static readonly Color32 ThemedColor = new Color32(0, 160, 80, 255);
        // ThemeExpProbe.uss の .exp-probe（変数を使わない対照）の背景色。component USS が当たっていれば applied がこの色になる
        static readonly Color32 ComponentUssColor = new Color32(220, 38, 38, 255);

        [SerializeField] PanelRenderer m_Abs;
        [SerializeField] PanelRenderer m_Rel;
        [SerializeField] PanelRenderer m_Default;

        VisualElement m_AbsRoot;
        VisualElement m_RelRoot;
        VisualElement m_DefaultRoot;
        bool m_Failed;
        bool m_Finished;
        bool m_HasOut;
        float m_WatchdogAt;
        int m_LogCount;
        int m_WarningCount;
        int m_ErrorCount;
        readonly List<string> m_Messages = new List<string>();

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            if (m_Abs == null || m_Rel == null || m_Default == null)
            {
                Fail($"PanelRenderer is not assigned (abs={m_Abs != null} rel={m_Rel != null} default={m_Default != null})");
                return;
            }

            // PanelRenderer の root は public では reload callback 経由でしか取れない
            m_Abs.RegisterUIReloadCallback((pr, root, version) => m_AbsRoot = root);
            m_Rel.RegisterUIReloadCallback((pr, root, version) => m_RelRoot = root);
            m_Default.RegisterUIReloadCallback((pr, root, version) => m_DefaultRoot = root);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void Update()
        {
            if (m_HasOut && !m_Finished && Time.realtimeSinceStartup > m_WatchdogAt)
            {
                Fail($"watchdog: not finished within {MaxRunSeconds}s");
                Finish();
            }
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
            if (m_Messages.Count < MaxMessages)
                m_Messages.Add($"{type}: {condition}");
        }

        IEnumerator Start()
        {
            Log($"isEditor={Application.isEditor} unity={Application.unityVersion} platform={Application.platform} dev={Debug.isDebugBuild} isFocused={Application.isFocused}");

            m_HasOut = TryGetArg("-themeexp-out", out var outDir);
            if (m_HasOut && outDir == null)
            {
                Debug.LogError("[ThemeExpProbe] -themeexp-out needs a path");
                Log("RESULT FAIL");
                Application.Quit(1);
                yield break;
            }

            m_WatchdogAt = Time.realtimeSinceStartup + MaxRunSeconds;
            if (outDir == null)
                Log("screenshots are not saved (no -themeexp-out)");
            yield return Guard(Run(outDir));
            Finish();
        }

        // yield を含むコルーチンは try/catch の中に書けないので、MoveNext を手で回して未処理の例外を Fail にする
        IEnumerator Guard(IEnumerator inner)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                        yield break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    Fail($"unhandled exception: {e}");
                    yield break;
                }
                yield return current;
            }
        }

        IEnumerator Run(string outDir)
        {
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
            if (!UnityEngine.Rendering.SplashScreen.isFinished)
                Fail($"splash did not finish within {MaxWaitSeconds}s; screenshot diff is invalid");
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            Log($"roots abs={m_AbsRoot != null} rel={m_RelRoot != null} default={m_DefaultRoot != null} waitedFrames={frames}");

            if (!RootsReady())
            {
                Fail("UI reload callback was not invoked for every PanelRenderer");
                yield break;
            }

            ReportCase1("abs", m_AbsRoot);
            ReportCase1("rel", m_RelRoot);
            ReportCase3();
            yield return Guard(Case2(outDir));
            yield return Guard(Case4(outDir));
        }

        void Finish()
        {
            if (m_Finished)
                return;
            m_Finished = true;
            Log($"unexpected logs total={m_LogCount} warnings={m_WarningCount} errors={m_ErrorCount}");
            foreach (var m in m_Messages)
                Log($"  log: {m}");
            if (m_LogCount > m_Messages.Count)
                Log($"  (only the first {m_Messages.Count} of {m_LogCount} logs are listed)");
            if (m_ErrorCount > 0)
                m_Failed = true;
            Log($"RESULT {(m_Failed ? "FAIL" : "OK")}");
            if (m_HasOut)
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
            // applied は .exp-probe だけ（変数を使わない）。ComponentUssColor なら component USS は効いている。効いていなければ判定できない。
            // themed が ThemedColor なら「:root の変数が component USS の var() から読めた」。
            // 未解決の var() は、6000.7.0b2 では前の規則の値（赤）に戻らず、プロパティが初期値（transparent）になった（undefined の値）。
            string verdict;
            if (!Same(a, ComponentUssColor))
            {
                verdict = "INVALID(component uss not applied)";
                Fail($"case1 {panel}: component USS was not applied (applied={a})");
            }
            else
            {
                verdict = Same(t, ThemedColor) ? "VAR_RESOLVED" : "VAR_UNRESOLVED";
            }
            Log($"CASE1 panel={panel} applied={a} themed={t} undefined={u} verdict={verdict}");
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
            var prop = typeof(PanelTextSettings).GetProperty("defaultPanelTextSettings", F);
            var getDefault = typeof(TextSettings).GetMethod("GetDefaultFont", F, null, Type.EmptyTypes, null);
            var getLegacy = typeof(TextSettings).GetMethod("GetLegacyRuntimeFontAsset", F, null, new[] { typeof(Font), typeof(bool) }, null);
            if (prop == null || getDefault == null || getLegacy == null)
            {
                Fail($"case3: reflection target not found (API renamed?) defaultPanelTextSettings={prop != null} GetDefaultFont={getDefault != null} GetLegacyRuntimeFontAsset={getLegacy != null}");
                return;
            }

            var ts = prop.GetValue(null) as TextSettings;
            if (ts == null)
            {
                Fail("case3: PanelTextSettings.defaultPanelTextSettings returned null");
                return;
            }

            Log($"CASE3 defaultPanelTextSettings.defaultFontAssetPath={ts.defaultFontAssetPath} fallbackFontAssets={(ts.fallbackFontAssets == null ? "null" : ts.fallbackFontAssets.Count.ToString())}");
            DescribeFont("CASE3 TextSettings.GetDefaultFont()", getDefault.Invoke(ts, null) as FontAsset);
            var legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (legacy == null)
            {
                Fail("case3: built-in font LegacyRuntime.ttf not found");
                return;
            }

            DescribeFont($"CASE3 TextSettings.GetLegacyRuntimeFontAsset({legacy.name})", getLegacy.Invoke(ts, new object[] { legacy, false }) as FontAsset);
        }

        static void DescribeFont(string label, FontAsset fa)
        {
            if (fa == null)
            {
                Log($"{label}=null");
                return;
            }

            // fallback は familyName で出す。Player では runtime 生成の FontAsset の name が空になる（6000.7.0b2）
            var fallbackList = "null";
            if (fa.fallbackFontAssetTable != null)
            {
                var names = new List<string>();
                foreach (var f in fa.fallbackFontAssetTable)
                    names.Add(f != null ? $"{f.faceInfo.familyName}({f.atlasPopulationMode})" : "null");
                fallbackList = $"[{string.Join(", ", names)}]";
            }
            Log($"{label}: name={fa.name} atlasPopulationMode={fa.atlasPopulationMode} sourceFontFile={(fa.sourceFontFile != null ? fa.sourceFontFile.name : "null")} family={fa.faceInfo.familyName} style={fa.faceInfo.styleName} renderMode={fa.atlasRenderMode} fallbacks={fallbackList}");
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
            if (noise.Count > 0)
                Log("CASE2 WARN: the baseline itself is noisy; read the diff below with that in mind");
            // panel 座標（左上原点）の worldBound を画面のピクセルに直す。テクスチャの行は下から数える
            var panelBound = btn.panel.visualTree.worldBound;
            var scale = before.width / panelBound.width;
            var scaleY = before.height / panelBound.height;
            var wb = btn.worldBound;
            var region = new RectInt(
                Mathf.Max(0, Mathf.FloorToInt(wb.x * scale) - RegionMargin),
                Mathf.Max(0, before.height - Mathf.CeilToInt(wb.yMax * scale) - RegionMargin),
                Mathf.CeilToInt(wb.width * scale) + 2 * RegionMargin,
                Mathf.CeilToInt(wb.height * scale) + 2 * RegionMargin);
            Log($"CASE2 scale={scale:F3} scaleY={scaleY:F3} btn region(bottom-left origin, px)={region}");
            var regionValid = scale > 0 && !float.IsInfinity(scale) && Mathf.Abs(scale - scaleY) < 0.01f
                && region.width > 0 && region.height > 0 && region.xMax <= before.width && region.yMax <= before.height;
            if (!regionValid)
                Fail("case2: btn region is not valid (scale, panel coverage or layout); the in-region diff is not reliable");

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
            Log($"CASE2 diff(unfocused vs focused) whole screen diffPixels={d.Count} bbox={d.BoxText} screen={before.width}x{before.height}");
            var dRegion = Diff(before, focused, region);
            var nRegion = Diff(before, beforeAgain, region);
            Log($"CASE2 diff inside btn region: focused vs unfocused={dRegion.Count} px, unfocused vs unfocused(noise)={nRegion.Count} px; outside btn region: {d.Count - dRegion.Count} px");
            if (d.Count > 0 && dRegion.Count == 0)
                Log("CASE2 WARN: the diff is entirely outside the btn region; the region may be misplaced");
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
            try
            {
                // ベースライン: 追加前のフレーム時間
                var baseline = new List<float>();
                for (var i = 0; i < BaselineSkipFrames + BaselineFrames; i++)
                {
                    yield return null;
                    if (i >= BaselineSkipFrames)
                        baseline.Add(Time.unscaledDeltaTime * 1000f);
                }
                baseline.Sort();
                Log($"CASE4 baseline frame ms median={baseline[baseline.Count / 2]:F2} max={baseline[baseline.Count - 1]:F2} frames={baseline.Count}");

                // 段 1: ASCII（最初の動的な追加そのもののコスト。対照）。段 2: 日本語（未収録文字の初回）。段 3: 別の日本語
                yield return Guard(AddLabelAndMeasure(host, "ascii", "Zzz Qxj 987", recorders));
                yield return Guard(AddLabelAndMeasure(host, "jp-1", "はじめる", recorders));
                yield return Guard(AddLabelAndMeasure(host, "jp-2", "設定を保存", recorders));

                yield return new WaitForEndOfFrame();
                if (outDir != null)
                {
                    var full = Capture();
                    Save(outDir, "case4-full.png", full);
                    Destroy(full);
                }
            }
            finally
            {
                foreach (var r in recorders)
                    r.Value.Dispose();
            }
        }

        // ラベルを追加してから AfterFrames frame のフレーム時間を並べる。+1 は追加した frame の処理時間（+0 は無い）
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
            var def = label.resolvedStyle.unityFontDefinition;
            Log($"CASE4 add '{name}' text='{text}' afterFrames(ms)=[{string.Join(", ", samples)}] wall(add..+{AfterFrames})={sw.ElapsedMilliseconds}ms layout={label.layout.size} resolvedFontAsset={(def.fontAsset != null ? def.fontAsset.name : "null")} resolvedFont={(def.font != null ? def.font.name : "null")}");
            LogRecorders(name, recorders);
        }

        // ProfilerRecorder は 1 frame 1 サンプル（SumAllSamplesInFrame）。ラベル追加後の AfterFrames frame での最大値のうち、0.1ms 以上のものだけ出す
        static void LogRecorders(string name, List<KeyValuePair<string, ProfilerRecorder>> recorders)
        {
            var lines = new List<string>();
            var valid = 0;
            var withSamples = 0;
            foreach (var kv in recorders)
            {
                var r = kv.Value;
                if (r.Valid)
                    valid++;
                if (!r.Valid || r.Count == 0)
                    continue;
                withSamples++;
                long max = 0;
                for (var i = Math.Max(0, r.Count - AfterFrames); i < r.Count; i++)
                    max = Math.Max(max, r.GetSample(i).Value);
                if (max >= 100000)
                    lines.Add($"{kv.Key}={max / 1000000.0:F2}ms");
            }

            var result = lines.Count > 0 ? string.Join(", ", lines) : $"no marker >=0.1ms (recorders={recorders.Count} valid={valid} withSamples={withSamples})";
            Log($"CASE4 markers >=0.1ms in the {AfterFrames} frames after '{name}': {result}");
        }

        static List<KeyValuePair<string, ProfilerRecorder>> StartRecorders()
        {
            var result = new List<KeyValuePair<string, ProfilerRecorder>>();
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var re = new Regex("Layout|Font|Text|Glyph|UIR|UIElements|Atlas", RegexOptions.IgnoreCase);
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || !d.Category.Name.StartsWith("UI") || !re.IsMatch(d.Name))
                    continue;
                var options = ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame;
                result.Add(new KeyValuePair<string, ProfilerRecorder>($"{d.Category.Name}/{d.Name}", new ProfilerRecorder(h, 60, options)));
            }
            Log($"CASE4 profiler markers matched={result.Count} available={handles.Count}{(result.Count == 0 ? " (no recorder: non-development Player, or the markers are not registered yet)" : "")}");
            return result;
        }

        // ---- screenshot helpers ----
        // 撮影に失敗したら例外にして、Guard が Fail にする
        static Texture2D Capture() => ScreenCapture.CaptureScreenshotAsTexture() ?? throw new InvalidOperationException("ScreenCapture.CaptureScreenshotAsTexture returned null");

        readonly struct DiffResult
        {
            public readonly int Count;
            public readonly int MinX, MinY, MaxX, MaxY;

            public string BoxText => Count > 0 ? $"({MinX},{MinY})-({MaxX},{MaxY})" : "none";

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
            if (a.width != b.width || a.height != b.height)
                throw new InvalidOperationException($"screenshot size mismatch: {a.width}x{a.height} vs {b.width}x{b.height}");
            var pa = a.GetPixels32();
            var pb = b.GetPixels32();
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
                var path = Path.GetFullPath(Path.Combine(dir, file));
                var bytes = tex.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
                Log($"saved {path} ({bytes.Length} bytes)");
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
