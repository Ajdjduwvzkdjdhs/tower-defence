using System.Collections;
using UnityEngine;

public class GameOverEffect : MonoBehaviour
{
    [Header("UI設定")]
    public RectTransform logoRect;   // GAME OVERのロゴ（RectTransform）
    public RectTransform shakeTarget; // 揺らしたい対象（Source Image や Canvas など）
    
    [Header("アニメーション設定")]
    [Tooltip("上から落ちてくる時間（秒）")]
    public float dropDuration = 0.8f; // 落ちる速度

    [Tooltip("着地後の揺れ時間（秒）")]
    public float shakeDuration = 0.3f;// 揺れる時間

    [Tooltip("揺れの強さ（ピクセル数）")]
    public float shakeMagnitude = 30f;// 揺れの強さ

    private Vector2 logoTargetPos;
    private Vector2 shakeTargetOriginalPos;

    void Awake()
    {
        if (logoRect == null)
        {
            logoRect = GetComponent<RectTransform>();
        }

        if (logoRect != null)
        {
            // ロゴの本来の配置位置を記憶
            logoTargetPos = logoRect.anchoredPosition;
            // 開始時は画面の上外へ移動
            logoRect.anchoredPosition = new Vector2(logoTargetPos.x, logoTargetPos.y + 1000f);
        }

        if (shakeTarget != null)
        {
            // 揺らす対象の元位置を記憶
            shakeTargetOriginalPos = shakeTarget.anchoredPosition;
        }
    }

    void OnEnable()
    {
        StartCoroutine(DropAndShakeRoutine());
    }

    private IEnumerator DropAndShakeRoutine()
    {
        float elapsed = 0f;
        Vector2 startPos = logoRect.anchoredPosition;

        // 1. ロゴが上からドスンと落下する処理
        while (elapsed < dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / dropDuration;
            t = t * t; // イージング（加速しながら落下）
            logoRect.anchoredPosition = Vector2.Lerp(startPos, logoTargetPos, t);
            yield return null;
        }

        logoRect.anchoredPosition = logoTargetPos;

        // 2. 着地した瞬間に【指定したオブジェクト（背景など）】を揺らす処理
        if (shakeTarget != null)
        {
            elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                elapsed += Time.deltaTime;
                float offsetX = Random.Range(-1f, 1f) * shakeMagnitude;
                float offsetY = Random.Range(-1f, 1f) * shakeMagnitude;

                shakeTarget.anchoredPosition = shakeTargetOriginalPos + new Vector2(offsetX, offsetY);
                yield return null;
            }

            // 最後に元の位置に戻す
            shakeTarget.anchoredPosition = shakeTargetOriginalPos;
        }
    }
}