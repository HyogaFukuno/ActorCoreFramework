# Changelog

このプロジェクトのすべての変更点を記録します。

書式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。

## [2.4.0] - 2026-09-26

### 追加

- World Debugger(`Window > Actor Core Framework > World Debugger`)を追加しました。
  生きている World と、Spawn されている Actor / Component の状態、Tick の設定、
  Pawn と Controller の関係、利用者が宣言したフィールドを UI Toolkit のウィンドウで一覧できます。
  `CanEverTick` の設定漏れ、`WorldLoop` への登録漏れ、`World.Dispose` の呼び忘れ、
  GameObject が破棄されたまま残っている Pawn など、例外にならず黙って何も起きない誤りを警告として表示します。
- `World.Id` / `World.Name` / `World.IsDisposed` を追加しました。

### 修正

- 破棄が予約された Pawn を、Controller の `ControlledPawn` / `TryGetControlledPawn` が
  実際の破棄まで返し続けていた問題を修正しました。`BindTo(destroyCancellationToken)` で
  紐づけた Pawn では、GameObject の破棄後も次のフレームの Tick で Controller から
  壊れた Transform に触れて `MissingReferenceException` になっていました。
  破棄が予約された Pawn は検索系と同じく見えなくなります。`OnUnpossessed` はこれまでどおり
  実際の破棄に伴って届きます。
- `OnBeginPlay` が例外を投げたときの巻き戻しで `OnEndPlay` も例外を投げると、
  本来の原因である `OnBeginPlay` の例外が失われていた問題を修正しました。
  呼び出し元へは `OnBeginPlay` の例外を投げ直し、巻き戻しの例外は `Debug.LogException` へ出します。
- Component の `OnBeginPlay` の中で Component が取り外されると、後ろの Component が
  BeginPlay を受け取らないまま Tick だけ配送されていた問題を修正しました。
  取り外しは BeginPlay の配送を終えてから反映し、BeginPlay 前に取り外された Component には配送しません。

## [2.3.0] - 2026-09-23

### 修正

- `BindTo` に渡したトークンが別スレッドでキャンセルされると(`CancelAfter` など)、
  そのスレッドから `World.Destroy` が走り、World の内部状態が壊れる問題を修正しました。
  所有スレッド以外からの破棄要求はキューへ積み、次に World の Tick が回った冒頭で受け付けます。
- Component の `OnEndPlay` から他の Component を 2 つ以上取り外すと
  `ArgumentOutOfRangeException` になる問題を修正しました。
  EndPlay の配送中の取り外しは、配送を終えてから反映します。
- 同じフレームで起きた 2 件目以降の例外が、ログにも出ずに失われていた問題を修正しました。
  最初の 1 件はこれまでどおり呼び出し元へ投げ直し、以降は `Debug.LogException` へ出します。
- Pawn 自身の破棄に伴う `OnUnpossessed` が、`OnEndPlay` の後(Component の後片付け後)に
  届いていた問題を修正しました。UE と同じく `OnEndPlay` より前に届きます。
- 別の World に登録された Pawn を Possess できてしまう問題を修正しました。
  World 未登録のうちに Possess した Pawn が別の World へ登録された場合も解除されます。
- `PrimaryActorTick.Enabled` を戻した直後に、`Interval` を待たずに Tick されていた問題を修正しました。
- 破棄済みの Actor への `RemoveComponent` が `true` を返していた問題を修正しました。

### 変更

- 大量の Spawn / Destroy を行うフレームの計算量を O(n²) から改善しました。
  Tick グループへの挿入は二分探索に、破棄したActorの除去はフレーム末尾の一括除去にしています。
  これに伴い、`World.Actors` は PostTick 末尾の破棄処理が終わるまで、破棄中の Actor を含みます。
  生きている Actor だけが必要な場合は `GetActors` を使ってください。

### ドキュメント

- `Character.Position` / `Character2D.Position2D` は物理ステップ上の座標であり、
  Rigidbody の補間が効かないことを明記しました。描画の基準には `Transform.position` を使ってください。
- `WorldLoop` は World の寿命を持たず、Play mode 終了時にも `World.Dispose` しないことを明記しました。
- サンプルの `PlayerController` が、入力空間の `Vector2` をそのまま
  `AddMovementInput` へ渡していたのを、ワールド空間の方向へ変換してから渡すように直しました。

## [2.2.0] - 2026-09-10

### 追加

