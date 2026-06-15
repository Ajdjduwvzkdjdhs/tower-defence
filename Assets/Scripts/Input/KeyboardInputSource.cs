using UnityEngine;
using UnityEngine.InputSystem;

namespace KakigoriTD.Input
{
    /// <summary>
    /// キーボードによる <see cref="IInputSource"/> 実装（Strategy、ADR-6）。
    ///
    /// なぜ新 Input System を使うか:
    /// 本プロジェクトの Active Input Handling は「Input System Package (New)」のみに設定されている。
    /// この設定下では旧 <c>UnityEngine.Input.GetKey/GetKeyDown</c>（レガシー API）は実行時に例外を投げるため使えない。
    /// よって入力読み取りは <see cref="Keyboard.current"/>（UnityEngine.InputSystem）経由で行う。
    ///
    /// ADR-6 に従い、入力パッケージ（UnityEngine.InputSystem）への直接参照はこのクラス内に限定する。
    /// 他のシステムは <see cref="IInputSource"/> を介してのみ入力を読むこと。
    /// 実機接続時は IInputSource の別実装を作り、GameManager での代入を差し替えるだけでよい。
    /// </summary>
    public class KeyboardInputSource : MonoBehaviour, IInputSource
    {
        /// <inheritdoc/>
        public int ActiveChannel { get; private set; } = -1;

        /// <inheritdoc/>
        public bool IsShaving { get; private set; }

        /// <inheritdoc/>
        public bool ConfirmPressed { get; private set; }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) { ConfirmPressed = false; return; }

            // 決定/開始/再挑戦は Enter のエッジ（押下の瞬間のみ）。削り Space と役割を分ける。
            ConfirmPressed = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;

            // 谷の選択はラッチ式・同時1本のみ。このフレームで押された方向に切り替える（else-if で1本に限定）。
            // W/A/S/D -> 0:N / 1:W / 2:S / 3:E。
            if (keyboard.wKey.wasPressedThisFrame) ActiveChannel = 0;
            else if (keyboard.aKey.wasPressedThisFrame) ActiveChannel = 1;
            else if (keyboard.sKey.wasPressedThisFrame) ActiveChannel = 2;
            else if (keyboard.dKey.wasPressedThisFrame) ActiveChannel = 3;

            // 削りは Space 長押し中だけ true。
            IsShaving = keyboard.spaceKey.isPressed;
        }
    }
}
