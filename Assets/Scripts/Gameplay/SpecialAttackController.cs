using System;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 必殺技「ビーム砲」の演出進行役（ADR-30）。発動条件は GameManager 側の <c>SpecialGauge</c>（ゲージ MAX で
    /// 自動発動）が決め、本クラスは <see cref="Fire"/> で呼ばれてから一連の演出を進める:
    /// 山頂にビーム砲モデルが出現 → <see cref="SpecialAttackShockwave"/> の子テキスト(SpecialAttackText)
    /// を 0 からプレハブ設定値まで拡大して保持（**カットイン**） → テキストを消去 →
    /// 紫の光線(<see cref="BeamSweepVfx"/>)が敵の並ぶ輪を一周 → 少し遅れて
    /// 衝撃波(<see cref="ExplosionTelegraph"/> の色違いプレハブ)が走り、**その瞬間に盤面の敵を全員**
    /// <see cref="EnemyField.KillAllForBeam"/> で撃破 → 衝撃波が消えたら砲も消える。
    /// 展示にはプレイヤーが押せるボタンが無いため照準入力を持たず、全方位に均一に効かせる。
    /// 演出中（<see cref="IsPlaying"/>）は GameManager が次の発動を待たせる。
    ///
    /// ※ カットイン用の秒数（拡大時間、保持時間）は <see cref="SpecialAttackConfigSO"/> に集約（ADR-1）。
    ///   実際の値を編集するのは `Assets/ScriptableObjects/SpecialAttackConfig.asset`（TH_Stage プレハブの
    ///   GameManager から渡される SO）。Prefab / シーン内オブジェクトではなくこの ScriptableObject から設定する。
    /// </summary>
    public class SpecialAttackController : MonoBehaviour
    {
        private enum Phase { Waiting, TextIn, TextHold, TextOut, Beaming, WaitingShockwave, Firing }

        private EnemyField _field;
        private Transform _fortTransform;
        private SpecialAttackConfigSO _config;
        private GameObject _shockwavePrefab;
        private GameObject _cannonInstance;
        private Transform _muzzleTransform; // ビームの発射元（ビーム砲モデルの砲口）。見つからなければ本体位置で代用
        private Vector3 _cannonForwardWorld; // 配置直後（未回転）の砲口方向（XZ, 正規化）。銃身の向き合わせ用
        private Quaternion _cannonInitialRotation;
        private BeamSweepVfx _activeBeam;
        private GameObject _shockwaveInstance;
        private Transform _cutinText; // SpecialAttackShockwave の子（SpecialAttackText）
        private Vector3 _cutinTargetScale; // プレハブに焼き込まれた最終スケール（拡大の目標）

        private Phase _phase = Phase.Waiting;
        private float _phaseT;

        /// <summary>衝撃波で撃破した瞬間に発火（引数＝一掃した体数）。音・ログ用。</summary>
        public event Action<int> OnFired;

        /// <summary>発動〜演出終了までの間 true（GameManager が次の発動を待たせる／UI が他の表示を抑える）。</summary>
        public bool IsPlaying => _phase != Phase.Waiting;

        /// <summary>
        /// GameManager（Composition Root）から一度だけ呼ぶ。field/fortTransform/config は必須。
        /// cannonModelPrefab/shockwavePrefab は未割り当てでも動く（見た目無しでロジックだけ動作）。
        /// </summary>
        public void Configure(EnemyField field, Transform fortTransform, SpecialAttackConfigSO config,
            GameObject cannonModelPrefab, GameObject shockwavePrefab)
        {
            _field = field;
            _fortTransform = fortTransform;
            _config = config;
            _shockwavePrefab = shockwavePrefab;

            if (cannonModelPrefab != null)
            {
                _cannonInstance = Instantiate(cannonModelPrefab, transform);
                _cannonInstance.transform.position = _fortTransform.position + _config.CannonOffset;
                _cannonInstance.SetActive(false);
                _muzzleTransform = FindMuzzle(_cannonInstance.transform);

                _cannonInitialRotation = _cannonInstance.transform.rotation;
                Vector3 fwd = _muzzleTransform.position - _cannonInstance.transform.position;
                fwd.y = 0f;
                _cannonForwardWorld = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            }
        }

        // ビーム砲モデル内の砲口（"Muzzle" を含む名前の子）を探す。見つからなければ本体位置で代用する。
        private static Transform FindMuzzle(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("Muzzle")) return t;
            }
            return root;
        }

        /// <summary>
        /// ビーム砲を発動する（ゲージ MAX で GameManager が呼ぶ）。演出中の再入は無視する。
        /// 撃破は光線一周＋遅延の後、衝撃波の瞬間に起きる（発動と同時ではない）。
        /// カットイン演出（テキスト拡大→保持→消去）が先頭に来る（ADR-30 延長）。
        /// </summary>
        public void Fire()
        {
            if (_phase != Phase.Waiting) return;

            _phaseT = 0f;
            if (_cannonInstance != null) _cannonInstance.SetActive(true);

            // 衝撃波プレハブ（SpecialAttackShockwave）は先頭で Instantiate し、
            // ExplosionTelegraph を抑制して「テキストだけが浮いている状態」を作る。
            if (_shockwavePrefab != null)
            {
                Vector3 center = _fortTransform.position;
                _shockwaveInstance = Instantiate(_shockwavePrefab, center, Quaternion.identity);
                var telegraph = _shockwaveInstance.GetComponent<ExplosionTelegraph>();
                if (telegraph != null) telegraph.SetSuppressed(true);

                _cutinText = FindCutinText(_shockwaveInstance.transform);
                if (_cutinText != null)
                {
                    _cutinTargetScale = _cutinText.localScale; // プレハブ値を目標として控えて…
                    _cutinText.localScale = Vector3.zero;       // …0 から拡大する
                }
            }

            _phase = Phase.TextIn;
        }

        private void Update()
        {
            switch (_phase)
            {
                case Phase.TextIn:
                {
                    _phaseT += Time.deltaTime;
                    float dur = _config.TextScaleInDuration;
                    float k = dur > 0f ? Mathf.Clamp01(_phaseT / dur) : 1f;
                    if (_cutinText != null)
                        _cutinText.localScale = Vector3.Lerp(Vector3.zero, _cutinTargetScale, k);
                    if (k >= 1f) EnterTextHold();
                    break;
                }

                case Phase.TextHold:
                {
                    _phaseT += Time.deltaTime;
                    if (_phaseT >= _config.TextHoldSeconds) EnterTextOut();
                    break;
                }

                case Phase.TextOut:
                {
                    // 消去は短く（拡大の 50% を目安に、SO には持たず）。
                    _phaseT += Time.deltaTime;
                    float dur = _config.TextScaleInDuration * 0.5f;
                    float k = dur > 0f ? Mathf.Clamp01(_phaseT / dur) : 1f;
                    if (_cutinText != null)
                        _cutinText.localScale = Vector3.Lerp(_cutinTargetScale, Vector3.zero, k);
                    if (k >= 1f) StartBeam();
                    break;
                }

                case Phase.Beaming:
                    break; // BeamSweepVfx が自身で一周し、OnBeamComplete で遷移する

                case Phase.WaitingShockwave:
                    _phaseT += Time.deltaTime;
                    if (_phaseT >= _config.ShockwaveDelay) FireShockwave();
                    break;

                case Phase.Firing:
                    // 衝撃波の演出オブジェクトが自壊(Destroy)したのと同じタイミングで砲台も消す。
                    if (_shockwaveInstance == null) FinishFiring();
                    break;
            }
        }

        private void EnterTextHold()
        {
            _phaseT = 0f;
            _phase = Phase.TextHold;
            // 保持開始時にプレハブ値で固定（縮み防止・浮動誤差の吸収）。
            if (_cutinText != null) _cutinText.localScale = _cutinTargetScale;
        }

        private void EnterTextOut()
        {
            _phaseT = 0f;
            _phase = Phase.TextOut;
        }

        // SpecialAttackShockwave の子に付いている SpecialAttackText（SpriteRenderer）を
        // 名称で探す。見つかっていない場合は null を返し、カットインはスキップされる（ビーム・衝撃波は照常）。
        private static Transform FindCutinText(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("SpecialAttackText") && t.GetComponent<SpriteRenderer>() != null)
                    return t;
            }
            return null;
        }

        private void StartBeam()
        {
            _phase = Phase.Beaming;

            float radius = RingRadius;
            Vector3 sourcePos = _muzzleTransform != null ? _muzzleTransform.position : _fortTransform.position;
            _activeBeam = new GameObject("BeamSweepVfx").AddComponent<BeamSweepVfx>();
            _activeBeam.Play(_fortTransform.position, sourcePos, radius, _config.BeamBandWidth,
                _config.BeamSweepDuration, _config.BeamColor, OnBeamTargetUpdate, OnBeamComplete);
        }

        // ビームが今指している地点に合わせて、砲台本体(銃身)を水平方向だけ向ける。
        // 回転後の砲口位置を光線の発射元にも反映し、銃身の動きと光線の根本がズレないようにする。
        private void OnBeamTargetUpdate(Vector3 targetPoint)
        {
            if (_cannonInstance == null) return;

            Vector3 toTarget = targetPoint - _cannonInstance.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 1e-6f) return;
            toTarget.Normalize();

            // 配置直後（未回転）の砲口方向 _cannonForwardWorld を toTarget に向ける回転を、
            // Y軸まわりのヨー角だけで計算する（毎フレーム絶対値なので累積ズレは起きない）。
            // Quaternion.FromToRotation は使わない — 2ベクトルがほぼ正反対になる瞬間
            // （掃引の半周あたり）に回転軸が不定になり、砲台が上下反転して見えることがあるため。
            float fromYaw = Mathf.Atan2(_cannonForwardWorld.x, _cannonForwardWorld.z) * Mathf.Rad2Deg;
            float toYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            var aim = Quaternion.AngleAxis(toYaw - fromYaw, Vector3.up) * _cannonInitialRotation;
            _cannonInstance.transform.rotation = aim;

            if (_muzzleTransform != null && _activeBeam != null)
                _activeBeam.SetSourcePosition(_muzzleTransform.position);
        }

        private void OnBeamComplete()
        {
            _phase = Phase.WaitingShockwave;
            _phaseT = 0f;
        }

        // 光線・衝撃波の半径。盤面の敵を全員（入場中も含めて）倒すので、見た目も入場口まで届かせる
        // （「見えている範囲＝効く範囲」）。プレハブに焼き込んだ固定値は使わない。
        private float RingRadius => _field.EntranceRadius + _config.RingRadiusPadding;

        private void FireShockwave()
        {
            _phase = Phase.Firing;

            // Fire() の時点で抑制されて Shockwave は既に Instantiate 済み（テキストだけ見ていた状態）。
            // ここのタイミングで初めて演出を解放して「爆発 + KillAll + 自壊」を走らせる。
            if (_shockwaveInstance != null)
            {
                var telegraph = _shockwaveInstance.GetComponent<ExplosionTelegraph>();
                if (telegraph != null)
                {
                    telegraph.PlayImmediate(RingRadius, (_, __) => KillAll());
                    return;
                }
                KillAll();
            }
            else
            {
                KillAll();
            }

            // 砲台を消すタイミングは Update() の Phase.Firing 側で、衝撃波オブジェクトが自壊した
            // (=衝撃波の演出が消えた)瞬間に合わせる（FinishFiring）。
        }

        private void KillAll()
        {
            int killed = _field != null ? _field.KillAllForBeam() : 0;
            OnFired?.Invoke(killed);
        }

        // 衝撃波の演出が消えたのと同じタイミングで、砲台も非表示に戻す。
        private void FinishFiring()
        {
            if (_cannonInstance != null) _cannonInstance.SetActive(false);
            _phase = Phase.Waiting;
        }
    }
}
