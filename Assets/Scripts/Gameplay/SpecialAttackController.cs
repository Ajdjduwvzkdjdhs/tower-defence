using System.Reflection;
using UnityEngine;
using KakigoriTD.Core;
using KakigoriTD.Data;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 必殺技「ビーム砲」の統括。敵を <see cref="SpecialAttackConfigSO.KillThreshold"/> 体倒すとチャージ完了となり、
    /// 山頂のビーム砲モデルが出現して自動発動する。紫の光線( <see cref="BeamSweepVfx"/> )が敵の静止リング
    /// （<see cref="EnemyField.RestRadius"/>）を一周し、少し遅れて衝撃波(<see cref="ExplosionTelegraph"/> の
    /// 色違いプレハブを再利用)が走って範囲内の敵を <see cref="EnemyField.KillAllWithin"/> でまとめて撃破する。
    /// 発動後はカウントをリセットし、再び<see cref="SpecialAttackConfigSO.KillThreshold"/>体倒すと再発動する
    /// （1ゲーム中に複数回使える）。撃破カウントは <see cref="KillTally"/>（純ロジック・ADR-8）に委譲する。
    /// </summary>
    public class SpecialAttackController : MonoBehaviour
    {
        private enum Phase { Waiting, Charged, Beaming, WaitingShockwave, Firing }

        // ExplosionTelegraph._radius を実行時に上書きするためのリフレクション。プレハブ側の値は
        // フラッシュ光の range 計算（Awake 時のみ参照）に使われた後なので、ここで上書きしても
        // 閃光が過大にならない。ExplosionTelegraph.cs 自体（他ブランチ由来）は変更しない。
        private static readonly FieldInfo RadiusField =
            typeof(ExplosionTelegraph).GetField("_radius", BindingFlags.NonPublic | BindingFlags.Instance);

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

        private readonly KillTally _tally = new KillTally();
        private Phase _phase = Phase.Waiting;
        private float _phaseT;

        /// <summary>前回発動からの撃破数（HUD用）。</summary>
        public int KillsSinceCharge => _tally.Total;

        /// <summary>チャージに必要な撃破数（HUD用）。</summary>
        public int Threshold => _config != null ? _config.KillThreshold : 0;

        /// <summary>チャージ完了〜発動シーケンス中かどうか（HUD用）。</summary>
        public bool IsCharged => _phase != Phase.Waiting;

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

            _field.OnEnemyKilled += OnEnemyKilled;

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

        // 発動シーケンス中（Waiting 以外）の撃破は数えない。衝撃波自身が巻き込む敵の同時撃破が
        // 即座に次チャージへ持ち越ってしまう（無限連鎖）のを防ぐガード。
        private void OnEnemyKilled()
        {
            if (_phase != Phase.Waiting) return;

            _tally.Add(1);
            if (_tally.Total < Threshold) return;

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

            float radius = _field.RestRadius + _config.RingRadiusPadding;
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

        private void FireShockwave()
        {
            _phase = Phase.Firing;
            _shockwaveInstance = null;

            float radius = _field.RestRadius + _config.RingRadiusPadding;
            Vector3 center = _fortTransform.position;

            if (_shockwavePrefab != null)
            {
                _shockwaveInstance = Instantiate(_shockwavePrefab, center, Quaternion.identity);
                var telegraph = _shockwaveInstance.GetComponent<ExplosionTelegraph>();
                if (telegraph != null)
                {
                    // 見た目の半径を、実際に敵が撃破される半径(敵の静止半径ベース)まで届くよう
                    // 動的に上書きする。プレハブに焼き込んだ固定値だと EnemyField.RestRadius を
                    // 調整したときにズレてしまうため。
                    RadiusField?.SetValue(telegraph, radius);
                    telegraph.PlayImmediate((_, __) => _field.KillAllWithin(center, radius));
                }
                else _field.KillAllWithin(center, radius);
            }
            else
            {
                _field.KillAllWithin(center, radius);
            }

            // 砲台を消すタイミングは Update() の Phase.Firing 側で、衝撃波オブジェクトが自壊した
            // (=衝撃波の演出が消えた)瞬間に合わせる（FinishFiring）。プレハブ未設定時は次フレームで即完了する。
        }

        // 衝撃波の演出が消えたのと同じタイミングで、砲台も非表示に戻す。
        private void FinishFiring()
        {
            if (_cannonInstance != null) _cannonInstance.SetActive(false);
            _tally.Reset();
            _phase = Phase.Waiting;
        }

        private void OnDestroy()
        {
            if (_field != null) _field.OnEnemyKilled -= OnEnemyKilled; // 購読解除（ADR-3）
        }
    }
}
