# かき氷タワーディフェンス — Unityプロトタイプ仕様書 v2

> スコープ：**Unity単体・キーボード入力**で動く簡易版。物理インターフェース（かき氷機・センサー・プロジェクション）は未接続。入力は実機に差し替えられるよう抽象化する。
> v2の主な更新：①ウェーブ毎の制限時間 ②麓の位置関係と遠隔攻撃の明記 ③遠隔攻撃をプロジェクタイル化 ④採用アーキテクチャの確定とC#スケルトン。

---

## 0. 1行コンセプト
雪山の神となり、頂上からかき氷を削って雪崩を起こし、麓の4方向から砦を**遠隔攻撃**する敵を、殲滅レースで退ける。

---

## 1. スコープと実装フェーズ
| Phase | 内容 |
|---|---|
| **Phase 1（最優先）** | Wave1の核（味方なし）。削り→雪崩→殲滅の手触りと「資源(i)＋注意(ii)」の判断を検証 |
| Phase 2 | 味方NPC（深さ制御ジレンマ）、Wave2 |
| Phase 3 | 変装敵・突撃隊、Wave3 |

---

## 2. 遊びの核（確定）
| 軸 | 内容 |
|---|---|
| ジャンル | 殲滅レース型タワーディフェンス |
| 勝利 | 3 Wave を生き延びる |
| 敗北 | 砦HPが0になる |
| スコア | 終了時の砦HP（Phase2以降は −味方巻き込みペナルティ） |
| 圧力 (i) | 氷が有限（1プレイ固定・補充なし・溶けない） |
| 圧力 (ii) | 一度に1方向の谷にしか雪崩を流せない |

---

## 3. マップ構成
山を中央に置いたトップダウン2D。砦＝**中央/山頂**。谷（雪崩の流路）は**放射状4本**（North/West/South/East）。敵は各谷の**麓（外周端）**にマトリックスで位置する。

**攻撃と雪崩は逆向きに交差する：**
```
        敵(麓・外周)
            │ ▲
   遠隔攻撃 │ │ 雪崩
   (外→中) ▼ │ (中→外)
          砦(中央/山頂)

        [North麓に敵]
              ▲
[West麓] ◀ 砦(中央) ▶ [East麓]
              ▼
        [South麓に敵]
```
- 敵の遠隔攻撃＝**麓→山頂（内向き）**に飛ぶ弾（プロジェクタイル）。
- 雪崩＝**山頂→麓（外向き）**に流れ、麓の敵を先頭行から消す。

---

## 4. 入力（キーボード／Strategyパターンで実機差し替え可）
| キー | 動作 | 実機対応 |
|---|---|---|
| `W`/`A`/`S`/`D` | 照準する谷を選択（N/W/S/E、ラッチ式・同時1本のみ） | かき氷機のXY座標 |
| `Space`（長押し） | アクティブな谷を削る（雪崩ストリーム） | ハンドル回転検知 |

```csharp
// 入力 = Strategy パターン。GameManager は IInputSource だけを見る。
public interface IInputSource {
    int  ActiveChannel { get; } // 0:N 1:W 2:S 3:E / -1:未選択
    bool IsShaving { get; }
}

public class KeyboardInputSource : MonoBehaviour, IInputSource {
    public int  ActiveChannel { get; private set; } = -1;
    public bool IsShaving => Input.GetKey(KeyCode.Space);
    void Update() {
        if      (Input.GetKeyDown(KeyCode.W)) ActiveChannel = 0;
        else if (Input.GetKeyDown(KeyCode.A)) ActiveChannel = 1;
        else if (Input.GetKeyDown(KeyCode.S)) ActiveChannel = 2;
        else if (Input.GetKeyDown(KeyCode.D)) ActiveChannel = 3;
    }
}
// 実機接続時: InterfaceInputSource : IInputSource を作り、Awakeでの代入を差し替えるだけ。
```

---

## 5. コアメカニクス
### 5.1 削り → 雪崩ストリーム
`Space`長押し中、アクティブな谷に雪崩ストリームが流れる（連続ストリーム型）。削っている間、**氷ストックを毎秒消費**。0で削れない。

### 5.2 撃破解決（深さ制御モデル）
- ストリーム中、`avalancheDelay` 秒後から判定開始（**Inspector可変・初期値0＝即時**）。
- 以降 `killInterval` 秒ごとに、その谷の**先頭行（最大3体）を消す**（1ヒット1キル）。
- 先頭行が消えると次行が先頭になる＝**削り続けると先頭から奥へ進む**。
- プレイヤーは削りを止める／谷を変えることで「どこまで削るか（深さ）」を制御できる。

