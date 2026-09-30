using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

namespace Hone.Sandbox.Experiments
{
    // #12 の実験用。FocusTrap.uxml（外側 4 + コンテナ内 3 のボタン）に対して、trap の 2 案を入力経路ごとに試し、
    // フォーカスがどのフレームでどこにあったかを [FocusTrapProbe] のログに出す。FocusScope の本実装ではない。
    //
    // 入力は Input System の合成デバイスへ QueueStateEvent して、UI map の既定バインディング（InputForUI / InputSystemUIInputModule）を通す。
    // "Nav:*" だけは NavigationMoveEvent を SendEvent で直接送る（入力層を通らない）。
    // 全ステップを回したら終了する（Player では Application.Quit。失敗時は終了コード 1）。
    [RequireComponent(typeof(PanelRenderer))]
    public class FocusTrapProbe : MonoBehaviour
    {
        enum Mode
        {
            None,              // 対照。trap を入れない
            Preempt,           // 案 1: trap の NavigationMoveEvent で IgnoreEvent し、scope 内の次の要素へ Focus()
            PreemptKey,        // 案 1 + Tab の KeyDownEvent も同様に処理
            PullbackImmediate, // 案 2: FocusOutEvent の relatedTarget が scope 外なら、その場で元の要素へ Focus()
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

            public Sample(int offset, string atUpdate, string atEndOfFrame)
            {
                Offset = offset;
                AtUpdate = atUpdate;
                AtEndOfFrame = atEndOfFrame;
            }
        }

        const int MaxWaitFrames = 300;
        const float MaxRunSeconds = 240f;
        const int SampleFrames = 8;
        const int HoldFrames = 3;
        static readonly Color FocusColor = new Color32(59, 130, 246, 255);
        static readonly string[] ButtonNames = { "out-top", "out-bottom", "out-left", "out-right", "in-1", "in-2", "in-3" };

        VisualElement m_Root;
        VisualElement m_Trap;
        IPanel m_Panel;
        Gamepad m_Pad;
        Keyboard m_Keyboard;

        bool m_Acting;
        int m_PressFrame;
        int m_FirstEventOffset;
        readonly List<string> m_Notes = new List<string>();
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

        void Update()
        {
            while (m_Deferred.Count > 0 && m_Deferred.Peek().frame < Time.frameCount)
                m_Deferred.Dequeue().action();

            if (Time.realtimeSinceStartup > MaxRunSeconds)
            {
                Debug.LogError($"[FocusTrapProbe] did not finish within {MaxRunSeconds}s");
                Application.Quit(1);
            }
        }

        IEnumerator Start()
        {
            // 合成デバイスの入力はフォーカスが無い Player では既定で捨てられる
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
                Debug.LogError($"[FocusTrapProbe] panel was not ready within {frames} frames");
                Application.Quit(1);
                yield break;
            }

            m_Panel = m_Root.panel;
            m_Trap = m_Root.Q("trap");
            m_Pad = InputSystem.AddDevice<Gamepad>("FocusTrapPad");
            m_Keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            RegisterProbes();
            LogHeader(frames);

            var steps = BuildSteps();
            foreach (var step in steps)
                yield return RunStep(step);

            Debug.Log($"[FocusTrapProbe] DONE steps={steps.Count}");
            yield return null;
            Application.Quit(0);
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
            Debug.Log(
                $"[FocusTrapProbe] HEADER unity={Application.unityVersion} platform={Application.platform} isEditor={Application.isEditor} " +
                $"eventSystem={(eventSystem != null)} inputModule={(module != null)} moduleActions={(module != null && module.actionsAsset != null ? module.actionsAsset.name : "n/a")} " +
                $"projectWideActions={(InputSystem.actions != null ? InputSystem.actions.name : "null")} waitedFrames={waitedFrames}");
            Debug.Log($"[FocusTrapProbe] HEADER devices={string.Join(",", InputSystem.devices.Select(d => d.name))}");
        }

