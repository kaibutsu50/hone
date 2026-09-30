using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // Issue #12（FocusScope の trap 機構の検証）の実験用。FocusTrap.uxml（外側 4 + コンテナ内 3 のボタン）に対して、
    // trap の 2 案を入力経路ごとに試し、フォーカスがどのフレームでどこにあったかを [FocusTrapProbe] のログに出す。FocusScope の本実装ではない。
    //
    // 入力は Input System の合成デバイスへ QueueStateEvent して、UI map の既定バインディング（InputForUI / InputSystemUIInputModule）を通す。
    // "Nav:*" だけは NavigationMoveEvent を SendEvent で直接送る（入力層を通らない）。
    // trap の成否は判定しない。RESULT 行を人が読む。Probe が判定するのは測定の前提（開始フォーカス、対照ステップで入力と描画検出が届いたか、
    // シーンと EventSystem の有無の一致、Error / Exception のログ）だけで、崩れていれば FAIL を出し、Player では終了コード 1 で終了する。
    // Editor の Play Mode では Application.Quit が効かないので、Player で回す。
    [RequireComponent(typeof(PanelRenderer))]
    public class FocusTrapProbe : MonoBehaviour
    {
        enum Mode
        {
            None,              // 対照。trap を入れない
            Preempt,           // 案 1: trap のルートに TrickleDown で登録した NavigationMoveEvent で IgnoreEvent し、scope 内の次の要素へ Focus()。
                               //       target が trap の中にあるときだけ呼ばれる（フォーカスが無いときは呼ばれない）
            PreemptKey,        // 案 1 + Tab の KeyDownEvent も同様に処理
            PullbackImmediate, // 案 2: FocusOutEvent の relatedTarget が scope 外なら、その場で FocusOut の target へ Focus()。relatedTarget が null なら何もしない
            PullbackDeferred,  // 案 2: 同上だが、次のフレームの Update で Focus()
        }

        readonly struct Step
        {
            public readonly string Case;
            public readonly Mode Mode;
            public readonly string Start;
            public readonly string Input;

            public Step(string caseId, Mode mode, string start, string input)
            {
                Case = caseId;
                Mode = mode;
                Start = start;
                Input = input;
            }
        }

        readonly struct Sample
        {
            public readonly int Offset;
            public readonly string AtUpdate;
            public readonly string AtEndOfFrame;
            public readonly Focusable EndOfFrameElement;

            public Sample(int offset, string atUpdate, Focusable endOfFrameElement)
            {
                Offset = offset;
                AtUpdate = atUpdate;
                AtEndOfFrame = Name(endOfFrameElement);
                EndOfFrameElement = endOfFrameElement;
            }
        }

        const int MaxWaitFrames = 300;
        const float MaxRunSeconds = 240f;
        const int SampleFrames = 8;
        const int HoldFrames = 3;
        const float DirectionEpsilon = 1f;
        // FocusTrap.uss の .ft-button:focus と同じ値にする
        static readonly Color FocusColor = new Color32(59, 130, 246, 255);
        static readonly string[] ButtonNames = { "out-top", "out-bottom", "out-left", "out-right", "in-1", "in-2", "in-3" };
        static readonly string[] Dpad = { "Up", "Down", "Left", "Right" };

        VisualElement m_Root;
        VisualElement m_Trap;
        IPanel m_Panel;
        Gamepad m_Pad;
        Keyboard m_Keyboard;

        bool m_Acting;
        bool m_Finished;
        int m_PressFrame;
        int m_FirstEventOffset;
        readonly List<string> m_Notes = new List<string>();
        readonly List<string> m_Failures = new List<string>();
        readonly Queue<(int frame, Action action)> m_Deferred = new Queue<(int, Action)>();

        EventCallback<NavigationMoveEvent> m_OnMove;
        EventCallback<KeyDownEvent> m_OnKeyDown;
        EventCallback<FocusOutEvent> m_OnFocusOut;
        Mode m_Attached = Mode.None;

        void Awake()
        {
            // PanelRenderer の root は public では reload callback 経由でしか取れない
            GetComponent<PanelRenderer>().RegisterUIReloadCallback((panelRenderer, root, version) => m_Root = root);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
        }

        void Update()
        {
            while (m_Deferred.Count > 0 && m_Deferred.Peek().frame < Time.frameCount)
                m_Deferred.Dequeue().action();

            if (!m_Finished && Time.realtimeSinceStartup > MaxRunSeconds)
            {
                m_Finished = true;
                Debug.LogError($"[FocusTrapProbe] did not finish within {MaxRunSeconds}s. first failure: {(m_Failures.Count > 0 ? m_Failures[0] : "none")}");
                Application.Quit(1);
            }
        }

        IEnumerator Start()
        {
            // ウィンドウが非アクティブな Player でも回し続ける。Input System は既定ではフォーカスの無いウィンドウへの入力（合成デバイス分も含む）を捨てる
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            var frames = 0;
            while (frames < MaxWaitFrames && !IsReady())
            {
                frames++;
                yield return null;
            }

            if (!IsReady())
            {
                m_Finished = true;
                Debug.LogError($"[FocusTrapProbe] panel was not ready within {frames} frames");
                Application.Quit(1);
                yield break;
            }

            Application.logMessageReceived += OnLog;
            m_Panel = m_Root.panel;
            m_Trap = m_Root.Q("trap");
            m_Pad = InputSystem.AddDevice<Gamepad>("FocusTrapPad");
            m_Keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            RegisterProbes();
            LogHeader(frames);

            var steps = BuildSteps();
            foreach (var step in steps)
            {
                if (m_Root.panel != m_Panel || m_Trap.panel != m_Panel)
                {
                    Fail($"panel was reloaded before case={step.Case} mode={step.Mode} input={step.Input}");
                    break;
                }
                yield return RunStep(step);
            }

            var failed = m_Failures.Count > 0;
            Debug.Log($"[FocusTrapProbe] DONE steps={steps.Count} failed={m_Failures.Count}{(failed ? $" first={m_Failures[0]}" : "")}");
            m_Finished = true;
            yield return null;
            Application.Quit(failed ? 1 : 0);
        }

        bool IsReady()
        {
            if (m_Root == null || m_Root.panel == null)
                return false;
            var trap = m_Root.Q("trap");
            return trap != null && trap.worldBound.width > 0 && ButtonNames.All(n => m_Root.Q(n) != null);
        }

        void LogHeader(int waitedFrames)
        {
            var eventSystem = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            var module = FindAnyObjectByType<InputSystemUIInputModule>();
            var scene = SceneManager.GetActiveScene().name;
            Debug.Log(
                $"[FocusTrapProbe] HEADER unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} scene={scene} " +
                $"eventSystem={(eventSystem != null)} inputModule={(module != null)} moduleActions={(module != null && module.actionsAsset != null ? module.actionsAsset.name : "n/a")} " +
                $"projectWideActions={(InputSystem.actions != null ? InputSystem.actions.name : "null")} waitedFrames={waitedFrames}");
            Debug.Log($"[FocusTrapProbe] HEADER devices={string.Join(",", InputSystem.devices.Select(d => d.name))}");

            // FocusTrap.unity は EventSystem なし、FocusTrapEventSystem.unity はあり
            var expectEventSystem = scene.EndsWith("EventSystem");
            if ((eventSystem != null) != expectEventSystem)
                Fail($"scene {scene} expects eventSystem={expectEventSystem} but found {eventSystem != null}");
            if (InputSystem.actions == null && module == null)
                Fail("no UI input path: project-wide actions are null and no InputSystemUIInputModule");
        }

        void RegisterProbes()
        {
            // 入力が UI Toolkit のどのイベントとして届くかを記録する。trap の handler より先に見るため、最上位に TrickleDown で登録する。
            // フォーカスが無いときの NavigationMoveEvent は panel の最上位（m_Root より上）へ送られる（6000.7.0b2 の実測）ので、panel の visualTree に登録する
            var top = m_Panel.visualTree;
            top.RegisterCallback<NavigationMoveEvent>(e => Note("event", $"NavigationMove {e.direction} target={Name(e.target)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<KeyDownEvent>(e => Note("event", $"KeyDown keyCode={e.keyCode} char={(int)e.character} shift={e.shiftKey} target={Name(e.target)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<FocusOutEvent>(e => Note("focus", $"FocusOut {Name(e.target)} -> {Name(e.relatedTarget)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<FocusInEvent>(e => Note("focus", $"FocusIn {Name(e.target)} <- {Name(e.relatedTarget)}"), TrickleDown.TrickleDown);

            // 外側の要素がフォーカス色で描画されたことを記録する。フレーム終端の標本では拾えない一瞬の描画を見る代理指標。
            // 検出器が働いていることは、外へ出る対照ステップ（Mode.None の D-pad）で必ず記録されることで確かめる（RunStep）
            foreach (var name in ButtonNames.Where(n => n.StartsWith("out-")))
            {
                var button = m_Root.Q(name);
                button.generateVisualContent += _ =>
                {
                    if (m_Acting && button.resolvedStyle.backgroundColor == FocusColor)
                        Note("paint", $"{name} painted with focus color");
                };
            }
        }

        static List<Step> BuildSteps()
        {
            var steps = new List<Step>();

            void Exits(string caseId, Mode mode)
            {
                steps.Add(new Step(caseId, mode, "in-1", "Up"));
                steps.Add(new Step(caseId, mode, "in-3", "Down"));
                steps.Add(new Step(caseId, mode, "in-2", "Left"));
                steps.Add(new Step(caseId, mode, "in-2", "Right"));
            }

            void Interior(string caseId, Mode mode)
            {
                steps.Add(new Step(caseId, mode, "in-1", "Down"));
                steps.Add(new Step(caseId, mode, "in-3", "Up"));
            }

            // ケース 3（起動直後）: Probe が一度も Focus() を呼んでいない状態。必ず先頭に置く。
            // 1 つ目は合成 Gamepad への最初の入力でもあるので、2 つ目で「デバイスの最初の入力が落ちた」のと区別する
            steps.Add(new Step("3-fresh", Mode.None, null, "Down"));
            steps.Add(new Step("3-fresh", Mode.None, null, "Up"));

            // ケース 1: 案 1（None は対照）
            Exits("1", Mode.None);
            Exits("1", Mode.Preempt);
            Interior("1", Mode.Preempt);

            // ケース 2: 案 2
            Exits("2", Mode.PullbackImmediate);
            Interior("2", Mode.PullbackImmediate);
            Exits("2", Mode.PullbackDeferred);

            // ケース 3: フォーカス無し（ケース 1・2 のあとに Blur() した状態）
            foreach (var mode in new[] { Mode.None, Mode.Preempt })
                foreach (var input in Dpad)
                    steps.Add(new Step("3", mode, null, input));

            // ケース 4（ケース 1・3 を EventSystem ありで再実行）は専用ステップを持たない。FocusTrapEventSystem.unity で同じステップを回す

            // ケース 5: Tab / Shift+Tab（合成 Keyboard の state + text event）
            foreach (var mode in new[] { Mode.None, Mode.Preempt, Mode.PreemptKey })
            {
                steps.Add(new Step("5", mode, "in-3", "Tab"));
                steps.Add(new Step("5", mode, "in-1", "ShiftTab"));
            }
            // 対照: text event を送らず、キーの state だけで Tab として届くか
            steps.Add(new Step("5", Mode.None, "in-3", "TabStateOnly"));
            // 対照: 入力層を通さず NavigationMoveEvent(Next / Previous) を直接送る
            foreach (var mode in new[] { Mode.None, Mode.Preempt })
            {
                steps.Add(new Step("5", mode, "in-3", "Nav:Next"));
                steps.Add(new Step("5", mode, "in-1", "Nav:Previous"));
            }

            return steps;
        }

        IEnumerator RunStep(Step step)
        {
            Detach();
            ReleaseInputs();
            m_Panel.focusController.focusedElement?.Blur();
            yield return Frames(3);
            if (step.Start != null)
                m_Root.Q(step.Start).Focus();
            yield return Frames(3);
            var startFocused = FocusName();
            Attach(step.Mode);

            m_Notes.Clear();
            m_FirstEventOffset = -1;
            m_PressFrame = Time.frameCount;
            m_Acting = true;
            Press(step.Input);

            // +0: 押した frame の終端。SendEvent（Nav:*）は同じ frame の中で処理される
            var samples = new List<Sample>();
            yield return new WaitForEndOfFrame();
            samples.Add(new Sample(Time.frameCount - m_PressFrame, "-", m_Panel.focusController.focusedElement));
            for (var i = 1; i <= SampleFrames; i++)
            {
                yield return null;
                var atUpdate = FocusName();
                yield return new WaitForEndOfFrame();
                samples.Add(new Sample(Time.frameCount - m_PressFrame, atUpdate, m_Panel.focusController.focusedElement));
                if (i == HoldFrames)
                    ReleaseInputs();
            }
            m_Acting = false;

            Report(step, startFocused, samples);
        }

        static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        void Report(Step step, string startFocused, List<Sample> samples)
        {
            var last = samples[samples.Count - 1];
            var final = last.AtEndOfFrame;
            string settled;
            if (samples.All(s => s.AtEndOfFrame == startFocused))
            {
                settled = "unchanged";
            }
            else
            {
                var index = samples.Count - 1;
                while (index > 0 && samples[index - 1].AtEndOfFrame == final)
                    index--;
                settled = $"+{samples[index].Offset}";
            }
            var inTrap = last.EndOfFrameElement is VisualElement finalElement && m_Trap.Contains(finalElement);
            var exposed = samples.Any(s => s.AtUpdate.StartsWith("out-") || s.AtEndOfFrame.StartsWith("out-"));
            var painted = m_Notes.Any(n => n.Contains("painted with focus color"));
            var firstEvent = m_FirstEventOffset < 0 ? "n/a" : $"+{m_FirstEventOffset}";

            var sb = new StringBuilder();
            sb.Append($"[FocusTrapProbe] RESULT eventSystem={FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null} case={step.Case} mode={step.Mode} start={step.Start ?? "(none)"} input={step.Input}");
            sb.Append($" | startFocused={startFocused} final={final} inTrap={inTrap} firstEvent={firstEvent} settled={settled}");
            sb.Append($" outsideSampled={exposed} outsidePainted={painted}");
            sb.Append(" | samples:");
            foreach (var s in samples)
                sb.Append($" +{s.Offset}[upd={s.AtUpdate},eof={s.AtEndOfFrame}]");
            sb.Append(" | notes:");
            foreach (var n in m_Notes)
                sb.Append(" ; ").Append(n);
            Debug.Log(sb.ToString());

            // 測定の前提の確認。trap の成否は判定しない
            var label = $"case={step.Case} mode={step.Mode} input={step.Input}";
            if (startFocused != (step.Start ?? "null"))
                Fail($"{label}: start focus is {startFocused}, expected {step.Start ?? "null"}");
            if (step.Mode == Mode.None && step.Start != null && Dpad.Contains(step.Input))
            {
                if (m_FirstEventOffset < 0)
                    Fail($"{label}: D-pad input did not reach UI Toolkit");
                if (!painted)
                    Fail($"{label}: focus-color paint detector recorded nothing on a step that leaves the trap");
            }
        }

        void Fail(string reason)
        {
            m_Failures.Add(reason);
            Debug.LogError($"[FocusTrapProbe] FAIL {reason}");
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning || condition.StartsWith("[FocusTrapProbe]"))
                return;
            m_Failures.Add($"{type}: {condition}");
        }

        void Note(string kind, string message)
        {
            if (!m_Acting)
                return;
            var offset = Time.frameCount - m_PressFrame;
            if (kind == "event" && m_FirstEventOffset < 0)
                m_FirstEventOffset = offset;
            m_Notes.Add($"+{offset} {message}");
        }

        string FocusName() => Name(m_Panel.focusController.focusedElement);

        static string Name(object o)
        {
            if (o == null)
                return "null";
            if (o is VisualElement ve)
                return string.IsNullOrEmpty(ve.name) ? ve.GetType().Name : ve.name;
            return o.GetType().Name;
        }

        // ---- 入力 ----

        void Press(string input)
        {
            switch (input)
            {
                case "Up": QueueDpad(GamepadButton.DpadUp); break;
                case "Down": QueueDpad(GamepadButton.DpadDown); break;
                case "Left": QueueDpad(GamepadButton.DpadLeft); break;
                case "Right": QueueDpad(GamepadButton.DpadRight); break;
                case "Tab":
                    InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(Key.Tab));
                    InputSystem.QueueTextEvent(m_Keyboard, '\t');
                    break;
                case "ShiftTab":
                    InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(Key.LeftShift, Key.Tab));
                    InputSystem.QueueTextEvent(m_Keyboard, '\t');
                    break;
                case "TabStateOnly":
                    InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState(Key.Tab));
                    break;
                case "Nav:Next": SendNavigation(NavigationMoveEvent.Direction.Next); break;
                case "Nav:Previous": SendNavigation(NavigationMoveEvent.Direction.Previous); break;
                default: throw new ArgumentException($"unknown input '{input}'");
            }
        }

        void QueueDpad(GamepadButton button)
        {
            InputSystem.QueueStateEvent(m_Pad, new GamepadState { buttons = 1u << (int)button });
        }

        void ReleaseInputs()
        {
            InputSystem.QueueStateEvent(m_Pad, new GamepadState());
            InputSystem.QueueStateEvent(m_Keyboard, new KeyboardState());
        }

        // フォーカスが無いときは、入力層経由と同じく panel の最上位へ送る
        void SendNavigation(NavigationMoveEvent.Direction direction)
        {
            var target = m_Panel.focusController.focusedElement as VisualElement ?? m_Panel.visualTree;
            using (var evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // ---- trap の実装（案 1・案 2） ----

        void Attach(Mode mode)
        {
            m_Attached = mode;
            switch (mode)
            {
                case Mode.Preempt:
                case Mode.PreemptKey:
                    m_OnMove = OnMovePreempt;
                    m_Trap.RegisterCallback(m_OnMove, TrickleDown.TrickleDown);
                    if (mode == Mode.PreemptKey)
                    {
                        m_OnKeyDown = OnKeyDownPreempt;
                        m_Trap.RegisterCallback(m_OnKeyDown, TrickleDown.TrickleDown);
                    }
                    break;
                case Mode.PullbackImmediate:
                case Mode.PullbackDeferred:
                    m_OnFocusOut = OnFocusOutPullback;
                    m_Trap.RegisterCallback(m_OnFocusOut, TrickleDown.TrickleDown);
                    break;
            }
        }

        void Detach()
        {
            if (m_OnMove != null)
                m_Trap.UnregisterCallback(m_OnMove, TrickleDown.TrickleDown);
            if (m_OnKeyDown != null)
                m_Trap.UnregisterCallback(m_OnKeyDown, TrickleDown.TrickleDown);
            if (m_OnFocusOut != null)
                m_Trap.UnregisterCallback(m_OnFocusOut, TrickleDown.TrickleDown);
            m_OnMove = null;
            m_OnKeyDown = null;
            m_OnFocusOut = null;
            m_Attached = Mode.None;
            // 前のステップで積んだ遅延 Focus() を次のステップへ持ち越さない
            m_Deferred.Clear();
        }

        void OnMovePreempt(NavigationMoveEvent evt)
        {
            MoveInScope(evt, evt.target as VisualElement, evt.direction, $"NavigationMove {evt.direction}");
        }

        void OnKeyDownPreempt(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Tab && evt.character != '\t')
                return;
            var direction = evt.shiftKey ? NavigationMoveEvent.Direction.Previous : NavigationMoveEvent.Direction.Next;
            MoveInScope(evt, evt.target as VisualElement, direction, $"KeyDown Tab shift={evt.shiftKey}");
        }

        // IgnoreEvent で既定の移動を止めてから、候補があれば Focus() する。候補が無ければフォーカスは動かない
        void MoveInScope(EventBase evt, VisualElement current, NavigationMoveEvent.Direction direction, string label)
        {
            m_Panel.focusController.IgnoreEvent(evt);
            var before = FocusName();
            var next = FindNext(current, direction);
            if (next != null)
                next.Focus();
            Note("handler", $"preempt({label}) current={Name(current)} next={Name(next)} focusedBefore={before} focusedRightAfterFocus()={FocusName()}");
        }

        // scope 内の focusable から、方向に応じて次の要素を選ぶ。その方向に候補が無いとき、または current が scope 外のときは null。
        // Next / Previous は階層順（tabIndex は見ない）で、scope 内で循環する
        VisualElement FindNext(VisualElement current, NavigationMoveEvent.Direction direction)
        {
            var items = m_Trap.Query<VisualElement>().ToList().Where(e => e.focusable && e.canGrabFocus).ToList();
            if (current == null || !items.Contains(current))
                return null;

            if (direction == NavigationMoveEvent.Direction.Next || direction == NavigationMoveEvent.Direction.Previous)
            {
                var step = direction == NavigationMoveEvent.Direction.Next ? 1 : -1;
                return items[(items.IndexOf(current) + step + items.Count) % items.Count];
            }

            var origin = current.worldBound.center;
            VisualElement best = null;
            var bestDistance = float.MaxValue;
            foreach (var item in items)
            {
                if (item == current)
                    continue;
                var delta = item.worldBound.center - origin;
                var ahead = direction switch
                {
                    NavigationMoveEvent.Direction.Up => delta.y < -DirectionEpsilon,
                    NavigationMoveEvent.Direction.Down => delta.y > DirectionEpsilon,
                    NavigationMoveEvent.Direction.Left => delta.x < -DirectionEpsilon,
                    NavigationMoveEvent.Direction.Right => delta.x > DirectionEpsilon,
                    _ => false,
                };
                if (ahead && delta.sqrMagnitude < bestDistance)
                {
                    best = item;
                    bestDistance = delta.sqrMagnitude;
                }
            }
            return best;
        }

        void OnFocusOutPullback(FocusOutEvent evt)
        {
            var from = evt.target as VisualElement;
            var to = evt.relatedTarget as VisualElement;
            if (from == null || to == null)
            {
                Note("handler", $"pullback(FocusOut {Name(from)} -> {Name(to)}) ignored: no target or no relatedTarget");
                return;
            }
            if (m_Trap.Contains(to))
                return;

            if (m_Attached == Mode.PullbackDeferred)
            {
                m_Deferred.Enqueue((Time.frameCount, () =>
                {
                    from.Focus();
                    Note("handler", $"pullback deferred: Focus({Name(from)}) done, focusedRightAfterFocus()={FocusName()}");
                }));
                Note("handler", $"pullback(FocusOut {Name(from)} -> {Name(to)}) deferred to next frame");
                return;
            }

            from.Focus();
            Note("handler", $"pullback(FocusOut {Name(from)} -> {Name(to)}) Focus({Name(from)}) called, focusedRightAfterFocus()={FocusName()}");
        }
    }
}
