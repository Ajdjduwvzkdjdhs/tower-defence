using System;
using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 撃破された敵の「身代わり」を出し、雪に押されて麓側へ吹き飛ばしてから蒸発させる（ADR-20）。
    /// <see cref="Play"/> で起動し、_DissolveAmount を 0→1 に進めたあと粒子が消えきるのを待って
    /// プール返却コールバックを呼ぶ。<see cref="KakigoriTD.Factory.ObjectPool{T}"/> で使い回し、
    /// ホットパスで Instantiate しない（ADR-4）。
    ///
    /// 敵本体には触れない。<see cref="ChannelController.ApplyAvalanche"/> は従来どおり
    /// Kill→Release を即座に行い、本エフェクトは複製した見た目だけを溶かす。
    /// 撃破解決ロジックには一切関与しない純 cosmetic（ADR-27）。
    ///
    /// 対象の階層下にある Renderer をすべて複製するため、単体プリミティブでも
    /// パーツ分割された FBX（TGS_兵士赤 は 17 Renderer）でも全身が溶ける。
    /// パーツ用の子オブジェクトは自身の中で使い回すので、2 回目以降は確保が起きない。
    /// SkinnedMeshRenderer は BakeMesh でその瞬間のポーズを焼き取る。
    /// </summary>
    public class DissolveBurst : MonoBehaviour
    {
        /// <summary>吹き飛んで止まるまでの秒数。この間は溶けない。</summary>
        private const float FlightTime = 0.45f;

        /// <summary>飛行中に横回転する角度の合計。位置と同じ ease-out で減速し、着地時に止まる。</summary>
        // 360 の倍数にしておくと着地時に直立へ戻る（半端な値だと逆さまのまま溶け始める）。
        private const float SpinDegrees = 720f;

        /// <summary>放物線の高さ。モデルの高さに対する比率（大きさが変わっても破綻しない）。</summary>
        private const float ArcRatio = 0.18f;

        /// <summary>溶けきるまでの秒数。</summary>
        private const float Duration = 0.5f;

        /// <summary>溶けきってから粒子が消えるまでの待ち時間（この間メッシュは非表示）。</summary>
        private const float EmberTail = 0.75f;

        // 粒子は控えめ。1 行最大 rowWidth 体 × 4 谷が同時に消えうるため、密度より個数を抑える。
        private const float EmberRate = 30f;
        private const float EmberLifeMin = 0.35f;
        private const float EmberLifeMax = 0.7f;
        private const int EmberMaxParticles = 48;

        private static readonly int IdDissolve = Shader.PropertyToID("_DissolveAmount");
        private static readonly int IdBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int IdBaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>複製した Renderer 1 つぶん。サブメッシュごとに色が違うので block も分ける。</summary>
        private class Part
        {
            public Transform Tr;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock[] Blocks = Array.Empty<MaterialPropertyBlock>();
            public Material[] Materials = Array.Empty<Material>();
            public Mesh Bake;      // SkinnedMeshRenderer 用。使い回して GC を避ける
            public int SubMeshes;
        }

        private readonly List<Part> _parts = new List<Part>();
        private readonly List<Renderer> _sources = new List<Renderer>();
        private MaterialPropertyBlock _srcBlock;

        private Material _dissolveMaterial;
        private Material _emberMaterial;
        private ParticleSystem _embers;

        private enum Phase { Flight, Dissolve, Tail }

        private Action<DissolveBurst> _onDone;
        private Phase _phase;
        private float _t;
        private Vector3 _flightFrom, _flightTo, _spinAxis;
        private float _arcHeight;
        private int _activeParts;
        private bool _visible;
        private bool _playing;

        /// <summary>
        /// 生成直後（プール投入前）に一度だけ呼ぶ。emberMaterial が null なら粒子なしで動作する。
        /// </summary>
        public void Configure(Material dissolveMaterial, Material emberMaterial)
        {
            _dissolveMaterial = dissolveMaterial;
            _emberMaterial = emberMaterial;
            if (_srcBlock == null) _srcBlock = new MaterialPropertyBlock();
            BuildEmbers();
        }

        /// <summary>
        /// sourceRoot 以下の見た目を複製し、landingPosition まで横に回りながら吹き飛んでから蒸発する。
        /// 完了時に onDone を呼ぶ（所有者がプール返却する）。
        /// sourceRoot は呼び出し時点で有効である必要がある（Kill / Release より前に呼ぶこと）。
        /// </summary>
        public void Play(Transform sourceRoot, Vector3 landingPosition, Action<DissolveBurst> onDone)
        {
            if (_srcBlock == null) _srcBlock = new MaterialPropertyBlock();

            _onDone = onDone;
            _phase = Phase.Flight;
            _t = 0f;
            _playing = true;

            // パーツはワールド姿勢で置くので、器は等倍・無回転から始める
            transform.localScale = Vector3.one;
            transform.rotation = Quaternion.identity;
            Vector3 ground = sourceRoot != null ? sourceRoot.position : transform.position;
            transform.position = ground;

            CopyHierarchy(sourceRoot);
            SetVisible(true);   // 前回 SetVisible(false) のまま返却されているので戻す
            SetAmount(0f);

            // 回転軸を体の中心へ移してから、飛行の始点・終点を決める
            _flightFrom = PivotToCenter();
            _flightTo = landingPosition + (_flightFrom - ground);

            // 縦軸まわりの横回転（コマのように水平に回りながら飛ぶ）。
            _spinAxis = Vector3.up;
        }

        /// <summary>
        /// 器の原点をモデルの中心へ移す。見た目は動かさずに回転軸だけを体の中心へ持ってくる。
        /// あわせて放物線の高さをモデルの大きさから決める。
        /// </summary>
        private Vector3 PivotToCenter()
        {
            if (_activeParts == 0) return transform.position;

            var b = _parts[0].Renderer.bounds;
            for (int i = 1; i < _activeParts; i++) b.Encapsulate(_parts[i].Renderer.bounds);

            // 器は無回転・等倍なので、ワールドの差分をそのままローカルの差分として引ける
            Vector3 delta = b.center - transform.position;
            for (int i = 0; i < _activeParts; i++) _parts[i].Tr.localPosition -= delta;
            transform.position = b.center;

            _arcHeight = b.size.y * ArcRatio;
            return b.center;
        }

        // ---------------------------------------------------------------- 複製

        private void CopyHierarchy(Transform sourceRoot)
        {
            _activeParts = 0;
            if (sourceRoot == null) { HideUnusedParts(); return; }

            _sources.Clear();
            sourceRoot.GetComponentsInChildren(true, _sources);

            for (int i = 0; i < _sources.Count; i++)
            {
                var src = _sources[i];
                if (src == null || !src.enabled || !src.gameObject.activeInHierarchy) continue;
                if (!(src is MeshRenderer || src is SkinnedMeshRenderer)) continue;

                var part = GetPart(_activeParts);
                var mesh = ResolveMesh(src, part);
                if (mesh == null) continue;

                var st = src.transform;
                part.Tr.SetPositionAndRotation(st.position, st.rotation);
                part.Tr.localScale = src is SkinnedMeshRenderer ? Vector3.one : st.lossyScale;

                part.Filter.sharedMesh = mesh;
                ApplyMaterials(part, src, mesh);
                part.Tr.gameObject.SetActive(true);

                _activeParts++;
            }
            HideUnusedParts();
        }

        private Mesh ResolveMesh(Renderer src, Part part)
        {
            if (src is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) return null;
                if (part.Bake == null)
                {
                    part.Bake = new Mesh { name = "DissolveBurstBake" };
                    part.Bake.MarkDynamic();
                }
                // useScale:true でワールドスケールが焼き込まれるため、こちら側は等倍で置く。
                skinned.BakeMesh(part.Bake, true);
                return part.Bake;
            }
            var mf = src.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        // サブメッシュごとに元マテリアルの色を引き継ぐ。
        // FBX はパーツ内でも赤／金／黒と分かれているため、まとめて 1 色にすると別物に見える。
        private void ApplyMaterials(Part part, Renderer src, Mesh mesh)
        {
            int sub = Mathf.Max(1, mesh.subMeshCount);
            if (part.SubMeshes != sub)
            {
                part.SubMeshes = sub;
                part.Materials = new Material[sub];
                part.Blocks = new MaterialPropertyBlock[sub];
                for (int i = 0; i < sub; i++) part.Blocks[i] = new MaterialPropertyBlock();
            }
            for (int i = 0; i < sub; i++) part.Materials[i] = _dissolveMaterial;
            part.Renderer.sharedMaterials = part.Materials;

            var srcMats = src.sharedMaterials;
            for (int i = 0; i < sub; i++)
            {
                Color tint = Color.white;
                Texture map = null;

                var sm = i < srcMats.Length ? srcMats[i] : null;
                if (sm != null)
                {
                    if (sm.HasProperty(IdBaseColor)) tint = sm.GetColor(IdBaseColor);
                    if (sm.HasProperty(IdBaseMap)) map = sm.GetTexture(IdBaseMap);
                }

                // MPB で per-instance に色を上書きしている場合はそちらを優先（ADR-16 の tint）。
                src.GetPropertyBlock(_srcBlock, i);
                if (!_srcBlock.isEmpty)
                {
                    var c = _srcBlock.GetColor(IdBaseColor);
                    if (c.a > 0f) tint = c;
                }

                var block = part.Blocks[i];
                block.Clear();
                block.SetColor(IdBaseColor, tint);
                if (map != null) block.SetTexture(IdBaseMap, map);
            }
        }

        private Part GetPart(int index)
        {
            while (_parts.Count <= index)
            {
                var go = new GameObject("Part" + _parts.Count);
                go.transform.SetParent(transform, false);
                var part = new Part
                {
                    Tr = go.transform,
                    Filter = go.AddComponent<MeshFilter>(),
                    Renderer = go.AddComponent<MeshRenderer>()
                };
                part.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _parts.Add(part);
            }
            return _parts[index];
        }

        private void HideUnusedParts()
        {
            for (int i = _activeParts; i < _parts.Count; i++)
                _parts[i].Tr.gameObject.SetActive(false);
        }

        private void SetAmount(float v)
        {
            for (int p = 0; p < _activeParts; p++)
            {
                var part = _parts[p];
                for (int i = 0; i < part.SubMeshes; i++)
                {
                    part.Blocks[i].SetFloat(IdDissolve, v);
                    part.Renderer.SetPropertyBlock(part.Blocks[i], i);
                }
            }
        }

        private void SetVisible(bool on)
        {
            for (int p = 0; p < _activeParts; p++) _parts[p].Renderer.enabled = on;
            _visible = on;
        }

        // ---------------------------------------------------------------- 粒子

        private void BuildEmbers()
        {
            if (_embers != null || _emberMaterial == null) return;

            // AddComponent 直後の ParticleSystem は playOnAwake=true で即再生され、
            // 再生中は main.duration を変更できない（Unity が実行時エラーを出す）。
            // 組み立てが済むまで GameObject を無効にしておき、自動再生自体を起こさせない。
            var go = new GameObject("Embers");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _embers = go.AddComponent<ParticleSystem>();

            var main = _embers.main;
            main.playOnAwake = false;
            main.duration = Duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(EmberLifeMin, EmberLifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.gravityModifier = -0.08f;                 // 負の重力＝立ち上る
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = EmberMaxParticles;
            main.stopAction = ParticleSystemStopAction.None;

            var em = _embers.emission;
            em.rateOverTime = EmberRate;

            // 発生源は Play のたびに「最も大きいパーツ」へ差し替える。
            var shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;
            shape.randomDirectionAmount = 0.35f;

            var vol = _embers.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            // x/y/z は同じ MinMaxCurve モードで揃える必要がある
            //（揃っていないと "Particle Velocity curves must all be in the same mode" が出る）。
            vol.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vol.y = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            vol.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var noise = _embers.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.12f);
            noise.frequency = 0.7f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var col = _embers.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sol = _embers.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f)));

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = _emberMaterial;
            pr.sortMode = ParticleSystemSortMode.None;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;

            // 設定完了。playOnAwake=false なので、有効化しても勝手には再生されない。
            go.SetActive(true);
        }

        private void StartEmbers()
        {
            if (_embers == null) return;

            // 一番体積の大きいパーツの表面から出す（全パーツに付けると数が増えすぎる）。
            Part biggest = null;
            float best = -1f;
            for (int p = 0; p < _activeParts; p++)
            {
                var s = _parts[p].Renderer.bounds.size;
                float v = s.x * s.y * s.z;
                if (v > best) { best = v; biggest = _parts[p]; }
            }

            var shape = _embers.shape;
            if (biggest != null)
            {
                _embers.transform.SetPositionAndRotation(biggest.Tr.position, biggest.Tr.rotation);
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = biggest.Renderer;
                shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
            }

            // プール再利用時に前回のバーストの粒子が残っていることがあるので、必ず消してから始める。
            _embers.Clear(true);
            _embers.Play(true);
        }

        // ---------------------------------------------------------------- 進行

        private void Update()
        {
            if (!_playing) return;

            _t += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Flight:
                {
                    float k = FlightTime > 0f ? Mathf.Clamp01(_t / FlightTime) : 1f;
                    float e = 1f - Mathf.Pow(1f - k, 3f); // ease-out: 押し出されて減速し、止まる

                    Vector3 pos = Vector3.Lerp(_flightFrom, _flightTo, e);
                    pos.y += _arcHeight * Mathf.Sin(Mathf.PI * k);
                    transform.position = pos;
                    transform.rotation = Quaternion.AngleAxis(SpinDegrees * e, _spinAxis);

                    if (k < 1f) return;

                    // 飛びきった。ここから溶かし始める。
                    _phase = Phase.Dissolve;
                    _t = 0f;
                    StartEmbers();
                    return;
                }

                case Phase.Dissolve:
                {
                    if (_t < Duration)
                    {
                        SetAmount(Mathf.Clamp01(_t / Duration));
                        return;
                    }
                    // 溶けきった。メッシュは消すが、粒子が漂いきるまでは生かしておく。
                    SetAmount(1f);
                    SetVisible(false);
                    _phase = Phase.Tail;
                    _t = 0f;
                    return;
                }

                default:
                {
                    if (_t < EmberTail) return;

                    _playing = false;
                    if (_embers != null) _embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);

                    var cb = _onDone;
                    _onDone = null;
                    cb?.Invoke(this); // ObjectPool.Release → SetActive(false) で Update 停止
                    return;
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].Bake != null) Destroy(_parts[i].Bake);
        }
    }
}