        void RegisterProbes()
        {
            // 入力が UI Toolkit のどのイベントとして届くかを記録する（trap の有無に関係なく最初に見る）。
            // フォーカスが無いときの NavigationMoveEvent は panel の最上位へ送られ m_Root より上になるので、panel の visualTree に登録する
            var top = m_Panel.visualTree;
            top.RegisterCallback<NavigationMoveEvent>(e => Note("event", $"NavigationMove {e.direction} target={Name(e.target)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<KeyDownEvent>(e => Note("event", $"KeyDown keyCode={e.keyCode} char={(int)e.character} shift={e.shiftKey} target={Name(e.target)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<FocusOutEvent>(e => Note("focus", $"FocusOut {Name(e.target)} -> {Name(e.relatedTarget)}"), TrickleDown.TrickleDown);
            top.RegisterCallback<FocusInEvent>(e => Note("focus", $"FocusIn {Name(e.target)} <- {Name(e.relatedTarget)}"), TrickleDown.TrickleDown);

            // 「外側の要素がフォーカス色で描画されたフレーム」を数える。フレーム終端の標本では拾えない一瞬の描画を見る代理指標
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

            // ケース 1: 案 1（None は対照）
            Exits("1", Mode.None);
            Exits("1", Mode.Preempt);
            Interior("1", Mode.Preempt);

            // ケース 2: 案 2
            Exits("2", Mode.PullbackImmediate);
            Interior("2", Mode.PullbackImmediate);
            Exits("2", Mode.PullbackDeferred);

            // ケース 3: フォーカス無し
            foreach (var mode in new[] { Mode.None, Mode.Preempt })
                foreach (var input in new[] { "Down", "Up", "Left", "Right" })
                    steps.Add(new Step("3", mode, null, input));

            // ケース 5: Tab / Shift+Tab
            foreach (var mode in new[] { Mode.None, Mode.Preempt, Mode.PreemptKey })
            {
                steps.Add(new Step("5", mode, "in-3", "Tab"));
                steps.Add(new Step("5", mode, "in-1", "ShiftTab"));
            }
            steps.Add(new Step("5", Mode.None, "in-3", "TabStateOnly"));
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

            var samples = new List<Sample>();
            for (var i = 1; i <= SampleFrames; i++)
            {
                yield return null;
                var atUpdate = FocusName();
                yield return new WaitForEndOfFrame();
                samples.Add(new Sample(Time.frameCount - m_PressFrame, atUpdate, FocusName()));
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
            var final = samples[samples.Count - 1].AtEndOfFrame;
            var settled = samples.Count - 1;
            while (settled > 0 && samples[settled - 1].AtEndOfFrame == final)
                settled--;
            var inTrap = m_Trap.Q(final) != null;
            var exposed = samples.Any(s => s.AtEndOfFrame.StartsWith("out-"));
            var painted = m_Notes.Any(n => n.Contains("painted with focus color"));

            var sb = new StringBuilder();
            sb.Append($"[FocusTrapProbe] RESULT eventSystem={FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null} case={step.Case} mode={step.Mode} start={step.Start ?? "(none)"} input={step.Input}");
            sb.Append($" | startFocused={startFocused} final={final} inTrap={inTrap} firstEvent=+{m_FirstEventOffset} settled=+{samples[settled].Offset}");
            sb.Append($" outsideAtFrameEnd={exposed} outsidePainted={painted}");
            sb.Append(" | samples:");
            foreach (var s in samples)
                sb.Append($" +{s.Offset}[upd={s.AtUpdate},eof={s.AtEndOfFrame}]");
            sb.Append(" | notes:");
            foreach (var n in m_Notes)
                sb.Append(" ; ").Append(n);
            Debug.Log(sb.ToString());
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

        void SendNavigation(NavigationMoveEvent.Direction direction)
        {
            var target = m_Panel.focusController.focusedElement as VisualElement ?? m_Root;
            using (var evt = NavigationMoveEvent.GetPooled(direction))
            {
                evt.target = target;
                target.SendEvent(evt);
            }
        }

        // ---- trap 本体 ----

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

        void MoveInScope(EventBase evt, VisualElement current, NavigationMoveEvent.Direction direction, string label)
        {
            m_Panel.focusController.IgnoreEvent(evt);
            var before = FocusName();
            var next = FindNext(current, direction);
            if (next != null)
                next.Focus();
            Note("handler", $"preempt({label}) current={Name(current)} next={Name(next)} focusedBefore={before} focusedRightAfterFocus()={FocusName()}");
        }

        // scope 内の focusable から、方向に応じて次の要素を選ぶ。無ければ null（その場に留まる）。Next / Previous は scope 内で循環する
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
                    NavigationMoveEvent.Direction.Up => delta.y < 0,
                    NavigationMoveEvent.Direction.Down => delta.y > 0,
                    NavigationMoveEvent.Direction.Left => delta.x < 0,
                    NavigationMoveEvent.Direction.Right => delta.x > 0,
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
            if (from == null || to == null || m_Trap.Contains(to))
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
