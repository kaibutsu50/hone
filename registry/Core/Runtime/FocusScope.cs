using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Hone.Core
{
    // 子孫へのフォーカス移動の限定（trap）と、初期フォーカス・復元。Dialog の open / close から Activate / Deactivate を呼ぶ。
    // autoFocus が true なら、panel に attach された時にも Activate が呼ばれる。Deactivate は自動では呼ばれないので、復元は利用側が呼ぶ。
    // 最初から表示されていない場所（閉じた Dialog の中など）や、グルーピングだけで置くときは autoFocus を false にする。
    // true のままだと attach のたびに最初の子孫へフォーカスを移す（canGrabFocus は祖先の display を見ないので、隠れていても移る）。
    // UXML では C# の namespace を宣言して書く: xmlns:core="Hone.Core" のうえで <core:FocusScope trap="true" auto-focus="false">
    //
    // trap の方式: scope のルートで NavigationMoveEvent を TrickleDown で受け、IgnoreEvent で既定の移動を止め、同じ handler の中で次の要素に Focus() する。
    // Next / Previous（Tab）も同じ NavigationMoveEvent で届く。
    // フォーカスが無いとき、D-pad の NavigationMoveEvent の target は panel の最上位で、この handler は呼ばれない（6000.7.0b2 で実測）。
    // 最初の入力が scope の外へ着地しないよう、scope を開いた時点で初期フォーカスを当てておく（autoFocus なら Activate()、そうでなければ FocusFirst()）。
    [UxmlElement]
    public partial class FocusScope : VisualElement
    {
        // 2D 移動で「その方向にある」とみなすのに必要な、中心のずれ（worldBound の座標）
        const float DirectionEpsilon = 1f;

        // true のとき、NavigationMoveEvent（方向入力と Tab）による移動を scope の子孫の中に留める。
        // ポインタ操作や Focus() の直接呼び出しによる移動は止めない。scope の外にあるフォーカスを中へ引き込むこともしない
        [UxmlAttribute]
        public bool trap { get; set; }

        // true のとき、panel に attach された時と Activate() 時に最初の focusable な子孫にフォーカスする
        [UxmlAttribute]
        public bool autoFocus { get; set; } = true;

        // Activate() 時点でフォーカスされていた scope 外の要素。フォーカスが無かったときは null
        VisualElement m_Previous;

        public FocusScope()
        {
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
        }

        // 呼び出し時点のフォーカスを記憶し（無ければ null を記憶する）、autoFocus なら初期フォーカスを当てる。
        // 既に scope の中にフォーカスがあるときは記憶を変えない。attach の自動 Activate のあとに Dialog が Activate を呼んでも、
        // scope の中へ移ったあとの要素を「元の要素」にしないため
        public void Activate()
        {
            var focused = panel?.focusController.focusedElement as VisualElement;
            if (focused == null || !Contains(focused))
                m_Previous = focused;
            if (autoFocus)
                FocusFirst();
        }

        // 記憶した要素がまだ panel 上にあり focusable なら Focus() する。無ければ何もせず、フォーカスはその場に残る。
        // 記憶は復元できたかどうかに関係なく消す
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
            // フォーカスの変更は非同期で、Focus() の直後に focusedElement を読んでも旧値のまま。この後で新しいフォーカスを前提にしない
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

        // 折り返さない。移動先が無いとき、および Next / Previous で現在地が候補でない（tabIndex < 0 など）ときは null
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

        // 進行方向と直交する軸で worldBound が重なり、中心が進行方向へずれている候補のうち、中心間の距離が最も近いもの。既定ナビの近似
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

        // 子孫のうち Focus() できる要素を DFS 順に集める。FocusController の ring は使わない（internal）。
        // tabIndex は 0 以上かを見るだけで、tabIndex による並べ替えはしない
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
                if (!child.delegatesFocus)
                {
                    Collect(child, result);
                    continue;
                }
                // delegatesFocus の要素（TextField など）は Focus() を内側へ委ねるので、内部の構造は候補にしない。
                // 内部も候補にすると、Next / Previous が内側の要素への Focus()（=現在地のまま）で止まる。
                // ただし利用者が子を入れる contentContainer を別に持つ要素（Foldout など）は、その子を候補にする
                var content = child.contentContainer;
                if (content != null && content != child && content.resolvedStyle.display != DisplayStyle.None)
                    Collect(content, result);
            }
        }
    }
}
