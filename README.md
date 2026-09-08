# ActorCoreFramework

Unreal Engine の Actor / Pawn / Controller の設計を Unity に持ち込むためのゲームプレイフレームワークです。
Actor は MonoBehaviour ではない純粋な C# オブジェクトなので、GameObject の寿命やシリアライズの都合から独立して寿命と実行順を設計できます。

> [!WARNING]
> このプロジェクトはまだ進行中です。

## 特徴

- **GameObject から独立した Actor** — 生成はコンストラクタ、破棄は `World` が管理する
- **明示的なライフサイクル** — `BeginPlay` / `EndPlay` / `Dispose` が決まった順序で配送される
- **PlayerLoop 直結の Tick** — TickGroup・優先度・実行間隔を Actor ごとに指定できる
- **Pawn と Controller の分離** — 「操作される側」と「操作する側」を分け、Possess で結び付ける

## 要件

Unity 6000.5 以降 / C# 9

## 導入

Package Manager の **Add package from git URL** に以下を入力します。

```
https://github.com/HyogaFukuno/ActorCoreFramework.git?path=/Assets/Plugins/ActorCoreFramework
```

## 使い方

### World をループに接続する

`World` が Actor の所有権と Tick を持ちます。`WorldLoop` がそれを Unity の PlayerLoop へ接続します。

```csharp
public sealed class Startup : MonoBehaviour
{
    World world;
    IDisposable loop;

    void Awake()
    {
        world = new World();

        var pawn = world.Spawn(() => new PlayerCharacter(transform, rigidbody, collider));
        world.Spawn(() => new PlayerController(pawn, moveAction));

        loop = WorldLoop.Register(world); // 戻り値を破棄すると登録解除される
    }

    void OnDestroy()
    {
        loop?.Dispose();
        world?.Dispose();
    }
}
```

### Actor

```csharp
public sealed class Turret : Actor
{
    public Turret()
    {
        PrimaryActorTick.CanEverTick = true;
        PrimaryActorTick.Interval = 0.5f; // 0.5秒ごとにTick
    }

    protected override void OnBeginPlay() { }                     // 資源の確保はここで
    protected override void OnTick(float deltaTime) { }
    protected override void OnEndPlay(EndPlayReason reason) { }   // 解放はここで
}
```

コンストラクタは組み立てだけに留め、**後始末が必要になる操作は `OnBeginPlay` で行い、`OnEndPlay` で戻してください。**
`InputAction` の `Enable()`、イベントの購読、プールからの借用、他オブジェクトへの自己登録などが該当します。
参照を引数で受け取って保持するだけなら、対になる解放が不要なのでコンストラクタで構いません。

```csharp
public PlayerController(Character pawn, InputAction moveAction) : base(pawn)
{
    this.moveAction = moveAction; // 参照を持つだけ。後始末は要らない
}

protected override void OnBeginPlay() => moveAction.Enable();
protected override void OnEndPlay(EndPlayReason reason) => moveAction.Disable();
```

`World` に登録されなかった Actor には `BeginPlay` / `EndPlay` が一度も配送されません。
コンストラクタで `Enable()` してしまうと、登録をやめた時点で `Disable()` される機会が失われます。

破棄は `World.Destroy(actor)` で予約し、実際の破棄はそのフレームの `PostTick` 末尾で行われます。
予約された時点で `Actor.IsPendingDestroy` が立ち、以降 Tick は配送されません。

### ActorComponent

機能単位を Actor に合成します。Unity の Component とは無関係で、GameObject の寿命に縛られません。

```csharp
public sealed class HealthComponent : ActorComponent
{
    protected override void OnBeginPlay() { }
    protected override void OnTick(float deltaTime) { }
}

// Actor 側
health = AddComponent(new HealthComponent());
RemoveComponent(health);   // EndPlay と Dispose が配送される
```

Tick は所有者の Actor から配送されるため、TickGroup と Interval は所有者の `PrimaryActorTick` に従います。
Component 単位で止めたい場合は `Enabled` を使います。

### 検索

