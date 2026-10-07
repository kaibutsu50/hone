using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // モデルルームの前提の検証（#56）。RoomExp.unity の 2 枚の PanelRenderer（Screen Space の RoomExp.uxml、World Space の RoomExpWorld.uxml。theme はどちらも RoomExp.tss）で測り、
    // 1 項目 1 行の "[RoomExpProbe] RESULT item=<項目> ok=<仮説どおりか> …" をログに出す。最後に "DONE" を出す。
    //   a        :focus の background-image でカーソル。フォーカス前後の resolvedStyle.backgroundImage と layout.width
    //   d-font   .room-exp で範囲を絞った --hone-font-body。中と外の .hone-text の unityFontDefinition と、.room-exp に書いた -unity-text-generator の継承
    //   d-rule   .room-exp .hone-focusable.hone-focusable:focus { border-width: 0 } が Core.uss の ring に勝つか
    //   b-*      ループするアニメーション（uss / schedule / experimental）× panel（screen / world）。動くか、panel から外すと止まるか、戻すと再開するか
    //   e-*      文字送り。言語ごとに、表示した文字数 0 / 半分 / 全部の Label の高さと各行の先頭の文字が同じか、未表示の文字の頂点の alpha が 0 か
    //   e-noparse, e-noparse-close   rich text のタグを文字のまま出す書き方（<noparse> と、本文の </noparse> のエスケープ）。parsedText が本文と同じか
    //   e-textelement   StringInfo の text element 単位の数え方
    // ok は「Issue の仮説どおりだったか」で、false でも Probe の失敗ではない（結果として README に書く）。
    // 測定の前提が崩れたとき（panel が準備できない、要素が無い、Probe 以外の Error のログ）は "[RoomExpProbe] FAIL …" を出す。
    // (c) ピクセルフォントのにじみは判定しない。HEADER 行に FontAsset の設定を出すだけで、スクリーンショットを人が見る。
    // Editor の Play Mode で回す（Issue の検証手順）。Domain Reload が無効なので、状態はインスタンスのフィールドだけに持つ。
    public class RoomExpProbe : MonoBehaviour
    {
        const string Prefix = "[RoomExpProbe]";
        const int MaxWaitFrames = 300;
        const int SettleFrames = 10;
        const float SampleSeconds = 1.5f;
        const float DetachSeconds = 1f;
        const int ScheduleIntervalMs = 250;
        const int ExperimentalDurationMs = 250;
        const float MovedThreshold = 0.5f;
        static readonly string[] TypewriterLanguages = { "ja", "en", "ar", "th" };

        [SerializeField] PanelRenderer m_Screen;
        [SerializeField] PanelRenderer m_World;
        // Gallery/TestStrings.json
        [SerializeField] TextAsset m_TestStrings;

        VisualElement m_ScreenRoot;
        VisualElement m_WorldRoot;
        int m_Results;
        int m_Mismatches;
        int m_Failures;

        void Awake()
        {
            Application.logMessageReceived += OnLog;
            if (m_Screen == null || m_World == null || m_TestStrings == null)
            {
                Fail($"field is not assigned (screen={m_Screen != null} world={m_World != null} testStrings={m_TestStrings != null})");
                return;
            }
            m_Screen.RegisterUIReloadCallback((pr, root, version) => m_ScreenRoot = root);
            m_World.RegisterUIReloadCallback((pr, root, version) => m_WorldRoot = root);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        IEnumerator Start()
        {
            Debug.Log($"{Prefix} unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor}");

            var frames = 0;
            while (frames < MaxWaitFrames && !Ready())
            {
                frames++;
                yield return null;
            }
            if (!Ready())
            {
                Fail($"panels did not become ready within {frames} frames (screen={m_ScreenRoot != null} world={m_WorldRoot != null})");
                Done();
                yield break;
            }
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            LogHeader();
            MeasureFont();
            yield return MeasureFocus();
            yield return MeasureLoops();
            yield return MeasureTypewriter();
            yield return MeasureNoParse();
            MeasureTextElements();
            Done();
        }

        bool Ready()
        {
            if (m_ScreenRoot == null || m_WorldRoot == null)
                return false;
            // style の解決前は fontAsset が null で、変数が解決できなかった場合と区別が付かない。外側（:root の RobotoMono）が解決するまで待つ
            var outside = m_ScreenRoot.Q<Label>("font-outside");
            return outside != null && outside.resolvedStyle.unityFontDefinition.fontAsset != null;
        }

        void LogHeader()
        {
            foreach (var (panel, root) in Panels())
            {
                var settings = panel == "screen" ? m_Screen.panelSettings : m_World.panelSettings;
                Debug.Log($"{Prefix} HEADER panel={panel} renderMode={settings.renderMode} scaleMode={settings.scaleMode} scale={settings.scale} root={root.layout.width}x{root.layout.height}");
                foreach (var label in root.Query<Label>(className: "room-exp-font").ToList())
                {
                    var fa = label.resolvedStyle.unityFontDefinition.fontAsset;
                    var detail = fa == null ? "null" : $"{fa.name} mode={fa.atlasRenderMode} samplingSize={fa.faceInfo.pointSize} atlasFilter={fa.atlasTexture?.filterMode}";
                    Debug.Log($"{Prefix} HEADER panel={panel} font fontSize={label.resolvedStyle.fontSize} fontAsset={detail}");
                }
            }
            var camera = Camera.main;
            if (camera != null)
            {
                // World Space の panel の 1px（1 / pixels per unit の world 単位）が、画面の何 px に写るか（panel がカメラの正面にある前提の近似）
                var distance = Vector3.Dot(m_World.transform.position - camera.transform.position, camera.transform.forward);
                var worldPerScreenPixel = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / camera.pixelHeight;
                // pixels per unit は PanelSettings の公開 API に無い（6000.7.0b2）。RoomExpWorldSpacePanelSettings.asset の m_PixelsPerUnit の値
                const float ppu = 100f;
                Debug.Log($"{Prefix} HEADER world panel position={m_World.transform.position} size={m_World.worldSpaceSize} distance={distance:F3} pixelsPerUnit={ppu} screenPixelsPerPanelPixel={1f / ppu / worldPerScreenPixel:F3} cameraPixelHeight={camera.pixelHeight}");
            }
        }

        IEnumerable<(string, VisualElement)> Panels()
        {
            yield return ("screen", m_ScreenRoot);
            yield return ("world", m_WorldRoot);
        }

        // ---- (d) 範囲を絞ったフォント ----
        void MeasureFont()
        {
            var inside = m_ScreenRoot.Q<Label>("font-inside");
            var outside = m_ScreenRoot.Q<Label>("font-outside");
            if (inside == null || outside == null)
            {
                Fail("label 'font-inside' or 'font-outside' not found");
                return;
            }
            var insideName = FontName(inside);
            var outsideName = FontName(outside);
            // -unity-text-generator は .room-exp にだけ書いている。中の Label が Standard なら継承している
            var insideGenerator = inside.resolvedStyle.unityTextGenerator;
            var outsideGenerator = outside.resolvedStyle.unityTextGenerator;
            Result("d-font", insideName == "DotGothic16" && outsideName == "RobotoMono" && insideGenerator == TextGeneratorType.Standard,
                $"inside={insideName} outside={outsideName} insideGenerator={insideGenerator} outsideGenerator={outsideGenerator}");
        }

        static string FontName(VisualElement element)
        {
            var fa = element.resolvedStyle.unityFontDefinition.fontAsset;
            return fa == null ? "null" : fa.name;
        }

        // ---- (a) :focus の background-image でカーソル、(d) 範囲を絞った規則 ----
        IEnumerator MeasureFocus()
        {
            var item = m_ScreenRoot.Q<VisualElement>("item-1");
            if (item == null)
            {
                Fail("element 'item-1' not found");
                yield break;
            }
            var beforeImage = ImageName(item);
            var beforeWidth = item.layout.width;
            var beforeBorder = item.resolvedStyle.borderTopWidth;
            item.Focus();
            // フォーカス変更は非同期。値を読む前に少し待つ
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            if (item.focusController?.focusedElement != item)
            {
                Fail("premise broken: item-1 did not get focus");
                yield break;
            }
            var afterImage = ImageName(item);
            var afterWidth = item.layout.width;
            var afterBorder = item.resolvedStyle.borderTopWidth;
            var classes = string.Join(" ", item.GetClasses());
            Result("a", beforeImage == "none" && afterImage == "RoomExpCursor" && Mathf.Approximately(beforeWidth, afterWidth),
                $"imageBefore={beforeImage} imageAfter={afterImage} widthBefore={beforeWidth} widthAfter={afterWidth} height={item.layout.height} classes=\"{classes}\"");

            // 対照: .room-exp の外の Button（border-width: 0 だけ同じ）に、Core.uss の ring が効くこと
            var control = m_ScreenRoot.Q<VisualElement>("ring-control");
            if (control == null)
            {
                Fail("element 'ring-control' not found");
                yield break;
            }
            var controlBefore = control.resolvedStyle.borderTopWidth;
            control.Focus();
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            if (control.focusController?.focusedElement != control)
            {
                Fail("premise broken: ring-control did not get focus");
                yield break;
            }
            var controlFocused = control.resolvedStyle.borderTopWidth;
            var itemAfterBlur = ImageName(item);
            Result("d-rule", Mathf.Approximately(afterBorder, 0f) && Mathf.Approximately(controlFocused, 2f),
                $"scopedBefore={beforeBorder} scopedFocused={afterBorder} controlBefore={controlBefore} controlFocused={controlFocused} controlImage={ImageName(control)} item1ImageAfterBlur={itemAfterBlur}");
            // スクリーンショットにカーソルを写すため、フォーカスを item-1 に戻しておく
            item.Focus();
        }

        static string ImageName(VisualElement element)
        {
            var background = element.resolvedStyle.backgroundImage;
            if (background.texture != null)
                return background.texture.name;
            if (background.sprite != null)
                return background.sprite.name;
            return "none";
        }

        // ---- (b) ループするアニメーション ----
        class Loop
        {
            public string Panel;
            public string Kind;
            public VisualElement Element;
            public int Ticks;
            public float Min = float.MaxValue;
            public float Max = float.MinValue;
            public IVisualElementScheduledItem Scheduled;

            public void Sample()
            {
                var opacity = Element.resolvedStyle.opacity;
                Min = Mathf.Min(Min, opacity);
                Max = Mathf.Max(Max, opacity);
            }
        }

        IEnumerator MeasureLoops()
        {
            var loops = new List<Loop>();
            foreach (var (panel, root) in Panels())
            {
                foreach (var kind in new[] { "uss", "schedule", "experimental" })
                {
                    var element = root.Q<VisualElement>("blink-" + kind);
                    if (element == null)
                    {
                        Fail($"element '{panel}.blink-{kind}' not found");
                        continue;
                    }
                    var loop = new Loop { Panel = panel, Kind = kind, Element = element };
                    if (kind == "schedule")
                        loop.Scheduled = element.schedule.Execute(() =>
                        {
                            loop.Ticks++;
                            element.ToggleInClassList("is-dim");
                        }).Every(ScheduleIntervalMs);
                    else if (kind == "experimental")
                        StartPingPong(loop, true);
                    loops.Add(loop);
                }
            }

            yield return SampleLoops(loops);
            var moved = loops.ToDictionary(l => l, l => $"{l.Max - l.Min > MovedThreshold}(min={l.Min:F2} max={l.Max:F2})");
            var movedOk = loops.ToDictionary(l => l, l => l.Max - l.Min > MovedThreshold);

            // panel から外して止まるか、戻して再開するか。uss は tick を数える口が無いので、外している間は見ず、戻した後に動いているかだけを見る
            var ticksAtDetach = loops.ToDictionary(l => l, l => l.Ticks);
            var parents = loops.ToDictionary(l => l, l => (l.Element.parent, l.Element.parent.IndexOf(l.Element)));
            foreach (var loop in loops)
                loop.Element.RemoveFromHierarchy();
            var deadline = Time.realtimeSinceStartup + DetachSeconds;
            while (Time.realtimeSinceStartup < deadline)
                yield return null;
            var ticksDetached = loops.ToDictionary(l => l, l => l.Ticks - ticksAtDetach[l]);
            foreach (var loop in loops)
            {
                var (parent, index) = parents[loop];
                parent.Insert(index, loop.Element);
                loop.Min = float.MaxValue;
                loop.Max = float.MinValue;
            }
            yield return SampleLoops(loops);

            foreach (var loop in loops)
            {
                var resumed = loop.Max - loop.Min > MovedThreshold;
                var values = $"moved={moved[loop]} movedAfterReattach={resumed}(min={loop.Min:F2} max={loop.Max:F2})";
                if (loop.Kind == "uss")
                {
                    Result($"b-{loop.Kind}-{loop.Panel}", movedOk[loop] && resumed, values);
                    continue;
                }
                values += $" ticksBeforeDetach={ticksAtDetach[loop]} ticksWhileDetached={ticksDetached[loop]}";
                if (loop.Scheduled != null)
                    values += $" scheduledIsActive={loop.Scheduled.isActive}";
                Result($"b-{loop.Kind}-{loop.Panel}", movedOk[loop] && ticksDetached[loop] == 0 && resumed, values);
            }
        }

        static IEnumerator SampleLoops(List<Loop> loops)
        {
            var deadline = Time.realtimeSinceStartup + SampleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var loop in loops)
                    loop.Sample();
                yield return null;
            }
        }

        void StartPingPong(Loop loop, bool down)
        {
            var from = down ? 1f : 0f;
            var to = down ? 0f : 1f;
            loop.Element.experimental.animation
                .Start(from, to, ExperimentalDurationMs, (e, v) =>
                {
                    loop.Ticks++;
                    e.style.opacity = v;
                })
                .OnCompleted(() => StartPingPong(loop, !down));
        }

        // ---- (e) 文字送り ----
        // PostProcessTextVertices で受け取った glyph ごとの行、parsedText 上の位置、頂点の alpha
        class Capture
        {
            public readonly List<(int line, int start, byte alpha)> Glyphs = new List<(int, int, byte)>();
        }

        IEnumerator MeasureTypewriter()
        {
            var container = m_ScreenRoot.Q<VisualElement>("typewriter");
            if (container == null)
            {
                Fail("element 'typewriter' not found");
                yield break;
            }
            var strings = ParseLongStrings(m_TestStrings.text);
            foreach (var language in TypewriterLanguages)
            {
                if (!strings.TryGetValue(language, out var text))
                {
                    Fail($"TestStrings.json has no '{language}'");
                    continue;
                }
                var total = new StringInfo(text).LengthInTextElements;
                var counts = new List<int> { 0, total / 2, total };
                // アラビア語は、左につながる文字どうしの間で切った場合も見る（見えている側の字形が、未表示の文字とつながったままかをスクリーンショットで見る）
                if (language == "ar")
                    counts.Add(JoiningBoundary(text, total / 2));
                var labels = new List<(Label, Capture, int)>();
                // スクリーンショットで読めるよう、1 言語を 1 行にする
                var row = new VisualElement();
                row.AddToClassList("room-exp-row");
                container.Add(row);
                foreach (var count in counts)
                {
                    var capture = new Capture();
                    var label = new Label(Typewriter(text, count)) { enableRichText = true };
                    label.AddToClassList("room-exp-typewriter");
                    label.PostProcessTextVertices = glyphs =>
                    {
                        capture.Glyphs.Clear();
                        foreach (var glyph in glyphs)
                        {
                            var alpha = glyph.vertices.Length > 0 ? glyph.vertices[0].tint.a : (byte)0;
                            capture.Glyphs.Add((glyph.line, glyph.textRange.start, alpha));
                        }
                    };
                    row.Add(label);
                    labels.Add((label, capture, count));
                }
                for (var i = 0; i < SettleFrames; i++)
                    yield return null;

                var heights = new List<float>();
                var firsts = new List<string>();
                var alphaMismatches = 0;
                var parsedMatches = true;
                var detail = new StringBuilder();
                foreach (var (label, capture, count) in labels)
                {
                    // textRange は parsedText（タグを除いた文字列）の位置を指す（6000.7.0b2。text の位置ではない）
                    var parsed = label.parsedText;
                    parsedMatches &= parsed == text;
                    var lines = capture.Glyphs.GroupBy(g => g.line).OrderBy(g => g.Key).ToList();
                    var lineStarts = string.Join("|", lines.Select(g => CharAt(parsed, g.Min(x => x.start))));
                    // 表示済みの部分（parsedText の先頭から boundary まで）の glyph だけが alpha > 0。結合文字は別の glyph になるので、glyph の数ではなく位置で見る
                    var boundary = HeadLength(text, count);
                    var visible = capture.Glyphs.Count(g => g.alpha > 0);
                    alphaMismatches += capture.Glyphs.Count(g => (g.start < boundary) != (g.alpha > 0));
                    heights.Add(label.layout.height);
                    firsts.Add(lineStarts);
                    detail.Append($" [n={count}/{total} height={label.layout.height} lines={lines.Count} lineStarts={lineStarts} visibleGlyphs={visible} hiddenGlyphs={capture.Glyphs.Count - visible}]");
                }
                var sameHeight = heights.All(h => Mathf.Approximately(h, heights[0]));
                var sameFirsts = firsts.All(f => f == firsts[0]);
                Result($"e-{language}", sameHeight && sameFirsts && alphaMismatches == 0 && parsedMatches,
                    $"sameHeight={sameHeight} sameLineStarts={sameFirsts} alphaMismatches={alphaMismatches} parsedTextEqualsSource={parsedMatches}{detail}");
            }
        }

        // 表示済みの部分はそのまま、未表示の部分を <alpha=#00> で透明にする。どちらも <noparse> で囲み、本文の < をタグとして解釈させない
        static string Typewriter(string text, int visibleTextElements)
        {
            var head = text.Substring(0, HeadLength(text, visibleTextElements));
            var tail = text.Substring(head.Length);
            return NoParse(head) + "<alpha=#00>" + NoParse(tail);
        }

        // from 以下で、直前の文字が左につながるアラビア文字で、直後もアラビア文字になる境界（text element の数）。text は結合文字を含まない前提
        static int JoiningBoundary(string text, int from)
        {
            const string rightJoiningOnly = "اأإآدذرزوؤة";
            bool IsArabicLetter(char c) => c >= 'ؠ' && c <= 'ي';
            for (var k = Math.Min(from, text.Length - 1); k > 0; k--)
                if (IsArabicLetter(text[k - 1]) && rightJoiningOnly.IndexOf(text[k - 1]) < 0 && IsArabicLetter(text[k]))
                    return k;
            return from;
        }

        // 先頭から visibleTextElements 個の text element の UTF-16 の長さ
        static int HeadLength(string text, int visibleTextElements)
        {
            var info = new StringInfo(text);
            return visibleTextElements == 0 ? 0 : info.SubstringByTextElements(0, Math.Min(visibleTextElements, info.LengthInTextElements)).Length;
        }

        // 本文の </noparse> だけは noparse の中でも閉じタグになる。"<" を noparse の中に残し、"/noparse>" を外に出して、文字のまま出す
        static string NoParse(string text) =>
            text.Length == 0 ? "" : "<noparse>" + text.Replace("</noparse>", "<</noparse>/noparse><noparse>") + "</noparse>";

        static string NaiveNoParse(string text) => "<noparse>" + text + "</noparse>";

        static string CharAt(string text, int index) => index >= 0 && index < text.Length ? text[index].ToString() : "?";

        // TestStrings.json の各キーの "long"。値に " を含まない前提（JsonUtility は辞書を読めない）
        static Dictionary<string, string> ParseLongStrings(string json)
        {
            var result = new Dictionary<string, string>();
            foreach (Match match in Regex.Matches(json, "\"(?<key>[a-z-]+)\"\\s*:\\s*\\{[^}]*\"long\"\\s*:\\s*\"(?<long>[^\"]*)\""))
                result[match.Groups["key"].Value] = match.Groups["long"].Value;
            return result;
        }

        IEnumerator MeasureNoParse()
        {
            var container = m_ScreenRoot.Q<VisualElement>("typewriter");
            // 本文に < とタグの形を含む。タグとして解釈されなければ、parsedText が本文と同じになる
            var cases = new[]
            {
                ("e-noparse", "HP<10 <b>bold</b>"),
                // 本文に </noparse> そのものが含まれる。素朴に囲む形（naive）は、そこで noparse が閉じる
                ("e-noparse-close", "a</noparse><b>b</b>"),
            };
            var labels = new List<(string, string, Label, Label)>();
            var row = new VisualElement();
            row.AddToClassList("room-exp-row");
            container.Add(row);
            foreach (var (item, body) in cases)
            {
                var escaped = new Label(NoParse(body)) { enableRichText = true };
                var naive = new Label(NaiveNoParse(body)) { enableRichText = true };
                escaped.AddToClassList("room-exp-typewriter");
                naive.AddToClassList("room-exp-typewriter");
                row.Add(escaped);
                row.Add(naive);
                labels.Add((item, body, escaped, naive));
            }
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            foreach (var (item, body, escaped, naive) in labels)
                Result(item, escaped.parsedText == body, $"body=\"{body}\" parsedText=\"{escaped.parsedText}\" naiveParsedText=\"{naive.parsedText}\"");
        }

        // ---- 1 文字の数え方 ----
        void MeasureTextElements()
        {
            // 見た目で 1 文字になるもの。期待はすべて 1
            var cases = new[]
            {
                ("ja-dakuten-decomposed", "が"),
                ("latin-combining-acute", "é"),
                ("emoji-surrogate-pair", "\U0001F600"),
                ("emoji-zwj-family", "\U0001F468‍\U0001F469‍\U0001F467"),
                ("flag-regional-indicators", "\U0001F1EF\U0001F1F5"),
                ("thai-tone-marks", "ที่"),
                ("thai-sara-am", "ทำ"),
                ("arabic-fatha", "بَ"),
            };
            var detail = new StringBuilder();
            var all = true;
            foreach (var (name, text) in cases)
            {
                var count = new StringInfo(text).LengthInTextElements;
                all &= count == 1;
                detail.Append($" {name}={count}(utf16={text.Length})");
            }
            Result("e-textelement", all, $"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}{detail}");
        }

        // ---- 出力 ----
        void Result(string item, bool ok, string values)
        {
            m_Results++;
            if (!ok)
                m_Mismatches++;
            Debug.Log($"{Prefix} RESULT item={item} ok={ok.ToString().ToLowerInvariant()} {values}");
        }

        void Fail(string message)
        {
            m_Failures++;
            Debug.LogError($"{Prefix} FAIL {message}");
        }

        void Done()
        {
            Debug.Log($"{Prefix} DONE results={m_Results} mismatches={m_Mismatches} failures={m_Failures}");
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition.StartsWith(Prefix))
                return;
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Fail($"{type}: {condition}");
        }
    }
}