### 5.3 氷ストック（旧称：氷ゲージ）
**有限の氷を数値化したもの**。実機では1プレイ分の氷量が物理的に固定（補充なし・溶けない）。プロトでは「削れる秒数」のメーターとして表現する。`Space`長押しで `iceDrainPerSec` ずつ減少、0で削り不可。圧力(i)の正体。

---

## 6. 敵と遠隔攻撃（プロジェクタイル方式）
- 各谷の麓に **3体×N行**のマトリックス。新規湧きは**最奥**に追加。
- 各敵は `EnemyTypeSO.fireInterval` 秒ごとに**弾を生成**し、砦（中央）へ向けて発射。
- 弾は `ProjectileTypeSO.speed` で麓→山頂へ飛び、着弾で `ProjectileTypeSO.damage` を砦に与える。
- 弾は **Object Pool** で使い回す（生成/破棄のGC負荷を回避）。
- 弾の種別生成は **Factory**（`ProjectileTypeSO` を渡す）。将来の貫通弾・拡散弾は SO追加＋Factory分岐で既存コード不変。
- 将来「連続DPS／プロジェクタイル」を切り替えたい場合は、敵の攻撃を Strategy 化して差し替え可能。

> 雪崩は飛来中の弾を防がない（スコープ外）。敵を倒せば**以後の発射**が止まる。

---

## 7. 砦HP ＝ ゲームの時計
砦HPは減り続けるカウントダウンで、減る速さ＝**生存敵数 × 発射頻度 × 弾ダメージ**で決まる。敵を放置するほど弾が増え、時計が速く進む。殲滅すると遅くなる。

着弾ごとに離散的に減算：
```csharp
public class Fort : MonoBehaviour {
    [SerializeField] int maxHp = 1000;
    int _hp;
    public event Action<int,int> OnHpChanged; // (current, max)  ← UIが購読
    public event Action OnDestroyed;           // ← GameManagerが購読し敗北へ
    void Awake() { _hp = maxHp; }
    public void TakeDamage(int dmg) {
        _hp = Mathf.Max(0, _hp - dmg);
        OnHpChanged?.Invoke(_hp, maxHp);
        if (_hp == 0) OnDestroyed?.Invoke();
    }
    public int CurrentHp => _hp; // スコア算出に使用
}
```
例（`maxHp=1000`, `fireInterval=2s`, `damage=5`）：敵5体≒12.5dmg/s→約80秒／敵30体≒75dmg/s→約13秒。

---

## 8. 味方NPC（Phase2）
A4で確定した「味方がいてもペナルティなしで倒せる」モデル：
- 見た目：青（敵は赤）。形は後で。
- **一時出現・自走退場**（`allyLifetime` 秒で消える）。
- マトリックスの1行を占有。雪崩が達すると巻き込み＝**スコア減**（`allyHitPenalty`）。
- 味方の**手前の敵**＝手前で削りを止めればノーペナルティで撃破可。味方の**奥の敵**＝退場を待てば撃破可（一方的に撃たれ続けない）。
- コスト＝待つ／別谷へ移る間、奥の敵と他谷が砦を削る（スループットと注意のジレンマ）。

---

## 9. 変装（Phase3）
- 変装敵は**青（味方の色）**で出るが**微妙な見た目差（tell）**を持つ。
- 見逃すと自走退場せず砦を攻撃し続ける／真の味方を撃つとスコア減。
- tellの具体（色味差・小マーク・動きの癖など）は**TBD**。

---

## 10. 勝敗・スコア
- `Fort.OnDestroyed`（HP0）→ **敗北**。
- 3 Wave 経過 → **勝利**。
- スコア＝終了時の `Fort.CurrentHp`（Phase2以降は −味方巻き込みペナルティ）。

---

## 11. Wave構成（ScriptableObject化）
- **ウェーブごとに制限時間を個別指定**（`WaveConfigSO.duration`）。
- Wave終了時、**残った敵は消える**。Wave間に `waveLull` 秒の小休止。

| Wave | 名称 | 内容 | Phase |
|---|---|---|---|
| 1 | 快感波 | 味方なし。資源/注意の判断 | 1 |
| 2 | 本隊侵攻 | 味方混在（深さ制御ジレンマ） | 2 |
| 3 | 突撃隊＋変装波 | 速い敵＋青に化けた変装敵 | 3 |

---

