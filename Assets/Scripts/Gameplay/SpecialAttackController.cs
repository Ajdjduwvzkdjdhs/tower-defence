using System;
using UnityEngine;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 必殺技「ビーム砲」の演出進行役（ADR-30）。発動条件は GameManager 側の <c>SpecialGauge</c>（ゲージ MAX で
    /// 自動発動）が決め、本クラスは <see cref="Fire"/> で呼ばれてから一連の演出を進める:
    /// 山頂にビーム砲モデルが出現 → 紫の光線(<see cref="BeamSweepVfx"/>)が敵の並ぶ輪を一周 → 少し遅れて
    /// 衝撃波(<see cref="ExplosionTelegraph"/> の色違いプレハブ)が走り、**その瞬間に盤面の敵を全員**
    /// <see cref="EnemyField.KillAllForBeam"/> で撃破 → 衝撃波が消えたら砲も消える。
    /// 展示にはプレイヤーが押せるボタンが無いため照準入力を持たず、全方位に均一に効かせる。
    /// 演出中（<see cref="IsPlaying"/>）は GameManager が次の発動を待たせる。
    /// </summary>
    public class SpecialAttackController : MonoBehaviour
    {
        private enum Phase { Waiting, Charged, Beaming, WaitingShockwave, Firing }

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
        /// </summary>
        public void Fire()
        {
            if (_phase != Phase.Waiting) return;

            _phase = Phase.Charged;
            if (_cannonInstance != null) _cannonInstance.SetActive(true);
        }

        private void Update()
        {
            switch (_phase)
            {
                case Phase.Charged:
                    StartBeam();
                    break;

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
            _shockwaveInstance = null;

            float radius = RingRadius;
            Vector3 center = _fortTransform.position;

            if (_shockwavePrefab != null)
            {
                _shockwaveInstance = Instantiate(_shockwavePrefab, center, Quaternion.identity);
                var telegraph = _shockwaveInstance.GetComponent<ExplosionTelegraph>();
                if (telegraph != null) telegraph.PlayImmediate(radius, (_, __) => KillAll());
                else KillAll();
            }
            else
            {
                KillAll();
            }

            // 砲台を消すタイミングは Update() の Phase.Firing 側で、衝撃波オブジェクトが自壊した
            // (=衝撃波の演出が消えた)瞬間に合わせる（FinishFiring）。プレハブ未設定時は次フレームで即完了する。
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