- `Actor.BindTo(CancellationToken)` を追加しました。Actor の寿命を任意の
  `CancellationToken` の寿命に紐づけ、キャンセルされた時点で `World.Destroy` を予約します。

  Actor は GameObject の寿命から独立しているため、GameObject を壊しても World は
  自動では気付かず、壊れた `Transform` を持つ Pawn が Tick され続けていました。
  MonoBehaviour の `destroyCancellationToken` を渡すことで、その 2 つを結び直せます。
  破棄が予約された時点で Tick の配送が止まるので、壊れた GameObject を掴んだまま
  Tick されることはありません。

  紐づけ先は MonoBehaviour に限りません。他の Actor の `DestroyToken` を渡せば
  「親が死んだら子も畳む」も同じ形で書けます。

  購読は `BeginPlay` で始まり `EndPlay` で解除されるため、Actor が先に死んだ場合に
  紐づけ先が Actor を掴み続けることはありません。
  戻り値は Actor 自身なので、`world.Spawn(...).BindTo(token)` と繋げられます。

## [2.1.0] - 2026-09-09

### 追加

- `Actor.DestroyToken` を追加しました。Actor の寿命に紐づく `CancellationToken` で、
  `EndPlay` の入口、派生クラスの `OnEndPlay` より前に発火します。
  非同期処理にこれを渡すことで、Actor が破棄された後も継続が走り、
  `World` の管理外から破棄済みの Actor を触る事故を防げます。

  キャンセルは協調的なので、`EndPlay` の完了と非同期処理の停止は同期しません。
  継続の再開後は `IsPlaying` などで生存を確認してください。
  また `OnEndPlay` は同期的に完結する契約のため、`await` はできません。

  トークンは使われたときだけ確保するので、非同期を使わない Actor に負担はありません。
  `EndPlay` 後に取得した場合は、最初からキャンセル済みのトークンを返します。
  非同期ライブラリへの依存は追加していません。

## [2.0.0] - 2026-09-09

### 破壊的変更

- `Pawn.OnPossessed` / `OnUnpossessed` を `public` から `protected` へ変更しました。
  `public` のままだと、Controller は Pawn を掴んだままなのに Pawn 側だけが
  「解除された」と信じる状態を外部から作れてしまうためです。
  外部で `public override` している派生 Pawn は `protected override` へ修正してください。

- `CanEverTick` / `Group` / `Priority` の施錠が `BeginPlay` より前に移動しました。
  これまで `OnBeginPlay` の中での変更は通っていましたが、
  `InvalidOperationException` を投げるようになります。コンストラクタで設定してください。
  この変更により、`OnBeginPlay` の中で登録された Actor に親が追い越されなくなり、
  Priority が同値のときの「登録順を保つ」という保証が守られます。

- サンプルをパッケージ本体から分離し、リポジトリ内の
  `Assets/Plugins/ActorCoreFramework.Samples` へ移動しました。
  パッケージ経由でサンプルのスクリプトを参照していた場合は参照が切れます。
  Samples の asmdef が `Unity.InputSystem` を無条件に参照していたため、
  Input System を導入していないプロジェクトで利用者側がコンパイルエラーになっていました。

### 修正

- 破棄済みの Pawn を Possess できてしまう問題を修正しました。
  Pawn は自身の `EndPlay` で Controller の参照を切るため、`EndPlay` 後に Possess すると
  切断の機会が二度と来ず、破棄済み Pawn への参照が Controller に残り続けていました。
  `Ended` / `Disposed` / 破棄予約済みの Pawn は `null` を渡したものとして扱います。
  World 未登録（`Created`）の Pawn は、後から登録すれば `BeginPlay` が届くため許可します。

- `OnEndPlay` が例外を投げると、巻き戻しが途中で打ち切られる問題を修正しました。
  Component への `EndPlay` 配送とフレームワーク内部の後始末が飛んでいました。
  特に `World.Dispose` は、打ち切られると二度目の呼び出しも即座に return するため、
  残りの Actor が永久に `EndPlay` も `Dispose` も受け取れない状態になっていました。

- Tick 中に 1 体の Actor が例外を投げると、同じ TickGroup の後続がすべて
  Tick されなくなる問題を修正しました。Priority が高い 1 体の不具合で
  グループ全体が巻き添えになっていました。

- `PostTick` が例外で抜けた際に、末尾の破棄予約が処理されない問題を修正しました。
  `Destroy` されたはずの Actor が次フレームまで生き残っていました。

上記いずれも例外を握り潰しません。配送をすべて終えてから、
その回で最初に発生した例外を呼び出し元へ投げ直します。

### 追加

- `World.Dispose` を Tick の最中に呼ぶと `InvalidOperationException` を投げるようになりました。
  反復中の Tick リストを破棄することになり、そのフレームの残りが黙って落ちるためです。

- `package.json` に `description` / `documentationUrl` / `keywords` を追加しました。

### その他

- `Actor.RemoveComponent` と `World.Destroy` の引数を nullable にしました。
  `null` を受け取って `false` / no-op を返す以上、シグネチャがそう宣言すべきためです。

- `TickGroup` の配列を列挙子の最大値 + 1 で確保するようにしました。
  列挙子に明示的な値を振っても添字が範囲外になりません。

- EditMode テストを 11 件追加しました（70 → 81 件）。

## [1.0.1]

初期リリース。