## 12. アーキテクチャ（確定）
| 領域 | 採用パターン | 備考 |
|---|---|---|
| データ管理 | **ScriptableObject** | WaveConfigSO / EnemyTypeSO / ProjectileTypeSO |
| ゲーム進行 | **State パターン** | 状態クラス＋StateMachine |
| システム通信 | **C# event（初期）→ SOイベントチャネル（移行）** | 下記移行計画 |
| 生成 | **Object Pool + Factory** | 敵・弾の使い回し、種別生成 |
| 依存配線 | **単一 Composition Root** | Singletonは GameManager 1個のみ |
| 入力 | **Strategy（IInputSource）** | 実機差し替え |
| 攻撃方式（将来） | Strategy（任意） | DPS/弾の切替が必要になったら |

---

## 13. ゲーム進行：State マシン
```csharp
public interface IGameState { void Enter(); void Tick(float dt); void Exit(); }

public class GameStateMachine {
    IGameState _current;
    public void Change(IGameState next) { _current?.Exit(); _current = next; _current?.Enter(); }
    public void Tick(float dt) => _current?.Tick(dt);
}
// 状態: TitleState → WaveIntroState → WavePlayingState → WaveLullState → (loop) → ResultState
//   WavePlayingState: 制限時間カウント / Fort.OnDestroyed購読 / 時間切れで残敵消去
```
遷移概要：
```
Title →(Start)→ WaveIntro(n=1) → WavePlaying(n)
   WavePlaying: timer0 →残敵消去→ n<3:WaveLull→WaveIntro(n+1) / n==3:Result(Win)
                fortHp0 → Result(Lose)
Result →(Retry)→ Title
```

---

## 14. 通信設計と段階移行
**現在：C# event** で迅速に構築。購読は `OnDestroy` で必ず解除する。購読箇所は少数に集中させる（UIは1クラスでまとめて購読）＝移行を局所化するため。

**移行先：ScriptableObject イベントチャネル**（シーン階層に依存せず疎結合）。
```csharp
[CreateAssetMenu(menuName="KakigoriTD/Events/IntEventChannel")]
public class IntEventChannelSO : ScriptableObject {
    public event Action<int> OnRaised;
    public void Raise(int value) => OnRaised?.Invoke(value);
}
// 移行時: Fort等は対応するEventChannelSOを参照してRaise、UIは同アセットを購読。
```
**移行の判断トリガー**（いずれか満たしたら移行）：
1. プレハブ（弾・敵）が**シーン参照なしで**イベントを発火したくなった
2. クロスシーン通信が必要になった
3. 相互参照するシステムが概ね5つを超えた

---

## 15. クラス構成
| クラス | 役割 | パターン |
|---|---|---|
| `GameManager`（唯一のSingleton） | 状態遷移駆動・各システムの生成と配線 | Composition Root |
| `GameStateMachine` ＋各State | 進行制御 | State |
| `WaveManager` | WaveConfigSOに沿った時間管理・湧き・Wave終了時の残敵消去 | — |
| `ChannelController`×4 | 1谷のマトリックス保持・雪崩適用・最奥湧き・味方出現(P2) | — |
| `EnemyMatrix` | 行リスト操作（先頭取得/消去/最奥追加） | — |
| `Enemy` | EnemyTypeSO参照・発射タイマー・isAlly/isDisguised | — |
| `Projectile` | 麓→山頂へ移動・着弾でFort.TakeDamage | Pool対象 |
| `ProjectileFactory` | ProjectileTypeSOから弾生成 | Factory |
| `ObjectPool<T>` | 敵・弾の使い回し | Object Pool |
| `AvalancheController` | アクティブ谷＋削り中に delay→interval で先頭行消去 | — |
| `IceStock` | 残り削り秒数管理・0で削り不可 | — |
| `Fort` | HP・着弾被弾・OnHpChanged/OnDestroyed発火 | Observer |
| `ScoreManager` | 残HP −ペナルティ | — |
| `UIManager` | 氷バー/砦HP/Waveタイマー/アクティブ谷ハイライト/リザルト（イベント購読） | Observer |
| `KeyboardInputSource` | IInputSource実装 | Strategy |

---

## 16. ScriptableObject 定義
```csharp
[CreateAssetMenu(menuName="KakigoriTD/WaveConfig")]
public class WaveConfigSO : ScriptableObject {
    public float duration;          // ★ウェーブ毎の制限時間
    public float spawnInterval;     // 谷ごとの湧き間隔
    public EnemyTypeSO[] enemyPool; // このウェーブに出る敵種
    public float allySpawnChance;   // Phase2
    public float disguiseChance;    // Phase3
}

[CreateAssetMenu(menuName="KakigoriTD/EnemyType")]
public class EnemyTypeSO : ScriptableObject {
    public float moveSpeed;            // 突撃隊は速い
    public float fireInterval;
    public ProjectileTypeSO projectile;
    public bool  isAlly;              // 味方NPC
    public bool  isDisguised;         // 変装(P3)
    public Color tint;                // 赤=敵 / 青=味方・変装
}

[CreateAssetMenu(menuName="KakigoriTD/ProjectileType")]
public class ProjectileTypeSO : ScriptableObject {
    public float speed;
    public int   damage;
    // 拡張: bool piercing; int spreadCount; など
}
```

