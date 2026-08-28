using UnityEngine;

namespace KakigoriTD.Gameplay
{
    /// <summary>
    /// 変装敵。<see cref="Enemy"/> を継承し同一 <c>EnemyMatrix&lt;Enemy&gt;</c> に多態で保持する（ADR-19）。
    /// 青い tint（味方と同色）で擬態するが、tell マーカー（小さな暗色キューブ）を持ち注意すれば見分けられる。
    /// 発射は基底 <see cref="Enemy.Update"/> のまま（factory≠null なので撃つ）。
    /// 自走退場しない（AllyNPC と異なり lifetime カウントを持たない）。
    /// 雪崩で撃破されても <c>EnemyField.OnAllyCaught</c> は発火しない（無ペナルティ）。
    /// </summary>
    public class DisguisedEnemy : Enemy
    {
        private GameObject _tellMarker;

        protected override void OnInitialized()
        {
            if (_tellMarker == null) BuildTellMarker();
        }

        // 小さな暗色キューブを敵キューブの角に置く（tell = 見た目差。注意すれば識別可能）。
        private void BuildTellMarker()
        {
            _tellMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _tellMarker.name = "TellMarker";
            _tellMarker.transform.SetParent(transform, false);
            // 親スケール 0.5× のため localPos は 2倍で計算。
            // 親前面(world z=+0.25)より手前(world z=+0.30): localZ=0.6 → world 0.5*0.6=0.30
            // 親外角(world xy=±0.25)より外: localXY=0.7 → world 0.5*0.7=0.35
            _tellMarker.transform.localPosition = new Vector3(0.7f, 0.7f, 0.6f);
            _tellMarker.transform.localScale = Vector3.one * 0.3f;

            var col = _tellMarker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.08f, 0.04f, 0.18f, 1f));
            _tellMarker.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
