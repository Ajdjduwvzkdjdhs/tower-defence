using System;
using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 必殺技「ビーム砲」の予告(テレグラフ)演出: 砲塔の砲口から紫の光線が伸び、敵の静止リング上の
    /// 1点を指したまま、その点が円周上を一周する（回転する光線＝サーチライトのような動き）。
    /// 光線が通り過ぎた後の地面には、薄いリングの帯（トレイル）が残っていく。
    /// 単発の演出のため ObjectPool は使わず、一周し終えたら自身を破棄する。
    /// </summary>
    public class BeamSweepVfx : MonoBehaviour
    {
        private const int FullCircleSegments = 96; // 満円時の分割数。単発演出のため負荷より滑らかさを優先

        private static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
        private static Material _beamMaterial;

        private MeshFilter _filter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private MaterialPropertyBlock _mpb;
        private readonly List<Vector3> _vertScratch = new List<Vector3>((FullCircleSegments + 1) * 2);
        private readonly List<int> _triScratch = new List<int>(FullCircleSegments * 6);

        // 光線は「外側の淡いグロー」＋「内側の白熱コア」の二重ラインで表現する（加算合成）。
        // 単色のラインだけだと平坦な色帯に見えてしまい「光線らしさ」が出ないため。
        private LineRenderer _beamOuter;
        private LineRenderer _beamInner;
        private MaterialPropertyBlock _outerMpb;
        private MaterialPropertyBlock _innerMpb;
        private Color _beamColor;

        private Vector3 _center;
        private Vector3 _sourcePosition;
        private float _radius;
        private float _bandWidth;
        private float _duration;
        private float _t;
        private bool _playing;
        private Action _onDone;
        private Action<Vector3> _onTargetUpdate;

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "BeamSweepRing" };

            _filter = gameObject.AddComponent<MeshFilter>();
            _filter.sharedMesh = _mesh;

            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = GetBeamMaterial();
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;

            _outerMpb = new MaterialPropertyBlock();
            _innerMpb = new MaterialPropertyBlock();
            _beamOuter = BuildBeamLine("BeamOuterGlow");
            _beamInner = BuildBeamLine("BeamInnerCore");
        }

        private LineRenderer BuildBeamLine(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = GetBeamMaterial();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        /// <summary>
        /// center を中心に radius のリング上の1点を指す光線を、sourcePosition(砲口)から伸ばし、
        /// duration 秒かけて一周させる。一周し終えると onDone を呼び、自身を破棄する。
        /// bandWidth は通過後に地面へ残るトレイルの帯の太さ。onTargetUpdate は毎フレーム、光線が
        /// 指している現在地点(ワールド座標)を渡す（砲台本体を光線の方向へ向けるためのフック）。
        /// </summary>
        public void Play(Vector3 center, Vector3 sourcePosition, float radius, float bandWidth, float duration,
            Color color, Action<Vector3> onTargetUpdate, Action onDone)
        {
            transform.position = center;
            _center = center;
            _sourcePosition = sourcePosition;
            _radius = Mathf.Max(0.1f, radius);
            _bandWidth = Mathf.Max(0.05f, bandWidth);
            _duration = Mathf.Max(0.01f, duration);
            _onTargetUpdate = onTargetUpdate;
            _onDone = onDone;
            _t = 0f;
            _playing = true;

            _beamColor = color;

            // 地面のトレイル: 単に薄い元色ではなく、彩度を上げて発光量を増やす（平坦で地味に見えないように）。
            _mpb.Clear();
            _mpb.SetColor(IdBaseColor, new Color(color.r * 1.4f, color.g * 0.9f, color.b * 1.6f, 1f));
            _meshRenderer.SetPropertyBlock(_mpb);

            // 外側グロー: 太め・彩度のある紫（加算合成なので明るいほど「発光」して見える）。
            _outerMpb.Clear();
            _outerMpb.SetColor(IdBaseColor, new Color(color.r * 1.3f, color.g * 1.3f, color.b * 1.6f, 1f));
            _beamOuter.SetPropertyBlock(_outerMpb);
            _beamOuter.startWidth = 0.55f;
            _beamOuter.endWidth = 0.8f;

            // 内側コア: 細め・白に近い高輝度色（レーザーの「芯」）。
            var core = Color.Lerp(color, Color.white, 0.75f);
            _innerMpb.Clear();
            _innerMpb.SetColor(IdBaseColor, new Color(core.r * 2f, core.g * 2f, core.b * 2f, 1f));
            _beamInner.SetPropertyBlock(_innerMpb);
            _beamInner.startWidth = 0.12f;
            _beamInner.endWidth = 0.22f;

            UpdateVisuals(0f);
        }

        /// <summary>光線の発射元（砲口）位置を更新する。砲台本体が向き直る（回転する）のに追従させる用。</summary>
        public void SetSourcePosition(Vector3 sourcePosition) => _sourcePosition = sourcePosition;

        private void Update()
        {
            if (!_playing) return;

            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _duration);
            UpdateVisuals(k * Mathf.PI * 2f);

            if (k < 1f) return;

            _playing = false;
            _onDone?.Invoke();
            Destroy(gameObject);
        }

        private void UpdateVisuals(float sweepAngle)
        {
            RebuildMesh(sweepAngle);

            float cx = Mathf.Sin(sweepAngle), cz = Mathf.Cos(sweepAngle);
            var targetPoint = _center + new Vector3(cx * _radius, 0f, cz * _radius);

            _beamOuter.SetPosition(0, _sourcePosition);
            _beamOuter.SetPosition(1, targetPoint);
            _beamInner.SetPosition(0, _sourcePosition);
            _beamInner.SetPosition(1, targetPoint);

            // 微細な明滅（電流っぽさ）。加算合成の輝度を毎フレーム少し揺らす。
            float flicker = 1f + 0.15f * Mathf.Sin(Time.time * 40f);
            _innerMpb.Clear();
            var core = Color.Lerp(_beamColor, Color.white, 0.75f) * (2f * flicker);
            core.a = 1f;
            _innerMpb.SetColor(IdBaseColor, core);
            _beamInner.SetPropertyBlock(_innerMpb);

            _onTargetUpdate?.Invoke(targetPoint);
        }

        // 中心角0からsweepAngleまでの帯状リング(扇形の輪、通過済みトレイル)を構築する。
        private void RebuildMesh(float sweepAngle)
        {
            _mesh.Clear();
            if (sweepAngle <= 0f) return;

            int segs = Mathf.Clamp(Mathf.CeilToInt(FullCircleSegments * (sweepAngle / (Mathf.PI * 2f))), 1, FullCircleSegments);
            float innerR = _radius - _bandWidth * 0.5f;
            float outerR = _radius + _bandWidth * 0.5f;

            _vertScratch.Clear();
            _triScratch.Clear();

            for (int i = 0; i <= segs; i++)
            {
                float a = sweepAngle * i / segs;
                float cx = Mathf.Sin(a), cz = Mathf.Cos(a);
                _vertScratch.Add(new Vector3(cx * innerR, 0f, cz * innerR));
                _vertScratch.Add(new Vector3(cx * outerR, 0f, cz * outerR));
            }

            for (int i = 0; i < segs; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = (i + 1) * 2, d = (i + 1) * 2 + 1;
                _triScratch.Add(a); _triScratch.Add(c); _triScratch.Add(b);
                _triScratch.Add(b); _triScratch.Add(c); _triScratch.Add(d);
            }

            _mesh.SetVertices(_vertScratch);
            _mesh.SetTriangles(_triScratch, 0);
            _mesh.RecalculateBounds();
        }

        // 加算合成の Unlit。カメラ角度に関わらず見えるよう両面描画（_Cull Off）にする。地面トレイル用。
        private static Material GetBeamMaterial()
        {
            if (_beamMaterial != null) return _beamMaterial;

            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _beamMaterial = m;
            return m;
        }
    }
}