---

## 17. 生成（Object Pool ＋ Factory）
```csharp
public class ObjectPool<T> where T : Component {
    readonly Stack<T> _stock = new();
    readonly Func<T> _create;
    public ObjectPool(Func<T> create, int prewarm = 0) {
        _create = create;
        for (int i = 0; i < prewarm; i++) _stock.Push(_create());
    }
    public T Get()  { var o = _stock.Count > 0 ? _stock.Pop() : _create(); o.gameObject.SetActive(true); return o; }
    public void Release(T o) { o.gameObject.SetActive(false); _stock.Push(o); }
}

public class ProjectileFactory {
    readonly ObjectPool<Projectile> _pool;
    public Projectile Create(ProjectileTypeSO type, Vector3 from, Vector3 to) {
        var p = _pool.Get();
        p.Init(type, from, to, onArrive: () => _pool.Release(p)); // 着弾でプールへ返却
        return p;
    }
}
// 貫通/拡散弾の追加 = ProjectileTypeSO追加 + Factory分岐。既存呼び出し側は不変。
```

---

## 18. Composition Root
```csharp
public class GameManager : MonoBehaviour {       // 唯一のSingleton
    [SerializeField] WaveConfigSO[] waves;        // Wave1..3（各duration持ち）
    [SerializeField] Fort fort;
    [SerializeField] UIManager ui;

    IInputSource _input;
    readonly GameStateMachine _fsm = new();

    void Awake() {
        _input = GetComponent<KeyboardInputSource>(); // 実機時はここを差し替え
        // IceStock / ChannelController×4 / WaveManager / ProjectileFactory / Pool を生成・配線
        // fort.OnHpChanged += ui.SetFortHp;  fort.OnDestroyed += () => _fsm.Change(resultLose);
        _fsm.Change(/* TitleState */);
    }
    void Update() => _fsm.Tick(Time.deltaTime);
}
```

---

## 19. パラメータ・テーブル（初期値＝プレイテストで調整）
| パラメータ | 初期値 | 公開 | 備考 |
|---|---|---|---|
| `channelCount` | 4 | ー | 放射状 N/W/S/E |
| `avalancheDelay` | 0（秒） | ✅ | 即時。物理寄りにするなら上げる |
| `killInterval` | 0.4（秒/行） | ✅ | 1行=最大3体 |
| `rowWidth` | 3 | ✅ | |
| `iceTotal` | 50（削り秒） | ✅ | 全3Wave通算 |
| `iceDrainPerSec` | 1.0 | ✅ | |
| `fortHpMax` | 1000 | ✅ | ゲームの時計 |
| `projectile.damage` | 5 | ✅(SO) | |
| `enemy.fireInterval` | 2.0（秒） | ✅(SO) | |
| `projectile.speed` | 6.0 | ✅(SO) | 麓→山頂 |
| `wave[1..3].duration` | 30 / 40 / 45（秒） | ✅(SO) | ★ウェーブ毎 |
| `wave[1..3].spawnInterval` | 1.5 / 1.0 / 0.6 | ✅(SO) | |
| `waveLull` | 5（秒） | ✅ | |
| `allyLifetime`（P2） | 6（秒） | ✅(SO) | |
| `allyHitPenalty`（P2） | 50 | ✅ | スコア減 |

---

## 20. 実装フェーズ詳細
- **Phase 1**：`GameManager`/`GameStateMachine`+States/`WaveManager`/`ChannelController`×4(敵のみ)/`Enemy`/`Projectile`/`ProjectileFactory`/`ObjectPool`/`AvalancheController`/`IceStock`/`Fort`/`KeyboardInputSource`/`UIManager`。通信はC# event。Wave1の核を検証。
- **Phase 2**：味方NPC（一時出現・自走退場）/スコアペナルティ/Wave2。
- **Phase 3**：変装敵＋tell/突撃隊/Wave3。
- 通信は §14 の移行トリガーに達したら SOイベントチャネルへ。

---

## 21. 残りTBD
1. 味方／敵／変装の**形のデザイン**
2. 変装のtell（見た目差）の具体
3. バランス数値（`iceTotal`, `projectile.damage`, `fireInterval`, `spawnInterval` 等）のプレイテスト調整
4. 弾の見た目・着弾エフェクト、谷の物理ガイド（実機検討時）
