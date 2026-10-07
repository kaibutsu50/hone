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
    // 1 項目 1 行の "[RoomExpProbe] RESULT item=<項目> ok=<仮説どおりか> …" をログに出す。最後に必ず "DONE" を出す。
    //   a        :focus の background-image でカーソル。フォーカス前後の resolvedStyle.backgroundImage と layout.width
    //   d-font   .room-exp で範囲を絞った --hone-font-body。中と外の .hone-text の unityFontDefinition と、.room-exp に書いた -unity-text-generator の継承
    //   d-rule   .room-exp .hone-focusable.hone-focusable:focus { border-width: 0 } が Core.uss の ring に勝つか
    //   b-*      ループするアニメーション（uss / schedule / experimental）× panel（screen / world）。動くか、panel から戻すと再開するか。
    //            schedule と experimental は、panel から外している間に tick が止まるかも見る（uss は tick を数える口が無いので見ない）
    //   e-*      文字送り。言語ごとに、表示した文字数 0 / 半分 / 全部（ar はつながる文字の境界も）の Label の高さと各行の先頭の文字が同じか、
    //            未表示の文字の glyph だけが頂点の alpha 0 か、parsedText が本文と同じか。
    //            e-<言語> は .room-exp の外（Advanced Text Generator、:root の RobotoMono と PanelTextSettings の fallback）、
    //            e-room-<言語> は .room-exp の中（Standard の生成器、DotGothic16）。Standard では Glyph.textRange と parsedText が使えないので、
    //            行頭の文字の代わりに行ごとの glyph の数を、位置による alpha の判定の代わりに glyph の並び順での alpha を見て、parsedText は比べない
    //   e-noparse, e-noparse-close, e-noparse-close-upper   rich text のタグを文字のまま出す書き方（<noparse> と、本文の </noparse> のエスケープ）。parsedText が本文と同じか
    //   e-textelement   StringInfo の text element 単位の数え方
    // ok は「Issue の仮説どおりだったか」で、false でも Probe の失敗ではない（結果として README に書く）。
    // 測定の前提が崩れたとき（panel が準備できない、要素が無い、glyph が取れない、対照が期待どおりでない、Probe 以外の Error のログ、時間切れ）は
    // "[RoomExpProbe] FAIL …" を出して failures に数え、その項目の RESULT は出さない。DONE の時点で RESULT が出ていない項目も FAIL にする。
    // (c) ピクセルフォントのにじみは判定しない。HEADER 行に FontAsset の設定を出すだけで、スクリーンショットを人が見る。
    // Editor の Play Mode で回す（Issue の検証手順）。Domain Reload が無効なので、状態はインスタンスのフィールドだけに持つ。
    public class RoomExpProbe : MonoBehaviour
    {
        const string Prefix = "[RoomExpProbe]";
        const int MaxWaitFrames = 300;
        const int SettleFrames = 10;
        const float MaxRunSeconds = 60f;
        const float SampleSeconds = 1.5f;
        const float DetachSeconds = 1f;
        const int ScheduleIntervalMs = 250;
        const int ExperimentalDurationMs = 250;
        const float MovedThreshold = 0.5f;
        const int MaxMessages = 20;
        // Tokens.uss の --hone-ring-width。トークンの値を変えたらここも変える
        const float RingWidth = 2f;
        // pixels per unit は PanelSettings の公開 API に無い（6000.7.0b2）。RoomExpWorldSpacePanelSettings.asset の m_PixelsPerUnit の値
        const float PixelsPerUnit = 100f;
        static readonly string[] TypewriterLanguages = { "ja", "en", "ar", "th" };
        // .room-exp の中（DotGothic16）で測る言語。DotGothic16 は ar と th の字形を持たない
        static readonly string[] RoomTypewriterLanguages = { "ja", "en" };
        static readonly string[] ExpectedItems =
        {
            "d-font", "a", "d-rule",
            "b-uss-screen", "b-schedule-screen", "b-experimental-screen", "b-uss-world", "b-schedule-world", "b-experimental-world",
            "e-ja", "e-en", "e-ar", "e-th", "e-room-ja", "e-room-en",
            "e-noparse", "e-noparse-close", "e-noparse-close-upper", "e-textelement",
        };

        [SerializeField] PanelRenderer m_Screen;
        [SerializeField] PanelRenderer m_World;
        // Gallery/TestStrings.json
        [SerializeField] TextAsset m_TestStrings;

        VisualElement m_ScreenRoot;
        VisualElement m_WorldRoot;
        float m_StartTime;
        bool m_SetupFailed;
        bool m_Finished;
        bool m_LoopsStopped;
        int m_LastSampleFrames;
        int m_Results;
        int m_Mismatches;
        int m_Failures;
        int m_Warnings;
        readonly List<string> m_Messages = new List<string>();
        readonly HashSet<string> m_Reported = new HashSet<string>();
        readonly List<Loop> m_Loops = new List<Loop>();

        void Awake()
        {
            m_StartTime = Time.realtimeSinceStartup;
            Application.logMessageReceived += OnLog;
            if (m_Screen == null || m_World == null || m_TestStrings == null)
            {
                m_SetupFailed = true;
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

        // コルーチンが例外で止まっても DONE を出す（Unity は例外で止まったコルーチンの finally を確実には走らせない）
        void Update()
        {
            if (m_Finished || Time.realtimeSinceStartup - m_StartTime <= MaxRunSeconds)
                return;
            StopAllCoroutines();
            Fail($"did not finish within {MaxRunSeconds} seconds");
            Done();
        }

        IEnumerator Start()
        {
            Debug.Log($"{Prefix} unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor}");
            if (m_SetupFailed)
            {
                Done();
                yield break;
            }

            var frames = 0;
            while (frames < MaxWaitFrames && !Ready())
            {
                frames++;
                yield return null;
            }
            if (!Ready())
            {
                Fail($"not ready within {frames} frames ({ReadyState()})");
                Done();
                yield break;
            }
            for (var i = 0; i < SettleFrames; i++)
                yield return null;

            LogHeader();
            MeasureFont();
            yield return MeasureFocus();
            yield return MeasureLoops();
            yield return MeasureTypewriter("e-", "typewriter", TypewriterLanguages, false);
            yield return MeasureTypewriter("e-room-", "typewriter-room", RoomTypewriterLanguages, true);
            yield return MeasureNoParse();
            MeasureTextElements();
            // ループを止めてから 1 frame 待ち、その間に出たログも DONE の集計に入れる
            StopLoops();
            yield return null;
            Done();
        }

        // 両方の panel の root が届き、World Space の layout が決まり、Screen Space の .hone-text のフォント（:root の RobotoMono）が解決していること。
        // style の解決前は fontAsset が null で、変数が解決できなかった場合と区別が付かない
        bool Ready()
        {
            if (m_ScreenRoot == null || m_WorldRoot == null || !(m_WorldRoot.layout.width > 0f))
                return false;
            var outside = m_ScreenRoot.Q<Label>("font-outside");
            return outside != null && outside.resolvedStyle.unityFontDefinition.fontAsset != null;
        }

        string ReadyState()
        {
            var outside = m_ScreenRoot?.Q<Label>("font-outside");
            return $"screenRoot={m_ScreenRoot != null} worldRoot={m_WorldRoot != null} worldLayoutWidth={m_WorldRoot?.layout.width} " +
                $"fontOutside={(outside == null ? "missing" : FontName(outside))}";
        }

        void LogHeader()
        {
            foreach (var (panel, root) in Panels())
            {
                var settings = panel == "screen" ? m_Screen.panelSettings : m_World.panelSettings;
                if (settings == null)
                {
                    Fail($"premise broken: {panel} PanelRenderer has no PanelSettings");
                    continue;
                }
                Debug.Log($"{Prefix} HEADER panel={panel} renderMode={settings.renderMode} scaleMode={settings.scaleMode} scale={settings.scale} root={root.layout.width}x{root.layout.height}");
                foreach (var label in root.Query<Label>(className: "room-exp-font").ToList())
                {
                    var fa = label.resolvedStyle.unityFontDefinition.fontAsset;
                    var atlas = fa == null ? null : fa.atlasTexture;
                    var detail = fa == null ? "null" : $"{fa.name} mode={fa.atlasRenderMode} samplingSize={fa.faceInfo.pointSize} atlasFilter={(atlas == null ? "null" : atlas.filterMode.ToString())}";
                    Debug.Log($"{Prefix} HEADER panel={panel} font fontSize={label.resolvedStyle.fontSize} generator={label.resolvedStyle.unityTextGenerator} fontAsset={detail}");
                }
            }
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.Log($"{Prefix} HEADER world camera=null (screenPixelsPerPanelPixel is not computed)");
                return;
            }
            // World Space の panel の 1px（1 / pixels per unit の world 単位）が、画面の何 px に写るか。panel の面がカメラの向きに垂直（回転なし）である前提
            var distance = Vector3.Dot(m_World.transform.position - camera.transform.position, camera.transform.forward);
            var worldPerScreenPixel = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / camera.pixelHeight;
            Debug.Log($"{Prefix} HEADER world panel position={m_World.transform.position} rotation={m_World.transform.rotation.eulerAngles} size={m_World.worldSpaceSize} distance={distance:F3} " +
                $"pixelsPerUnit={PixelsPerUnit} screenPixelsPerPanelPixel={1f / PixelsPerUnit / worldPerScreenPixel:F3} cameraPixelHeight={camera.pixelHeight}");
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
            // font-inside の祖先で -unity-text-generator を書いているのは .room-exp だけ。中の Label が Standard なら継承している
            var insideGenerator = inside.resolvedStyle.unityTextGenerator;
            var outsideGenerator = outside.resolvedStyle.unityTextGenerator;
            Result("d-font", insideName == "DotGothic16" && outsideName == "RobotoMono" && insideGenerator == TextGeneratorType.Standard && outsideGenerator == TextGeneratorType.Advanced,
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
            var control = m_ScreenRoot.Q<VisualElement>("ring-control");
            if (item == null || control == null)
            {
                Fail("element 'item-1' or 'ring-control' not found");
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
                $"imageBefore={beforeImage} imageAfter={afterImage} widthBefore={beforeWidth} widthAfter={afterWidth} height={item.layout.height} borderTopWidthFocused={afterBorder} classes=\"{classes}\"");

            // 対照: .room-exp の外で、border-width: 0 を同じく置いた Button。フォーカスで Core.uss の ring になることが、d-rule を読む前提
            var controlBefore = control.resolvedStyle.borderTopWidth;
            control.Focus();
            for (var i = 0; i < SettleFrames; i++)
                yield return null;
            var controlFocused = control.resolvedStyle.borderTopWidth;
            if (control.focusController?.focusedElement != control)
                Fail("premise broken: ring-control did not get focus");
            else if (!Mathf.Approximately(controlFocused, RingWidth))
                Fail($"premise broken: ring-control borderTopWidth focused={controlFocused} (expected the Core.uss ring {RingWidth})");
            else
                Result("d-rule", Mathf.Approximately(afterBorder, 0f),
                    $"scopedBefore={beforeBorder} scopedFocused={afterBorder} controlBefore={controlBefore} controlFocused={controlFocused} controlImage={ImageName(control)} item1ImageAfterBlur={ImageName(item)}");
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
                    m_Loops.Add(loop);
                }
            }
            var loops = m_Loops;

            var ticksAtStart = loops.ToDictionary(l => l, l => l.Ticks);
            yield return SampleLoops(loops);
            var framesSampled = m_LastSampleFrames;
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
            // 同じ親の要素をインデックスの昇順で戻すので、元の並びになる
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
                values += $" ticksWhileSampling={ticksAtDetach[loop] - ticksAtStart[loop]} framesSampled={framesSampled} ticksWhileDetached={ticksDetached[loop]}";
                if (loop.Scheduled != null)
                    values += $" scheduledIsActive={loop.Scheduled.isActive}";
                Result($"b-{loop.Kind}-{loop.Panel}", movedOk[loop] && ticksDetached[loop] == 0 && resumed, values);
            }
        }

        IEnumerator SampleLoops(List<Loop> loops)
        {
            var frames = 0;
            var deadline = Time.realtimeSinceStartup + SampleSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                foreach (var loop in loops)
                    loop.Sample();
                frames++;
                yield return null;
            }
            m_LastSampleFrames = frames;
        }

        void StartPingPong(Loop loop, bool down)
        {
            if (m_LoopsStopped)
                return;
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

        void StopLoops()
        {
            m_LoopsStopped = true;
            foreach (var loop in m_Loops)
                loop.Scheduled?.Pause();
        }

        // ---- (e) 文字送り ----
        // PostProcessTextVertices で受け取った glyph ごとの行、parsedText 上の位置、先頭の頂点の alpha（頂点が無い glyph は -1）
        class Capture
        {
            public readonly List<(int line, int start, int alpha)> Glyphs = new List<(int, int, int)>();
        }

        IEnumerator MeasureTypewriter(string itemPrefix, string containerName, string[] languages, bool roomFont)
        {
            var container = m_ScreenRoot.Q<VisualElement>(containerName);
            if (container == null)
            {
                Fail($"element '{containerName}' not found");
                yield break;
            }
            var strings = ParseLongStrings(m_TestStrings.text);
            foreach (var language in languages)
            {
                var item = itemPrefix + language;
                if (!strings.TryGetValue(language, out var text))
                {
                    Fail($"TestStrings.json has no '{language}'");
                    continue;
                }
                var total = new StringInfo(text).LengthInTextElements;
                var counts = new List<int> { 0, total / 2, total };
                // アラビア語は、左につながる文字どうしの間で切った場合も見る（見えている側の字形が、未表示の文字とつながったままかをスクリーンショットで見る）
                if (language == "ar")
                {
                    var joining = JoiningBoundary(text, total / 2);
                    if (joining < 0)
                        Fail($"premise broken: {item} has no boundary between joining Arabic letters");
                    else
                        counts.Add(joining);
                }
                var labels = new List<(Label label, Capture capture, int count)>();
                // スクリーンショットで読めるよう、1 言語を 1 行にする
                var row = new VisualElement();
                row.AddToClassList("room-exp-row");
                container.Add(row);
                foreach (var count in counts)
                {
                    var capture = new Capture();
                    var label = new Label(Typewriter(text, count)) { enableRichText = true };
                    label.AddToClassList("room-exp-typewriter");
                    // .room-exp の中では .hone-text で --hone-font-body（DotGothic16）に従わせる
                    if (roomFont)
                        label.AddToClassList("hone-text");
                    // Glyph.textRange は Standard の生成器では NotImplementedException になる（6000.7.0b2）。.room-exp の中では読まず、位置を -1 にする
                    label.PostProcessTextVertices = glyphs =>
                    {
                        capture.Glyphs.Clear();
                        foreach (var glyph in glyphs)
                            capture.Glyphs.Add((glyph.line, roomFont ? -1 : glyph.textRange.start, glyph.vertices.Length > 0 ? glyph.vertices[0].tint.a : -1));
                    };
                    row.Add(label);
                    labels.Add((label, capture, count));
                }
                for (var i = 0; i < SettleFrames; i++)
                    yield return null;

                // 測定の前提: どの Label でも glyph が取れていて、layout が決まっていて、glyph の数が同じ（本文は同じで、透明にしているだけなので）
                var glyphCounts = labels.Select(l => l.capture.Glyphs.Count).ToList();
                if (glyphCounts.Any(c => c == 0) || glyphCounts.Distinct().Count() != 1 || labels.Any(l => !(l.label.layout.height > 0f)))
                {
                    Fail($"premise broken: {item} glyphs=[{string.Join(",", glyphCounts)}] heights=[{string.Join(",", labels.Select(l => l.label.layout.height))}]");
                    continue;
                }

                var heights = new List<float>();
                var firsts = new List<string>();
                var alphaMismatches = 0;
                var noVertexGlyphs = 0;
                var parsedMatches = true;
                var detail = new StringBuilder();
                foreach (var (label, capture, count) in labels)
                {
                    // parsedText も Standard の生成器では NotImplementedException になる（6000.7.0b2）
                    var parsed = roomFont ? null : label.parsedText;
                    if (!roomFont)
                        parsedMatches &= parsed == text;
                    var lines = capture.Glyphs.GroupBy(g => g.line).OrderBy(g => g.Key).ToList();
                    var withVertices = capture.Glyphs.Where(g => g.alpha >= 0).ToList();
                    noVertexGlyphs += capture.Glyphs.Count - withVertices.Count;
                    var visible = withVertices.Count(g => g.alpha > 0);
                    string lineStarts;
                    if (roomFont)
                    {
                        // textRange が無いので、行の折り返しは行ごとの glyph の数で比べる。
                        // alpha は glyph の並び順で見る: 表示済みの glyph が先頭に連続し、その後ろはすべて alpha 0。0 文字なら全部 0、全部なら全部 > 0
                        lineStarts = "glyphsPerLine:" + string.Join("|", lines.Select(g => g.Count()));
                        var firstHidden = withVertices.FindIndex(g => g.alpha == 0);
                        var prefixBroken = firstHidden >= 0 && withVertices.Skip(firstHidden).Any(g => g.alpha > 0);
                        var endsWrong = (count == 0 && visible != 0) || (count == total && visible != withVertices.Count);
                        if (prefixBroken || endsWrong)
                            alphaMismatches++;
                    }
                    else
                    {
                        // textRange は parsedText（タグを除いた文字列）の位置を指す（6000.7.0b2。text の位置ではない）
                        lineStarts = string.Join("|", lines.Select(g => CharAt(parsed, g.Min(x => x.start))));
                        // 表示済みの部分（先頭から boundary まで）の glyph だけが alpha > 0。結合文字は別の glyph になるので、glyph の数ではなく位置で見る。
                        // boundary は本文（text）上の位置で、glyph の位置は parsedText 上の位置。両者を比べてよいのは parsedText == text のとき（parsedMatches で別に確かめる）
                        var boundary = HeadLength(text, count);
                        alphaMismatches += withVertices.Count(g => (g.start < boundary) != (g.alpha > 0));
                    }
                    heights.Add(label.layout.height);
                    firsts.Add(lineStarts);
                    detail.Append($" [n={count}/{total} height={label.layout.height} lines={lines.Count} lineStarts={lineStarts} visibleGlyphs={visible} hiddenGlyphs={withVertices.Count - visible}]");
                }
                var first = labels[0].label;
                var sameHeight = heights.All(h => Mathf.Approximately(h, heights[0]));
                var sameFirsts = firsts.All(f => f == firsts[0]);
                Result(item, sameHeight && sameFirsts && alphaMismatches == 0 && parsedMatches,
                    $"font={FontName(first)} generator={first.resolvedStyle.unityTextGenerator} sameHeight={sameHeight} sameLineStarts={sameFirsts} alphaMismatches={alphaMismatches} " +
                    $"noVertexGlyphs={noVertexGlyphs} parsedTextEqualsSource={(roomFont ? "n/a" : parsedMatches.ToString())}{detail}");
            }
        }

        // 表示済みの部分はそのまま、未表示の部分を <alpha=#00> で透明にする。どちらも <noparse> で囲み、本文の < をタグとして解釈させない
        static string Typewriter(string text, int visibleTextElements)
        {
            var head = text.Substring(0, HeadLength(text, visibleTextElements));
            var tail = text.Substring(head.Length);
            return NoParse(head) + "<alpha=#00>" + NoParse(tail);
        }

        // アラビア文字のうち、左（次の文字）につながらないもの。ハムザ（U+0621）はどちらにもつながらない
        static readonly string NonLeftJoining = S(0x0621, 0x0622, 0x0623, 0x0624, 0x0625, 0x0627, 0x0629, 0x062F, 0x0630, 0x0631, 0x0632, 0x0648);

        static bool IsArabicLetter(char c) => c >= 0x0620 && c <= 0x064A;

        // from 以下で、直前の text element の文字が左につながるアラビア文字で、直後もアラビア文字になる境界（text element の数）。見つからなければ -1
        static int JoiningBoundary(string text, int from)
        {
            var starts = StringInfo.ParseCombiningCharacters(text);
            for (var k = Math.Min(from, starts.Length - 1); k > 0; k--)
            {
                var previous = text[starts[k - 1]];
                if (IsArabicLetter(previous) && NonLeftJoining.IndexOf(previous) < 0 && IsArabicLetter(text[starts[k]]))
                    return k;
            }
            return -1;
        }

        // 先頭から visibleTextElements 個の text element の UTF-16 の長さ
        static int HeadLength(string text, int visibleTextElements)
        {
            var info = new StringInfo(text);
            return visibleTextElements == 0 ? 0 : info.SubstringByTextElements(0, Math.Min(visibleTextElements, info.LengthInTextElements)).Length;
        }

        // 本文の </noparse> は noparse の中でも閉じタグになる（大文字小文字を問わないかは e-noparse-close-upper で見る）。
        // "<" を noparse の中に残し、"/noparse>" を外に出して、文字のまま出す
        static string NoParse(string text) =>
            text.Length == 0 ? "" : "<noparse>" + Regex.Replace(text, "</noparse>", m => "<</noparse>" + m.Value.Substring(1) + "<noparse>", RegexOptions.IgnoreCase) + "</noparse>";

        static string NaiveNoParse(string text) => "<noparse>" + text + "</noparse>";

        static string CharAt(string text, int index) => index >= 0 && index < text.Length ? text[index].ToString() : "?";

        // コードポイントの並びから文字列を作る（結合文字や ZWJ をソースに直接書くと、エディタの正規化で消えたり変わったりしうる）
        static string S(params int[] codePoints) => string.Concat(codePoints.Select(char.ConvertFromUtf32));

        // TestStrings.json の各キーの "long"。JsonUtility は辞書を読めないので正規表現で読む。
        // 値に " とエスケープ（\" や \uXXXX）を含まず、"long" より前に } が無い前提。エスケープを含む値は FAIL にする
        Dictionary<string, string> ParseLongStrings(string json)
        {
            var result = new Dictionary<string, string>();
            foreach (Match match in Regex.Matches(json, "\"(?<key>[a-z-]+)\"\\s*:\\s*\\{[^}]*\"long\"\\s*:\\s*\"(?<long>[^\"]*)\""))
            {
                var value = match.Groups["long"].Value;
                if (value.IndexOf('\\') >= 0)
                    Fail($"premise broken: TestStrings.json '{match.Groups["key"].Value}' has an escape sequence");
                else
                    result[match.Groups["key"].Value] = value;
            }
            return result;
        }

        IEnumerator MeasureNoParse()
        {
            var container = m_ScreenRoot.Q<VisualElement>("typewriter");
            if (container == null)
            {
                Fail("element 'typewriter' not found");
                yield break;
            }
            // 本文に < とタグの形を含む。タグとして解釈されなければ、parsedText が本文と同じになる
            var cases = new[]
            {
                ("e-noparse", "HP<10 <b>bold</b>"),
                // 本文に </noparse> そのものが含まれる。素朴に囲む形（naive）は、そこで noparse が閉じる
                ("e-noparse-close", "a</noparse><b>b</b>"),
                ("e-noparse-close-upper", "a</NOPARSE><b>b</b>"),
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
                ("ja-dakuten-decomposed", S(0x304B, 0x3099)),
                ("latin-combining-acute", S(0x0065, 0x0301)),
                ("emoji-surrogate-pair", S(0x1F600)),
                ("emoji-zwj-family", S(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467)),
                ("flag-regional-indicators", S(0x1F1EF, 0x1F1F5)),
                ("thai-tone-marks", S(0x0E17, 0x0E35, 0x0E48)),
                ("thai-sara-am", S(0x0E17, 0x0E33)),
                ("arabic-fatha", S(0x0628, 0x064E)),
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
            m_Reported.Add(item);
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
            if (m_Finished)
                return;
            m_Finished = true;
            StopLoops();
            foreach (var item in ExpectedItems.Where(i => !m_Reported.Contains(i)))
                Fail($"no RESULT for item={item}");
            Debug.Log($"{Prefix} messages warning={m_Warnings}");
            foreach (var message in m_Messages)
                Debug.Log($"{Prefix} message {message}");
            Debug.Log($"{Prefix} DONE results={m_Results} expected={ExpectedItems.Length} mismatches={m_Mismatches} failures={m_Failures}");
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition.StartsWith(Prefix, StringComparison.Ordinal))
                return;
            if (type == LogType.Warning)
                m_Warnings++;
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Fail($"{type}: {condition}");
            else
                return;
            if (m_Messages.Count < MaxMessages)
                m_Messages.Add($"{type}: {condition}");
        }
    }
}
