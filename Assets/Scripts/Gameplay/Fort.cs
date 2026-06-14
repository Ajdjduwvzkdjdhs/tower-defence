using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 山頂の砦。敵弾の着弾で HP が減り、0 で陥落＝敗北条件（spec「砦HP＝ゲームの時計」）。
    /// 通信は C# event（ADR-3）。UI など購読側はこのイベントを購読し、<c>OnDestroy</c> で必ず解除すること。
    ///
    /// HP の増減は単純な離散減算のみのため、本 MonoBehaviour 内に保持する（薄いアダプタの範囲、ADR-8）。
    /// 値は <see cref="Initialize(int)"/> で確定する（Play Mode では Awake が Inspector 値で初期化、
    /// GameManager / テストは明示的に呼び出して任意の最大HPを与えられる）。
    /// </summary>
    public class Fort : MonoBehaviour
    {
        // 調整値（ADR-1）。spec 初期値 fortHpMax = 1000。
        [SerializeField] private int _maxHp = 1000;

        private int _currentHp;

        /// <summary>HP が変化したときに発火する（引数: 現在HP, 最大HP）。UI バー更新用。</summary>
        public event Action<int, int> OnHpChanged;

        /// <summary>HP が 0 に達した瞬間に一度だけ発火する（敗北）。</summary>
        public event Action OnDestroyed;

        /// <summary>現在HP。勝利時スコア（spec: 終了時の砦HP）に使う。</summary>
        public int CurrentHp => _currentHp;

        /// <summary>最大HP。</summary>
        public int MaxHp => _maxHp;

        /// <summary>HP 残量の正規化値 0..1（UI バー用）。MaxHp が 0 なら 0。</summary>
        public float Normalized => _maxHp <= 0 ? 0f : (float)_currentHp / _maxHp;

        /// <summary>陥落済みか（これ以上ダメージを受け付けない）。</summary>
        public bool IsDestroyed => _currentHp <= 0;

        // Play Mode では Inspector の _maxHp で初期化する。
        // EditMode テストや GameManager は Initialize(int) を明示的に呼び、任意の最大HPを与える。
        private void Awake() => Initialize(_maxHp);

        /// <summary>
        /// 砦HP を確定（初期化）する。<paramref name="maxHp"/> は 0 未満なら 0 に丸める。
        /// </summary>
        public void Initialize(int maxHp)
        {
            _maxHp = maxHp < 0 ? 0 : maxHp;
            _currentHp = _maxHp;
            // 初期化も HP 変化として通知する（リトライ時に UI バーを満タンへ戻すため）。
            // Awake 経由の初回はまだ購読者が居ないので無害。
            OnHpChanged?.Invoke(_currentHp, _maxHp);
        }

        /// <summary>
        /// 着弾ダメージを適用する。HP は 0 未満にはならない。
        /// HP が変化したら <see cref="OnHpChanged"/> を発火し、0 到達の瞬間に一度だけ <see cref="OnDestroyed"/> を発火する。
        /// 既に陥落済み、または 0 以下のダメージは無視する（二重発火・無効入力の防止）。
        /// </summary>
        public void TakeDamage(int amount)
        {
            if (IsDestroyed || amount <= 0) return;

            _currentHp -= amount;
            if (_currentHp < 0) _currentHp = 0;

            OnHpChanged?.Invoke(_currentHp, _maxHp);

            if (_currentHp == 0) OnDestroyed?.Invoke();
        }
    }
}
