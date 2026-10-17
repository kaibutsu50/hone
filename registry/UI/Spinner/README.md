# Spinner

待っていることを示す輪。shadcn の Spinner に当たる。`.cs` を持たない見た目だけの部品で、USS クラスを `VisualElement` に付けて使う。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement style="flex-direction: row; align-items: center;">
        <ui:VisualElement class="hone-spinner" />
        <ui:Label text="Loading..." class="hone-text" style="margin-left: 8px;" />
    </ui:VisualElement>
</ui:UXML>
```

## 見た目

| 項目 | 値 |
|---|---|
| 大きさ（幅と高さ） | `--hone-size-spinner`（16px） |
| 角丸 | `--hone-radius-spinner`（8px。大きさの半分で、円になる） |
| 枠の太さ | `--hone-border-width`（1px） |
| 枠の色 | 上の 1 辺は透明、残りの 3 辺は `--hone-color-foreground` |

上の 1 辺を透明にした円なので、止まっていても切れ目のある輪に見える。
`flex-shrink: 0` なので、文言と並べた行の幅が足りなくても、円は潰れない。
文言との間の余白は持たない。使用例の Label のように、隣の要素に `margin` を付ける。

## 回転

Spinner は回転を持たない。回転は演出（外しても「待っている」ことは輪の見た目で伝わる）なので、利用者が注入する。
例えば、次の Manipulator を自分のコードに置いて、`spinner.AddManipulator(new SpinManipulator())` で付ける。外すと回転が止まり、角度が戻る。

```csharp
using UnityEngine.UIElements;

// 付けた要素を、degreesPerSecond の速さで回し続ける
public class SpinManipulator : Manipulator
{
    readonly float m_DegreesPerSecond;
    IVisualElementScheduledItem m_Item;
    float m_Angle;

    // 既定は 1 秒で 1 回転（shadcn の animate-spin と同じ）
    public SpinManipulator(float degreesPerSecond = 360f)
    {
        m_DegreesPerSecond = degreesPerSecond;
    }

    protected override void RegisterCallbacksOnTarget()
    {
        m_Item = target.schedule.Execute(timer =>
        {
            m_Angle = (m_Angle + m_DegreesPerSecond * timer.deltaTime / 1000f) % 360f;
            target.style.rotate = new Rotate(m_Angle);
        }).Every(16);
    }

    protected override void UnregisterCallbacksFromTarget()
    {
        m_Item.Pause();
        m_Item = null;
        m_Angle = 0f;
        target.style.rotate = StyleKeyword.Null;
    }
}
```

回転の中心は要素の中央（`transform-origin` の既定）。`Update` や `Time` を使わず、要素の schedule で回す。

## variant

持たない。色や大きさを変えるときは、自分の USS で `.hone-spinner` の `border-color` や `width` / `height` を上書きするか、トークンを上書きする（`--hone-size-spinner` を変えたら `--hone-radius-spinner` もその半分に揃える）。
Button の中に置くときは、Button の文字色に合わせて `border-color` を上書きする（既定の `--hone-color-foreground` は、Default の Button の背景 `--hone-color-primary` の上では見えにくい）。

## headless 層との関係

振る舞いは持たない。Core は `.hone-text` などの USS だけを使う（Spinner 自身は文字を持たないので、`hone-text` は使用例の Label のため）。
