# SettingsScreen

「見出し + 説明文 + 右の操作」の行を縦に並べた設定画面の block。プリミティブ（Switch / Cycle / ToggleGroup / Button / Dialog）の合成だけで、C# は持たない。
行の項目名・並び順・操作の種類は雛形なので、コピーした後に自分のファイルで書き換える。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <ui:VisualElement class="hone-settings-screen">
        <ui:Label class="hone-settings-screen__title hone-text" text="Settings" />
        <ui:VisualElement class="hone-settings-screen__row">
            <ui:VisualElement class="hone-settings-screen__text">
                <ui:Label class="hone-settings-screen__heading hone-text" text="Notifications" />
                <ui:Label class="hone-settings-screen__description hone-text" text="Show a message when something new arrives." />
            </ui:VisualElement>
            <hone:Switch name="notifications" class="hone-settings-screen__control" value="true" />
        </ui:VisualElement>
        <!-- Cycle / ToggleGroup / Button（Secondary）の行は SettingsScreen.uxml を参照 -->
        <ui:VisualElement class="hone-settings-screen__row">
            <ui:VisualElement class="hone-settings-screen__text">
                <ui:Label class="hone-settings-screen__heading hone-text" text="Delete local data" />
                <ui:Label class="hone-settings-screen__description hone-text" text="Remove the data saved on this device. This cannot be undone." />
            </ui:VisualElement>
            <hone:Button name="delete" class="hone-settings-screen__control" variant="destructive" text="Delete" />
        </ui:VisualElement>
        <hone:Dialog name="confirm-delete">
            <!-- タイトル、本文、Cancel と確定の Button -->
        </hone:Dialog>
    </ui:VisualElement>
</ui:UXML>
```

`SettingsScreen.uxml` は、行の型ごとに 1 行ずつ入れた使用例（Switch / Cycle / ToggleGroup / Secondary の Button / Destructive の Button と確認の Dialog）。

## 配線（Destructive の行）

block は振る舞いを持たない。確認は Dialog に委ね、開閉とフォーカスの復元（開く前にフォーカスしていた Button へ戻る）は Dialog が行う。利用者のコードがするのは、ボタンと Dialog をつなぐことだけ。

```csharp
var dialog = root.Q<Hone.Dialog>("confirm-delete");
root.Q<Hone.Button>("delete").clicked += dialog.Open;
dialog.Q<Hone.Button>("cancel").clicked += dialog.Close;
dialog.Q<Hone.Button>("confirm").clicked += () =>
{
    DeleteLocalData();
    dialog.Close();
};
```

Destructive の Button に `commit-delay` は付けない。確認は Dialog が担うので、押し込みの間は要らない。
Dialog は親の全面を覆う（Dialog の README）ので、`hone-settings-screen` の最後の子に置く。

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-settings-screen` | 画面全体。縦並び |
| `hone-settings-screen__title` | 画面のタイトルの `Label` |
| `hone-settings-screen__row` | 1 行。横並びで、上に区切り線を引く（先頭の行の線はタイトルとの区切りになる） |
| `hone-settings-screen__text` | 行の左の列（見出しと説明）。残りの幅を取って縮む |
| `hone-settings-screen__heading` `hone-settings-screen__description` | 見出しと説明の `Label`。折り返す |
| `hone-settings-screen__control` | 行の右の操作（Switch / Cycle / ToggleGroup / Button）。縮まず、行の半分を超えない。Cycle は行の 40% の幅 |

## レイアウトの規約

- 説明は折り返し、左の列が縮む。操作は縮まない。長い文字列は、操作の中で省略記号になる（Cycle の値、ToggleGroup と Button のラベル）。
- 操作の幅は行の割合で決める。固定幅を書かない（言語によって文字列の幅が変わる）。
- 画面の背景色・固定幅・中央寄せは持たない。World Space では panel 自体が画面の大きさになる。Screen Space で中央に置きたいときは、利用者が親に書く。
- 行の区切りと見出しの見た目はこの block の USS に書いている。見た目だけの部品（Card と Separator）が配布されたら、置き換えてよい。

## headless 層との関係

block は `FocusScope` も `BackStack` も直接使わない。フォーカスの移動は各プリミティブ（Switch / Cycle / ToggleGroup / Button）の既定の動きのままで、確認の Dialog の trap、Cancel で閉じる動作、元のボタンへのフォーカスの復元は Dialog が持つ。

registryDependencies の `Core`、`Button`、`Switch`、`Cycle`、`ToggleGroup`、`Dialog` は、`SettingsScreen.uxml` が使うもの。使わない行を消したら、対応するプリミティブは不要になる。
