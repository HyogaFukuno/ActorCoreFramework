# Changelog

このプロジェクトのすべての変更点を記録します。

書式は [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) に、
バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従います。

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
