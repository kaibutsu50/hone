using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // World Space の panel に、カメラ経由の入力（PanelInputConfiguration + EventSystem + InputSystemUIInputModule）が届き、
    // Hone.Button と Hone.Dialog が World Space の panel で動くかの検証。Input System の合成 Mouse を作り、要素の位置を screen 座標にして、
    // ポインタを動かし press / release を積む。各ステップは [WorldSpaceProbe] RESULT 行を 1 行出す。
    //
    // シーンには World Space の PanelRenderer が 2 枚ある（どちらも WorldSpace.uxml: Hone.Button の "target" と、閉じた Hone.Dialog の "dialog"）。
    //   1 枚目（この GameObject）: 1920×1080。Button と Dialog の検証の本体。
    //   2 枚目（GameObject 名 PanelRenderer2、別 PanelSettings）: 1 つのダイアログ程度の小さい panel。1 枚目の手前の右側に置く。
    //
    // ステップ（1 ステップが 1 RESULT 行。pass=false は FAIL に数える）:
    //   hover / active / focus / click   1 枚目の Button
    //   open / overlay / footer          1 枚目の Dialog
    //   open-narrow                      2 枚目の Dialog の content の幅（panel が 512px より狭いとき）
    //   cross-click                      1 枚目の Button → 2 枚目の Dialog の OK の順にクリックして、両方の clicked が届くか
    //   cross-focus                      1 枚目の Button にフォーカスを当て、2 枚目の Dialog を Open → Close したときの両 panel の focusedElement（記録のみ）
    //
    // 要素の screen 座標は、要素の worldBound の中心を、panel の root の worldBound の中心を原点にした値とみなし、
    // PanelRenderer の transform（panel の中央が位置）を通して world に直し、Camera.WorldToScreenPoint に渡して出す。
    // 変換は、Button の中心が transform.position と一致することを前提の確認で確かめる。Screen.width などは見ない。
    //
    // 判定するのは測定の前提（panel の準備、型、シーンの構成）と、各ステップの期待値、Error / Exception のログ、時間切れ。
    // 失敗はすべて [WorldSpaceProbe] FAIL 行で出し、最後に FAIL total=… を出して終了コード 1 で終了する。全部通れば DONE steps=N failed=0 を出して終了コード 0 で終了する。
    // 前提が崩れたときは何も押さず、RESULT 行を出さない。
    // Editor の Play Mode では Application.Quit が効かないので、Player で回す。
    [RequireComponent(typeof(PanelRenderer))]
    public class WorldSpaceProbe : MonoBehaviour
    {
        const int MaxWaitFrames = 300;
        const int SettleFrames = 5;
        const float MaxRunSeconds = 60f;
        const float ScreenTolerance = 2f;
        const float OpacityTolerance = 0.01f;
        const string Prefix = "[WorldSpaceProbe]";
        const string SecondPanelName = "PanelRenderer2";

        // 1 枚の World Space の panel
        class Surface
        {
            public string Label;
            public PanelRenderer Renderer;
            public VisualElement Root;
            public UnityEngine.UIElements.Button Target;
            public Dialog Dialog;
        }

        Surface m_First;
        Surface m_Second;
        Camera m_Camera;
        Mouse m_Mouse;
        Vector2 m_Pointer;
        int m_Steps;
        bool m_Finished;
        readonly List<string> m_Failures = new List<string>();

        void Awake()
        {
            // panel の準備中に出た Error / Exception も拾うため、最初に購読する
            Application.logMessageReceived += OnLog;
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            m_First = Register("first", GetComponent<PanelRenderer>());
            var second = FindObjectsByType<PanelRenderer>().FirstOrDefault(p => p.gameObject.name == SecondPanelName);
            if (second != null)
                m_Second = Register("second", second);
        }

        static Surface Register(string label, PanelRenderer renderer)
        {
            var surface = new Surface { Label = label, Renderer = renderer };
            renderer.RegisterUIReloadCallback((panelRenderer, root, version) => surface.Root = root);
            return surface;
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void Update()
        {
            if (!m_Finished && Time.realtimeSinceStartup > MaxRunSeconds)
            {
                StopAllCoroutines();
                Fail($"did not finish within {MaxRunSeconds}s");
                Finish();
            }
        }

        IEnumerator Start()
        {
            // ウィンドウが非アクティブな Player でも回し続ける。Input System は既定ではフォーカスの無いウィンドウへの入力（合成デバイス分も含む）を捨てる
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            if (m_Second == null)
            {
                Fail($"no PanelRenderer named {SecondPanelName} in the scene");
                Finish();
                yield break;
            }

            var frames = 0;
            while (frames < MaxWaitFrames && !IsReady())
            {
                frames++;
                yield return null;
            }

            if (!IsReady())
            {
                Fail($"panel was not ready within {frames} frames");
                Finish();
                yield break;
            }

            m_Camera = Camera.main;
            CheckPrerequisites(frames);
            if (m_Failures.Count > 0)
            {
                Finish();
                yield break;
            }

            m_Mouse = InputSystem.AddDevice<Mouse>("WorldSpaceProbeMouse");
            try
            {
                yield return RunButtonSteps();
                yield return RunDialogSteps();
                yield return RunCrossPanelSteps();
            }
            finally
            {
                InputSystem.RemoveDevice(m_Mouse);
            }

            Finish();
        }

        bool IsReady()
        {
            return IsReady(m_First) && (m_Second == null || IsReady(m_Second));
        }

        static bool IsReady(Surface surface)
        {
            if (surface.Root == null || surface.Root.panel == null)
                return false;
            surface.Target = surface.Root.Q<UnityEngine.UIElements.Button>("target");
            surface.Dialog = surface.Root.Q<Dialog>("dialog");
            return surface.Target != null && surface.Dialog != null &&
                surface.Target.worldBound.width > 0 && surface.Target.worldBound.height > 0;
        }

        void CheckPrerequisites(int waitedFrames)
        {
            var panelSettings = m_First.Renderer.panelSettings;
            var eventSystem = FindAnyObjectByType<EventSystem>();
            var module = FindAnyObjectByType<InputSystemUIInputModule>();
            var config = FindAnyObjectByType<PanelInputConfiguration>();
            var mainIsEventCamera = config != null && m_Camera != null &&
                (config.defaultEventCameraIsMainCamera || (config.eventCameras != null && config.eventCameras.Contains(m_Camera)));
            Debug.Log(
                $"{Prefix} HEADER unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} " +
                $"renderMode={(panelSettings != null ? panelSettings.renderMode.ToString() : "n/a")} " +
                $"eventSystem={(eventSystem != null)} inputModule={(module != null)} panelInputConfiguration={(config != null)} " +
                $"processWorldSpaceInput={(config != null && config.processWorldSpaceInput)} camera={(m_Camera != null ? m_Camera.name : "null")} " +
                $"mainIsEventCamera={mainIsEventCamera} waitedFrames={waitedFrames}");

            foreach (var surface in new[] { m_First, m_Second })
            {
                var settings = surface.Renderer.panelSettings;
                Debug.Log(
                    $"{Prefix} HEADER panel={surface.Label} renderMode={(settings != null ? settings.renderMode.ToString() : "n/a")} " +
                    $"panelSettings={(settings != null ? settings.name : "null")} sizeMode={surface.Renderer.worldSpaceSizeMode} " +
                    $"layout={surface.Root.resolvedStyle.width:F0}x{surface.Root.resolvedStyle.height:F0} rootWorldBound={surface.Root.worldBound} " +
                    $"position={surface.Renderer.transform.position} button={surface.Target.GetType().FullName} buttonWorldBound={surface.Target.worldBound} " +
                    $"dialogOpen={surface.Dialog.isOpen}");

                // Hone.Button と Hone.Dialog を参照できる asmdef にあるが、UXML のタグが Unity 標準の Button に化けていないかを型名で確かめる
                if (surface.Target.GetType().FullName != "Hone.Button")
                    Fail($"the target of the {surface.Label} panel is {surface.Target.GetType().FullName}, not Hone.Button");
                if (settings == null || settings.renderMode != PanelRenderMode.WorldSpace)
                    Fail($"the PanelSettings of the {surface.Label} panel is not World Space");
                if (surface.Renderer.worldSpaceSizeMode != WorldSpaceSizeMode.Fixed)
                    Fail($"worldSpaceSizeMode of the {surface.Label} panel is {surface.Renderer.worldSpaceSizeMode}, not Fixed");
                if (surface.Dialog.isOpen)
                    Fail($"the Dialog of the {surface.Label} panel is open at the start");
            }

            if (m_First.Renderer.panelSettings == m_Second.Renderer.panelSettings)
                Fail("the two panels share one PanelSettings (they would be the same panel)");
            if (eventSystem == null || module == null)
                Fail("no EventSystem with InputSystemUIInputModule in the scene");
            if (config == null || !config.processWorldSpaceInput)
                Fail("no PanelInputConfiguration with processWorldSpaceInput = true in the scene");
            if (m_Camera == null)
                Fail("no camera tagged MainCamera");
            else if (config != null && !mainIsEventCamera)
                Fail("the Main Camera is not an event camera of the PanelInputConfiguration");
            if (m_Failures.Count > 0)
                return;

            // 変換の確認: panel の中央にある Button の中心が、PanelRenderer の transform.position の screen 座標と一致すること
            foreach (var surface in new[] { m_First, m_Second })
            {
                var expected = (Vector2)m_Camera.WorldToScreenPoint(surface.Renderer.transform.position);
                var actual = ScreenPoint(surface, surface.Target.worldBound.center);
                Debug.Log($"{Prefix} POINTER check panel={surface.Label} buttonCenter=({actual.x:F1},{actual.y:F1}) transformPosition=({expected.x:F1},{expected.y:F1})");
                if (Vector2.Distance(actual, expected) > ScreenTolerance)
                    Fail($"the screen point of the {surface.Label} Button centre does not match the panel position (tolerance {ScreenTolerance}px)");
            }
        }

        // ---- Button（1 枚目） ----

        IEnumerator RunButtonSteps()
        {
            var button = m_First.Target;
            var clicked = 0;
            button.clicked += () => clicked++;
            var borderBefore = button.resolvedStyle.borderTopWidth;
            var opacityBefore = button.resolvedStyle.opacity;

            yield return MoveTo(CenterOf(m_First, button));
            var hover = button.resolvedStyle.opacity;
            Result("hover", Approx(opacityBefore, 1f) && Approx(hover, 0.9f), $"opacityBefore={F(opacityBefore)} opacity={F(hover)} expected=0.90");

            yield return Press();
            var active = button.resolvedStyle.opacity;
            Result("active", Approx(active, 0.8f), $"opacity={F(active)} expected=0.80");

            yield return Release();
            var focused = m_First.Root.panel.focusController.focusedElement == button;
            var borderAfter = button.resolvedStyle.borderTopWidth;
            Result("focus", focused && borderAfter > borderBefore,
                $"focused={Lower(focused)} borderTopWidthBefore={F(borderBefore)} borderTopWidthAfter={F(borderAfter)}");
            Result("click", clicked == 1, $"clicked={Lower(clicked == 1)} count={clicked}");
        }

        // ---- Dialog（1 枚目） ----

        IEnumerator RunDialogSteps()
        {
            var surface = m_First;
            var dialog = surface.Dialog;
            var closed = 0;
            dialog.closed += () => closed++;

            dialog.Open();
            yield return Frames(SettleFrames);
            MeasureDialog("open", surface, expectedBy: "max-width");

            // overlay の press。content の外側の点（panel の左上の角の近く）
            yield return MoveTo(ScreenPoint(surface, CornerPoint(surface, 12f)));
            yield return Press();
            var overlayClosed = closed == 1 && !dialog.isOpen;
            var restored = surface.Root.panel.focusController.focusedElement == surface.Target;
            Result("overlay", overlayClosed, $"closed={Lower(closed == 1)} closedCount={closed} isOpen={Lower(dialog.isOpen)} focusRestoredToButton={Lower(restored)}");
            yield return Release();

            // footer の OK の press / release。overlay の後ろに隠れていないか
            var ok = dialog.Q<UnityEngine.UIElements.Button>("ok");
            var okClicked = 0;
            ok.clicked += () => okClicked++;
            dialog.Open();
            yield return Frames(SettleFrames);
            yield return MoveTo(CenterOf(surface, ok));
            yield return Press();
            yield return Release();
            Result("footer", okClicked == 1, $"clicked={Lower(okClicked == 1)} count={okClicked} dialogStillOpen={Lower(dialog.isOpen)}");

            dialog.Close();
            yield return Frames(SettleFrames);
        }

        // content の worldBound と panel の root の worldBound、content の幅がどちらの規則で決まったかを RESULT に出す
        void MeasureDialog(string step, Surface surface, string expectedBy)
        {
            var root = surface.Root;
            var content = surface.Dialog.Q(className: "hone-dialog__content");
            var rootBound = root.worldBound;
            var contentBound = content.worldBound;
            const float epsilon = 0.0001f;
            var inside = contentBound.xMin >= rootBound.xMin - epsilon && contentBound.xMax <= rootBound.xMax + epsilon &&
                contentBound.yMin >= rootBound.yMin - epsilon && contentBound.yMax <= rootBound.yMax + epsilon;

            var width = content.resolvedStyle.width;
            var percent = root.resolvedStyle.width * 0.9f;
            var maxWidth = content.resolvedStyle.maxWidth.value;
            var by = Mathf.Abs(width - percent) < 0.5f ? "percent" : Mathf.Abs(width - maxWidth) < 0.5f ? "max-width" : "other";
            Result(step, inside && by == expectedBy,
                $"panel={surface.Label} insidePanel={Lower(inside)} contentWorldBound={contentBound} rootWorldBound={rootBound} " +
                $"contentWidthPx={F(width)} rootWidthPx={F(root.resolvedStyle.width)} percent90Px={F(percent)} maxWidthPx={F(maxWidth)} " +
                $"contentHeightPx={F(content.resolvedStyle.height)} rootHeightPx={F(root.resolvedStyle.height)} widthBy={by} expectedBy={expectedBy}");
        }

        // ---- 2 枚の panel ----

        IEnumerator RunCrossPanelSteps()
        {
            var buttonA = m_First.Target;
            var dialogB = m_Second.Dialog;
            var okB = dialogB.Q<UnityEngine.UIElements.Button>("ok");

            dialogB.Open();
            yield return Frames(SettleFrames);
            MeasureDialog("open-narrow", m_Second, expectedBy: "percent");

            // 1 枚目の Button → 2 枚目の Dialog の OK の順にクリックする。2 枚目の overlay が 1 枚目への入力を止めないこと
            var clickedA = 0;
            var clickedB = 0;
            buttonA.clicked += () => clickedA++;
            okB.clicked += () => clickedB++;
            yield return MoveTo(CenterOf(m_First, buttonA));
            yield return Press();
            yield return Release();
            yield return MoveTo(CenterOf(m_Second, okB));
            yield return Press();
            yield return Release();
            Result("cross-click", clickedA == 1 && clickedB == 1,
                $"firstButtonClicked={Lower(clickedA == 1)} firstButtonCount={clickedA} secondOkClicked={Lower(clickedB == 1)} secondOkCount={clickedB}");

            // 1 枚目の Button にフォーカスを当ててから、2 枚目の Dialog を Open → Close し、両 panel の focusedElement を記録する
            dialogB.Close();
            yield return Frames(SettleFrames);
            buttonA.Focus();
            yield return Frames(SettleFrames);
            var firstBefore = FocusedOf(m_First);
            var secondBefore = FocusedOf(m_Second);
            if (firstBefore != buttonA)
                Fail("the first Button did not take focus before the second Dialog was opened");

            dialogB.Open();
            yield return Frames(SettleFrames);
            var firstOpen = FocusedOf(m_First);
            var secondOpen = FocusedOf(m_Second);

            dialogB.Close();
            yield return Frames(SettleFrames);
            var firstClose = FocusedOf(m_First);
            var secondClose = FocusedOf(m_Second);

            Result("cross-focus", firstBefore == buttonA,
                $"firstBefore={Describe(firstBefore)} secondBefore={Describe(secondBefore)} " +
                $"firstAfterOpen={Describe(firstOpen)} secondAfterOpen={Describe(secondOpen)} " +
                $"firstAfterClose={Describe(firstClose)} secondAfterClose={Describe(secondClose)} " +
                $"firstButtonFocusedAfterClose={Lower(firstClose == buttonA)}");
        }

        // ---- 座標と入力 ----

        // root の worldBound の中心を原点にした値を、panel の中央を位置とする PanelRenderer の transform で world に直し、screen 座標にする。
        // World Space の worldBound は world 単位で、y が上向き（layout の下端にある footer の y が、root の中心より小さい）なので、反転しない
        Vector2 ScreenPoint(Surface surface, Vector2 worldBoundPoint)
        {
            var local = worldBoundPoint - surface.Root.worldBound.center;
            var world = surface.Renderer.transform.TransformPoint(new Vector3(local.x, local.y, 0f));
            var screen = m_Camera.WorldToScreenPoint(world);
            if (screen.z <= 0)
                Fail($"a point of the {surface.Label} panel is behind the camera (depth={screen.z})");
            else if (!m_Camera.pixelRect.Contains(new Vector2(screen.x, screen.y)))
                Fail($"a point of the {surface.Label} panel is outside the camera view: ({screen.x:F1},{screen.y:F1})");
            return new Vector2(screen.x, screen.y);
        }

        Vector2 CenterOf(Surface surface, VisualElement element)
        {
            var screen = ScreenPoint(surface, element.worldBound.center);
            Debug.Log($"{Prefix} POINTER panel={surface.Label} element={Describe(element)} worldBound={element.worldBound} screen=({screen.x:F1},{screen.y:F1})");
            return screen;
        }

        // panel の左上の角から、layout の px で offset だけ内側の点（worldBound の座標。y が上向きなので、上端は yMax）
        static Vector2 CornerPoint(Surface surface, float offsetPx)
        {
            var bound = surface.Root.worldBound;
            var offset = offsetPx * bound.width / surface.Root.resolvedStyle.width;
            return new Vector2(bound.xMin + offset, bound.yMax - offset);
        }

        IEnumerator MoveTo(Vector2 screen)
        {
            m_Pointer = screen;
            QueueMouse(false);
            yield return Frames(SettleFrames);
        }

        IEnumerator Press()
        {
            QueueMouse(true);
            yield return Frames(SettleFrames);
        }

        IEnumerator Release()
        {
            QueueMouse(false);
            yield return Frames(SettleFrames);
        }

        void QueueMouse(bool leftDown)
        {
            var state = new MouseState { position = m_Pointer };
            if (leftDown)
                state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            InputSystem.QueueStateEvent(m_Mouse, state);
        }

        static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        // ---- 結果と終了 ----

        static VisualElement FocusedOf(Surface surface) => surface.Root.panel.focusController.focusedElement as VisualElement;

        static string Describe(VisualElement element)
        {
            if (element == null)
                return "null";
            return string.IsNullOrEmpty(element.name) ? element.GetType().Name : $"{element.GetType().Name}#{element.name}";
        }

        static bool Approx(float value, float expected) => Mathf.Abs(value - expected) <= OpacityTolerance;

        static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

        static string Lower(bool value) => value ? "true" : "false";

        void Result(string step, bool pass, string values)
        {
            m_Steps++;
            Debug.Log($"{Prefix} RESULT step={step} {values} pass={Lower(pass)}");
            if (!pass)
                Fail($"step {step} did not meet its expectation");
        }

        // 1 frame 待って、その間に出たログも数えてから終了する。終了コードは待った後の失敗の件数で決める
        void Finish()
        {
            if (m_Finished)
                return;
            m_Finished = true;
            StartCoroutine(QuitAfterFrame());
        }

        IEnumerator QuitAfterFrame()
        {
            yield return null;
            if (m_Failures.Count == 0)
            {
                Debug.Log($"{Prefix} DONE steps={m_Steps} failed=0");
                Application.Quit(0);
            }
            else
            {
                Debug.LogError($"{Prefix} FAIL total={m_Failures.Count} first={m_Failures[0]}");
                Application.Quit(1);
            }
        }

        void Fail(string reason)
        {
            m_Failures.Add(reason);
            Debug.LogError($"{Prefix} FAIL {reason}");
        }

        // Probe 自身のログ（Fail の LogError を含む）は数えない。それ以外の Error / Exception / Assert は FAIL にする
        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || condition.StartsWith(Prefix, StringComparison.Ordinal))
                return;
            Fail($"{type}: {condition}");
        }
    }
}
