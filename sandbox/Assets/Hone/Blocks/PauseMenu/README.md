# PauseMenu

「タイトル + 縦に並んだ Button」のポーズメニューと、「終了しますか」の確認の block。プリミティブ（Dialog / Button）の合成だけで、C# は持たない。
項目の名前・数・並び順は雛形なので、コピーした後に自分のファイルで書き換える。

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:hone="Hone">
    <ui:VisualElement class="hone-pause-menu">
        <hone:Dialog name="pause">
            <ui:Label class="hone-dialog__title hone-text" text="Paused" />
            <ui:VisualElement class="hone-pause-menu__items">
                <hone:Button name="resume" text="Resume" />
                <hone:Button name="settings" variant="outline" text="Settings" />
                <hone:Button name="quit" variant="outline" text="Quit" />
            </ui:VisualElement>
        </hone:Dialog>
        <hone:Dialog name="confirm-quit">
            <!-- タイトル、本文、Cancel（name="cancel"）と確定（name="confirm"）の Button -->
        </hone:Dialog>
    </ui:VisualElement>
</ui:UXML>
```

`PauseMenu.uxml` は、確認の Dialog まで含めた使用例。2 つの Dialog は block の root の子で、兄弟になる。

## 配線

block は振る舞いを持たない。開閉・フォーカスの移動と復元・Cancel で一段戻る動作は Dialog が行う。利用者のコードがするのは、ポーズの入力と Button と Dialog をつなぐことだけ。
ポーズの入力（Start ボタンなど）を block は読まない。`pause.Open()` を呼ぶのは利用者のコード。

```csharp
var pause = root.Q<Hone.Dialog>("pause");
var confirm = root.Q<Hone.Dialog>("confirm-quit");

// ポーズの入力を処理するところから
pause.Open();

root.Q<Hone.Button>("resume").clicked += pause.Close;
root.Q<Hone.Button>("settings").clicked += OpenSettings;
root.Q<Hone.Button>("quit").clicked += confirm.Open;
confirm.Q<Hone.Button>("cancel").clicked += confirm.Close;
confirm.Q<Hone.Button>("confirm").clicked += () =>
{
    QuitGame();
    confirm.Close();
};
```

- `pause.Open()` で `resume`（最初の Button）にフォーカスが当たる。Cancel（B / Esc）で閉じる（Dialog の `dismissOnCancel` の既定）。
- `quit` を押すと確認が `pause` の上に重なり、フォーカスは確認の `cancel`（最初の Button）に移る。`pause` は開いたまま。
  Cancel でも `cancel` でも閉じるのは確認だけで、フォーカスは `quit` に戻る（`BackStack` は最後に開いたものだけを閉じる）。もう一度 Cancel を送ると `pause` が閉じる。
- 確認が開いている間、方向入力と Tab は確認の中に留まり、`pause` 側の Button へは移らない。
- ゲームの一時停止（`Time.timeScale` など）は block の外。`pause.Open()` と `pause.Close()` の前後で利用者が行う。`opened` と `closed` が使える（Dialog の README）。
- `settings` の行き先（SettingsScreen を開くなど）と終了の処理は利用者のコード。`OpenSettings` と `QuitGame` はその置き場所を示す名前で、block は何も定義しない。

Destructive の Button に `commit-delay` は付けない。確認は Dialog が担うので、押し込みの間は要らない（SettingsScreen と同じ）。

## 2 つの Dialog の置き方

確認の Dialog は `pause` より後ろの兄弟にする。UI Toolkit は hierarchy の順で描画とポインタの判定をするので、後ろの兄弟が `pause` の上に描かれ、入力も受ける。
前に置くと、確認の overlay が `pause` の下に隠れる。

2 つの Dialog が兄弟でも、確認を開いている間に `pause` 側の `FocusScope` がフォーカスを奪い返すことはない。`FocusScope` の trap が止めるのは、自分の中の要素が target の `NavigationMoveEvent` だけで、確認の中の要素が target のときは動かない（Unity 6000.7.0b4 の PlayMode で、確認を開いて 5 frame 進めても、確認の Button から全方向と Tab を送っても、フォーカスは確認の中に留まった）。
確認を `pause` の中に入れる必要はない。

Dialog は親の全面を覆う（`position: absolute`）ので、block の root に大きさが要る。`hone-pause-menu` は親の残りを埋める（`flex-grow: 1`）。
- Screen Space で panel 全体を覆うには、`hone-pause-menu` を panel の root の直下の最後の子にする。
- World Space では panel 自体がポーズメニューの大きさになるので、`hone-pause-menu` がそれを埋める。World Space 用の設定や分岐は無い（Dialog の README の「World Space での置き方」）。

## USS クラス

| クラス | 付く要素 |
|---|---|
| `hone-pause-menu` | block の root。親の残りを埋める |
| `hone-pause-menu__items` | `pause` の中の Button を縦に並べる要素。Button は幅いっぱいに広がり、間に余白が入る |

タイトル・本文・確認のボタンの行は Dialog の `hone-dialog__title` `hone-dialog__description` `hone-dialog__footer` がそのまま当たる。

## レイアウトの規約

- Button の幅は固定しない。`hone-pause-menu__items` の幅いっぱいに広がり、`pause` の content の幅（panel の 90% と `--hone-size-dialog` の小さい方）で決まる。
- 項目の数を固定しない。Button を足し引きしてよい。1 つ目の Button に初期フォーカスが当たる。
- 背景色・固定幅・中央寄せは block に持たせない。content が中央に置かれるのは Dialog の見た目。

## headless 層との関係

block は `FocusScope` も `BackStack` も直接使わない。フォーカスの閉じ込め、開いた時の初期フォーカス、閉じた時の元の要素への復元、Cancel で一段戻る動作は、2 つの Dialog が持つ。

registryDependencies の `Core`、`Button`、`Dialog` は、`PauseMenu.uxml` が使うもの。
