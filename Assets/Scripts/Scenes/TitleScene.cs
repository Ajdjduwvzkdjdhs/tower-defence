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

        private bool isTransitioning = false;

        void Update()
        {
            if (Keyboard.current != null &&
                Keyboard.current.enterKey.wasPressedThisFrame &&
                !isTransitioning)
            {
                StartCoroutine(FadeAndLoadScene());
            }
        }

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
