using System;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 必殺ビーム砲（ADR-30）。ゲージ MAX で **自動発動**し、盤面の敵を全方位一掃する。
    /// 展示にはプレイヤーが押せるボタンが無いため照準入力を持たない＝方向のある演出にしない
    /// （「なぜあっちに撃ったのか」が生まれないよう、全方位に均一に効かせる）。
    ///
    /// 演出は差し替え前提のプレースホルダ: 画面全体の白フラッシュ（立ち上がり 0.15s → 減衰 0.6s）。
    /// 撃破自体は発動と同時に確定させ、フラッシュは cosmetic に後追いする。
    /// 描画は IMGUI（ADR-13 の UI 方針に合わせる）。
    /// </summary>
    public class BeamCannon : MonoBehaviour
    {
        private const float RiseSec = 0.15f;
        private const float FallSec = 0.60f;

        private EnemyField _field;
        private float _elapsed = float.PositiveInfinity; // 再生していない状態
        private Texture2D _white;

        /// <summary>発動したフレームに発火（引数＝一掃した体数）。音・ログ用。</summary>
        public event Action<int> OnFired;

        /// <summary>再生中か（UI が他の表示を抑えるのに使う）。</summary>
        public bool IsPlaying => _elapsed < RiseSec + FallSec;

        public void Configure(EnemyField field)
        {
            _field = field;
        }

        private void Awake()
        {
            _white = Texture2D.whiteTexture;
        }

        /// <summary>ビームを撃つ。盤面を一掃し、フラッシュを再生する。</summary>
        public void Fire()
        {
            int killed = _field != null ? _field.KillAllForBeam() : 0;
            _elapsed = 0f;
            OnFired?.Invoke(killed);
        }

        private void Update()
        {
            if (IsPlaying) _elapsed += Time.deltaTime;
        }

        private void OnGUI()
        {
            if (!IsPlaying) return;

            float a = _elapsed < RiseSec
                ? Mathf.Clamp01(_elapsed / RiseSec)                       // 白く飛ぶ
                : 1f - Mathf.Clamp01((_elapsed - RiseSec) / FallSec);     // 戻る

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a * 0.9f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _white);
            GUI.color = prev;
        }
    }
}
