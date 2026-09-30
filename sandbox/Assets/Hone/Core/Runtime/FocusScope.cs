using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hone.Core
{
    // subtree に限定したフォーカス移動と、初期フォーカス・復元。Dialog の open / close から Activate / Deactivate を呼ぶ。
    //
    // trap の方式: scope のルートで NavigationMoveEvent を TrickleDown で受け、IgnoreEvent で既定の移動を止め、同じ handler の中で次の要素に Focus() する。
    // StopPropagation も 1 frame の遅延も要らない。Next / Previous（Tab）も同じ NavigationMoveEvent で届く。
    // フォーカスが無いとき、D-pad の NavigationMoveEvent の target は panel の最上位で、この handler は呼ばれない。
    // 最初の入力が scope の外へ着地しないよう、scope を開いた時点で Activate() が初期フォーカスを当てる必要がある。
    [UxmlElement]
    public partial class FocusScope : VisualElement
    {
        // 2D 移動で「その方向にある」とみなすのに必要な、中心のずれ
        const float DirectionEpsilon = 1f;

        // true のとき、scope の子孫から外へフォーカスを出さない
        [UxmlAttribute]
        public bool trap { get; set; }

        // true のとき、panel に attach された時と Activate() 時に最初の focusable な子孫にフォーカスする
        [UxmlAttribute]
        public bool autoFocus { get; set; } = true;

        // Activate() 時点でフォーカスされていた scope 外の要素
        VisualElement m_Previous;

        public FocusScope()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        // 呼び出し時点のフォーカスを記憶し、autoFocus なら初期フォーカスを当てる
        public void Activate()
        {
            var focused = panel?.focusController.focusedElement as VisualElement;
            // 二度呼ばれたとき、scope の中へ移ったあとの要素を「元の要素」で上書きしない
            if (focused != null && !Contains(focused))
                m_Previous = focused;
            if (autoFocus)
                FocusFirst();
        }

        // 記憶した要素がまだ panel 上にあり focusable なら Focus() する。無ければ何もしない
        public void Deactivate()
        {
            var previous = m_Previous;
            m_Previous = null;
            if (previous != null && previous.panel != null && previous.canGrabFocus)
                previous.Focus();
        }

        // 子孫の focusable のうち、DFS 順で最初の要素に Focus() する
        public void FocusFirst()
        {
            var candidates = CollectCandidates();
            if (candidates.Count > 0)
                candidates[0].Focus();
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (autoFocus)
                Activate();
        }

        void OnNavigationMove(NavigationMoveEvent evt)
        {
            if (!trap || evt.direction == NavigationMoveEvent.Direction.None)
                return;
            var target = evt.target as VisualElement;
            if (target == null || !IsInnermostTrap(target))
                return;

            // 先に既定の移動を止める。移動先が無い方向でも止める（止めないと既定のナビが scope の外へ出る）
            panel.focusController.IgnoreEvent(evt);
            // Focus() の直後は focusedElement が旧値のまま。ここでは新しいフォーカスを前提にしない
            FindNext(target, evt.direction)?.Focus();
        }

        // target を含む trap の scope のうち、最も内側が this か。入れ子のとき外側が先に動かさないため
        bool IsInnermostTrap(VisualElement target)
        {
            for (var e = target; e != null; e = e.parent)
            {
                if (e is FocusScope scope && scope.trap)
                    return scope == this;
            }
            return false;
        }

        // 折り返さない。移動先が無ければ null
        VisualElement FindNext(VisualElement target, NavigationMoveEvent.Direction direction)
        {
            var candidates = CollectCandidates();
            // target が候補の内側（TextField の入力部分など）のとき、その候補を現在地とみなす
            var current = target;
            while (current != null && current != this && !candidates.Contains(current))
                current = current.parent;
            var index = current == null || current == this ? -1 : candidates.IndexOf(current);

            if (direction == NavigationMoveEvent.Direction.Next || direction == NavigationMoveEvent.Direction.Previous)
            {
                if (index < 0)
                    return null;
                var next = index + (direction == NavigationMoveEvent.Direction.Next ? 1 : -1);
                return next >= 0 && next < candidates.Count ? candidates[next] : null;
            }

            return FindInDirection(index >= 0 ? current : target, candidates, direction);
        }

        // 進行方向に worldBound が重なる候補のうち、中心が最も近いもの。既定ナビの近似
        static VisualElement FindInDirection(VisualElement from, List<VisualElement> candidates, NavigationMoveEvent.Direction direction)
        {
            var fromBound = from.worldBound;
            var fromCenter = fromBound.center;
            var vertical = direction == NavigationMoveEvent.Direction.Up || direction == NavigationMoveEvent.Direction.Down;
            VisualElement best = null;
            var bestDistance = float.MaxValue;
            foreach (var candidate in candidates)
            {
                if (candidate == from)
                    continue;
                var bound = candidate.worldBound;
                var center = bound.center;
                var ahead = direction switch
                {
                    NavigationMoveEvent.Direction.Up => fromCenter.y - center.y,
                    NavigationMoveEvent.Direction.Down => center.y - fromCenter.y,
                    NavigationMoveEvent.Direction.Left => fromCenter.x - center.x,
                    _ => center.x - fromCenter.x,
                };
                var overlaps = vertical
                    ? fromBound.xMin < bound.xMax && bound.xMin < fromBound.xMax
                    : fromBound.yMin < bound.yMax && bound.yMin < fromBound.yMax;
                if (ahead <= DirectionEpsilon || !overlaps)
                    continue;
                var distance = (center - fromCenter).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        // 子孫のうち Focus() できる要素を DFS 順に集める。FocusController の ring は使わない（internal）
        List<VisualElement> CollectCandidates()
        {
            var result = new List<VisualElement>();
            if (resolvedStyle.display != DisplayStyle.None)
                Collect(this, result);
            return result;
        }

        static void Collect(VisualElement parent, List<VisualElement> result)
        {
            foreach (var child in parent.Children())
            {
                // 隠れた要素の子孫も対象外
                if (child.resolvedStyle.display == DisplayStyle.None)
                    continue;
                if (child.canGrabFocus && child.tabIndex >= 0 && child.enabledInHierarchy)
                    result.Add(child);
                // delegatesFocus の要素（TextField など）は Focus() を内側へ委ねるので、全体で 1 つの候補にする。
                // 内側も候補にすると、Next / Previous が内側の要素への Focus()（=現在地のまま）で止まる
                if (!child.delegatesFocus)
                    Collect(child, result);
            }
        }
    }
}
