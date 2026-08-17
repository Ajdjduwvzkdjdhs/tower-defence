using UnityEngine;

public class SphereSpawner : MonoBehaviour
{
    [Header("スポーン設定")]
    [Tooltip("生成する球体のプレハブ（未設定なら標準の球体を自動生成）")]
    public GameObject spherePrefab;

    [Tooltip("スポーンする位置（未設定ならこのオブジェクトの位置）")]
    public Transform spawnPoint;

    [Tooltip("スポーン間隔（秒）")]
    public float spawnInterval = 2f;

    [Tooltip("生成した球体の半径")]
    public float sphereScale = 0.5f;

    void Start()
    {
        // spawnIntervalごとにSpawnSphere()を繰り返し呼び出す
        // 最初の呼び出しはspawnInterval秒後から開始
        InvokeRepeating(nameof(SpawnSphere), spawnInterval, spawnInterval);
    }

    void SpawnSphere()
    {
        // 生成位置を決定（spawnPointがなければ自身の位置を使用）
        // ランダムなズレは加えず、常に同じ位置から生成する
        Vector3 spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject sphere;

        if (spherePrefab != null)
        {
            // 自作のプレハブがあればそれを生成
            sphere = Instantiate(spherePrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            // プレハブが未設定なら標準の球体を動的に生成
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = spawnPos;
            sphere.transform.localScale = Vector3.one * sphereScale;
        }

        // Rigidbodyがなければ追加（重力で落下・転がるように）
        if (sphere.GetComponent<Rigidbody>() == null)
        {
            sphere.AddComponent<Rigidbody>();
        }

        // 一定時間後に自動で削除したい場合は以下のコメントを外す
        // Destroy(sphere, 10f);
    }
}