Actor と Component は型で引けます。いずれも呼び出し側のリストへ詰める形なので、
リストを使い回せば毎フレーム呼んでもアロケーションが発生しません。
取り外しや破棄が予約されたものは、実際に消える前でも検索対象から外れます。

```csharp
readonly List<WeaponComponent> weapons = new();
readonly List<Enemy> enemies = new();

actor.TryGetComponent<HealthComponent>(out var health); // 単一。最初の1件
actor.GetComponents(weapons);                           // 同じ型を複数持つ場合
world.GetActors(enemies);                               // UE の GetAllActorsOfClass 相当
```

### Pawn と Controller

`Pawn` は操作される対象、`Controller` は操作する主体です。Controller は移動ロジックを知らず、意図だけを渡します。

```csharp
public sealed class PlayerController : Controller<Character>
{
    public PlayerController(Character pawn, InputAction move) : base(pawn)
    {
        PrimaryActorTick.CanEverTick = true;
        PrimaryActorTick.Group = TickGroup.FixedTick;
        PrimaryActorTick.Priority = -100; // 操作対象より先に入力を積む
    }

    protected override void OnTick(float deltaTime)
    {
        if (TryGetControlledPawn(out var pawn))
        {
            pawn.AddMovementInput(move.ReadValue<Vector2>());
        }
    }
}

controller.SwitchControlledPawn(otherPawn); // 差し替え。前の Pawn は自動で解除される
```

他の Controller が操作中の Pawn を指定すると、先にそちらが解除されます。
Pawn または Controller が破棄されたときも、参照は自動的に切られます。

### 型の対応

| 用途 | 3D | 2D |
|---|---|---|
| 操作対象の基底 | `Pawn` | `Pawn2D` |
| 物理付きの Pawn | `Character` (`Rigidbody` / `Collider`) | `Character2D` (`Rigidbody2D` / `Collider2D`) |

## Tick

`PrimaryActorTick` で挙動を指定します。

| プロパティ | 変更可能な時期 | 説明 |
|---|---|---|
| `CanEverTick` | コンストラクタのみ | Tick を使うか |
| `Group` | コンストラクタのみ | 所属する TickGroup |
| `Priority` | コンストラクタのみ | 同一グループ内の実行順。小さいほど先。同値なら登録順 |
| `Enabled` | いつでも | 実行時の一時停止 |
| `Interval` | いつでも | 0 なら毎フレーム。0.1 なら 0.1 秒ごと |

`CanEverTick` / `Group` / `Priority` は World への登録時に確定します。
登録後に変更すると `InvalidOperationException` になるので、コンストラクタで設定してください。
実行時に Tick を止めたい場合は `Enabled` を使います。

TickGroup と、1 フレーム内での実行順は次のとおりです。

| TickGroup | 実行タイミング | deltaTime |
|---|---|---|
| `FixedTick` | `FixedUpdate` の後（物理ステップごと） | `Time.fixedDeltaTime` |
| `Tick` | `Update` の後 | `Time.deltaTime` |
| `UnscaledTick` | `Tick` の直後 | `Time.unscaledDeltaTime` |
| `PostTick` | `LateUpdate` の後。末尾で破棄予約が処理される | `Time.deltaTime` |

## ライフサイクル

| タイミング | 配送順 |
|---|---|
| `World.Register` | Component の `OnBeginPlay` → Actor の `OnBeginPlay` |
| Tick | Component の `OnTick` → Actor の `OnTick` |
| 破棄 | Actor の `OnEndPlay` → Component の `OnEndPlay`（合成と逆順） |
| 破棄 | Component の `OnDispose`（合成と逆順）→ Actor の `OnDispose` |

`EndPlayReason` は `Destroyed`（`World.Destroy` による破棄）と `WorldShutdown`（`World.Dispose` による破棄）を区別します。

## テスト

- EditMode: `Assets/Plugins/ActorCoreFramework/Tests/Editor`
- PlayMode: `Assets/Plugins/ActorCoreFramework/Tests/Runtime`（PlayerLoop への差し込みと実行順）

Unity の Test Runner から実行できます。

## ライセンス

MIT
