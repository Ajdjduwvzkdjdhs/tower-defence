using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace KakigoriTD.Scenes
{
    public class TitleScene : MonoBehaviour
    {
        [SerializeField] private CanvasGroup fadeCanvas;
        [SerializeField] private float fadeDuration = 1.0f;
        [Tooltip("D キーをこの秒数押し続けるとランキングのローカル DB ファイルを消す（ADR-34・展示のリセット用）。")]
        [SerializeField] private float rankingResetHoldSec = 5f;

        private bool isTransitioning = false;
        private RankingResetHotkey rankingReset;

        void Awake()
        {
            rankingReset = RankingResetHotkey.CreateDefault(rankingResetHoldSec);
        }

        void Update()
        {
            rankingReset.Update(Time.deltaTime);
            if (Keyboard.current != null &&
                Keyboard.current.enterKey.wasPressedThisFrame &&
                !isTransitioning)
            {
                StartCoroutine(FadeAndLoadScene());
            }
        }

        private void OnGUI() => rankingReset.OnGUI();

        private IEnumerator FadeAndLoadScene()
        {
            isTransitioning = true;

            float time = 0f;

            // 透明 → 黒
            while (time < fadeDuration)
            {
                time += Time.deltaTime;
                fadeCanvas.alpha = Mathf.Lerp(0f, 1f, time / fadeDuration);
                yield return null;
            }

            fadeCanvas.alpha = 1f;

            // フェード完了後にシーン切り替え
            SceneManager.LoadScene("TH");
        }
    }
}
