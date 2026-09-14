using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// テスト用: 爆発予告(テレグラフ)演出。薄い赤の円(警告)が現れ、それをなぞるように濃い赤が
    /// 中心から広がって塗りつぶし、塗りきったところで爆発する（スマホゲームの範囲攻撃予告と同じ流れ）。
    /// 爆発の瞬間はフラッシュ(Point Light)・パーティクルバースト・カメラシェイクで「効いた感」を出す。
    /// <see cref="_radius"/> はインスペクタで調整できる。単発の検証用エフェクトのため
    /// ObjectPool は使わず、再生し終えたら自身を破棄する。
    /// 演出専用（cosmetic）ではなく、爆発した瞬間に <see cref="PlayImmediate"/> の onExplode コールバックで
    /// 呼び出し元へ (中心位置, 半径) を通知する。<see cref="EnemyField"/> はこれで巻き込み撃破を行う。
    /// </summary>
    public class ExplosionTelegraph : MonoBehaviour
    {
        [Header("範囲")]
        [SerializeField] private float _radius = 2f;

        [Header("タイミング")]
        [SerializeField] private float _warningHold = 0.25f;
        [SerializeField] private float _fillDuration = 0.6f;
        [SerializeField] private float _explodeDuration = 0.5f;

        [Header("見た目")]
        [SerializeField] private Color _warningColor = new Color(1f, 0.15f, 0.15f, 0.35f);
        [SerializeField] private Color _fillColor = new Color(0.55f, 0f, 0f, 0.85f);
        [SerializeField] private int _circleSegments = 48;
        [SerializeField] private float _cylinderHeight = 4.5f; // 敵が隠れるくらいの高さ（平面の円だと見づらいため）

        [Header("爆発の演出（フラッシュ・パーティクル・シェイク）")]
        [SerializeField] private Color _flashColor = new Color(1f, 0.9f, 0.6f);
        [SerializeField] private float _flashIntensity = 6f;
        [SerializeField] private float _flashDuration = 0.15f;
        [SerializeField] private int _particleCount = 30;
        [SerializeField] private float _cameraShakeMagnitude = 0.15f;
        [SerializeField] private float _cameraShakeDuration = 0.2f;

        private static readonly int IdColor = Shader.PropertyToID("_Color");
        private static Mesh _cylinderMesh;
        private static Material _discMaterial;
        private static Material _particleMaterial;

        private enum Phase { Warning, Fill, Explode, Done }

        private MeshRenderer _warningRenderer;
        private MeshRenderer _fillRenderer;
        private Transform _warningDisc;
        private Transform _fillDisc;
        private MaterialPropertyBlock _mpb;
        private Light _flashLight;
        private ParticleSystem _burst;

        private Phase _phase;
        private float _t;

        private Camera _cam;
        private Vector3 _camBasePos;
        private bool _shaking;

        private System.Action<Vector3, float> _onExplode;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();

            _warningDisc = BuildCylinder("WarningZone", _warningColor, 0f);
            _fillDisc = BuildCylinder("FillZone", _fillColor, 0.01f); // わずかに浮かせて z-fighting を防ぐ
            _warningRenderer = _warningDisc.GetComponent<MeshRenderer>();
            _fillRenderer = _fillDisc.GetComponent<MeshRenderer>();

            _warningDisc.localScale = new Vector3(_radius, _cylinderHeight, _radius);
            _fillDisc.localScale = Vector3.zero;

            _flashLight = BuildFlashLight();
            _burst = BuildBurstParticles();

            _phase = Phase.Warning;
            _t = 0f;
        }

        /// <summary>
        /// 警告・予告フェーズを飛ばし、いきなり爆発フェーズから再生する。撃破と同時に爆発させたい
        /// 用途向け（例: <see cref="EnemyField"/> の撃破フック）。
        /// onExplode は爆発した瞬間に (中心位置, 半径) を渡して呼ばれる（巻き込み撃破など、演出以外の
        /// 効果を呼び出し元から差し込むためのフック。null なら何もしない）。
        /// </summary>
        public void PlayImmediate(System.Action<Vector3, float> onExplode = null)
        {
            _onExplode = onExplode;
            _phase = Phase.Explode;
            _t = 0f;
            StartExplode();
        }

        private void Update()
        {
            _t += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Warning:
                    if (_t < _warningHold) return;
                    _phase = Phase.Fill;
                    _t = 0f;
                    return;

                case Phase.Fill:
                {
                    float k = _fillDuration > 0f ? Mathf.Clamp01(_t / _fillDuration) : 1f;
                    _fillDisc.localScale = new Vector3(_radius * k, _cylinderHeight, _radius * k);
                    if (k < 1f) return;

                    _phase = Phase.Explode;
                    _t = 0f;
                    StartExplode();
                    return;
                }

                case Phase.Explode:
                {
                    float k = _explodeDuration > 0f ? Mathf.Clamp01(_t / _explodeDuration) : 1f;
                    float scale = _radius * (1f + 0.4f * k);
                    _fillDisc.localScale = new Vector3(scale, _cylinderHeight, scale);
                    _warningDisc.localScale = new Vector3(scale, _cylinderHeight, scale);
                    SetAlpha(_fillRenderer, _fillColor.a * (1f - k));
                    SetAlpha(_warningRenderer, _warningColor.a * (1f - k));

                    float flashK = _flashDuration > 0f ? Mathf.Clamp01(_t / _flashDuration) : 1f;
                    _flashLight.intensity = _flashIntensity * (1f - flashK);

                    UpdateCameraShake();

                    if (k < 1f) return;

                    _phase = Phase.Done;
                    Destroy(gameObject);
                    return;
                }
            }
        }

        /// <summary>塗りきった瞬間に一度だけ呼ぶ。フラッシュ・パーティクル・カメラシェイクを起動する。</summary>
        private void StartExplode()
        {
            _flashLight.intensity = _flashIntensity;
            _burst.Play();
            _onExplode?.Invoke(transform.position, _radius);

            _cam = Camera.main;
            if (_cam != null)
            {
                _camBasePos = _cam.transform.localPosition;
                _shaking = true;
            }
        }

        private void UpdateCameraShake()
        {
            if (!_shaking || _cam == null) return;

            float k = _cameraShakeDuration > 0f ? Mathf.Clamp01(_t / _cameraShakeDuration) : 1f;
            if (k >= 1f)
            {
                _cam.transform.localPosition = _camBasePos;
                _shaking = false;
                return;
            }

            float magnitude = _cameraShakeMagnitude * (1f - k);
            _cam.transform.localPosition = _camBasePos + Random.insideUnitSphere * magnitude;
        }

        private void OnDestroy()
        {
            // 破棄タイミングに関わらずカメラのシェイクによるズレを必ず戻す。
            if (_shaking && _cam != null) _cam.transform.localPosition = _camBasePos;
        }

        private Transform BuildCylinder(string name, Color color, float yOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = GetCylinderMesh(_circleSegments);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetDiscMaterial();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _mpb.Clear();
            _mpb.SetColor(IdColor, color);
            renderer.SetPropertyBlock(_mpb);
            return go.transform;
        }

        private void SetAlpha(MeshRenderer renderer, float alpha)
        {
            renderer.GetPropertyBlock(_mpb);
            var c = _mpb.GetColor(IdColor);
            c.a = alpha;
            _mpb.SetColor(IdColor, c);
            renderer.SetPropertyBlock(_mpb);
        }

        private Light BuildFlashLight()
        {
            var go = new GameObject("Flash");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = _flashColor;
            light.range = Mathf.Max(1f, _radius * 4f);
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            return light;
        }

        private ParticleSystem BuildBurstParticles()
        {
            var go = new GameObject("Burst");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _particleCount * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)_particleCount) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f),
                    new GradientColorKey(new Color(1f, 0.4f, 0.1f), 0.4f),
                    new GradientColorKey(new Color(0.25f, 0.05f, 0.05f), 1f)
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = GetParticleMaterial();
            renderer.sortMode = ParticleSystemSortMode.None;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static Material GetParticleMaterial()
        {
            // URP では Resources.GetBuiltinResource("Default-Particle.mat") が解決できないため、
            // 円と同じ Sprites/Default（頂点カラー×_Color の単純な半透明シェーダー）を使い回す。
            if (_particleMaterial == null)
                _particleMaterial = new Material(Shader.Find("Sprites/Default"));
            return _particleMaterial;
        }

        private static Material GetDiscMaterial()
        {
            if (_discMaterial == null)
                _discMaterial = new Material(Shader.Find("Sprites/Default"));
            return _discMaterial;
        }

        // 底なしの円柱（半径1・高さ1、原点=底面中心）。平面の円は見づらいため高さを持たせている。
        // 側面は上下で頂点カラーに明暗差をつけ、非リットの Sprites/Default でも立体感が出るようにする。
        private static Mesh GetCylinderMesh(int segments)
        {
            if (_cylinderMesh != null) return _cylinderMesh;

            segments = Mathf.Max(8, segments);
            int topCenter = 0;
            int topCapStart = 1;
            int sideTopStart = topCapStart + segments;
            int sideBottomStart = sideTopStart + segments;
            int vertCount = sideBottomStart + segments;

            var vertices = new Vector3[vertCount];
            var colors = new Color[vertCount];

            vertices[topCenter] = new Vector3(0f, 1f, 0f);
            colors[topCenter] = Color.white;

            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float cx = Mathf.Cos(angle), cz = Mathf.Sin(angle);

                vertices[topCapStart + i] = new Vector3(cx, 1f, cz);
                colors[topCapStart + i] = Color.white;

                vertices[sideTopStart + i] = new Vector3(cx, 1f, cz);
                colors[sideTopStart + i] = new Color(0.8f, 0.8f, 0.8f, 1f);

                vertices[sideBottomStart + i] = new Vector3(cx, 0f, cz);
                colors[sideBottomStart + i] = new Color(0.45f, 0.45f, 0.45f, 1f);
            }

            var triangles = new System.Collections.Generic.List<int>(segments * 9);
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;

                // 上面キャップ
                triangles.Add(topCenter);
                triangles.Add(topCapStart + i);
                triangles.Add(topCapStart + next);

                // 側面（2枚の三角形）
                int a = sideTopStart + i, b = sideTopStart + next;
                int c = sideBottomStart + i, d = sideBottomStart + next;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }

            _cylinderMesh = new Mesh { name = "ExplosionTelegraphCylinder" };
            _cylinderMesh.SetVertices(vertices);
            _cylinderMesh.SetColors(colors);
            _cylinderMesh.SetTriangles(triangles, 0);
            _cylinderMesh.RecalculateNormals();
            _cylinderMesh.RecalculateBounds();
            return _cylinderMesh;
        }
    }
}
