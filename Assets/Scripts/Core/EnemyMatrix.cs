using System.Collections.Generic;

namespace KakigoriTD.Core
{
    /// <summary>
    /// 1 谷の敵マトリックス（行リスト）。純ロジック（ADR-8）— UnityEngine 非依存。
    /// 行は front(0) … back(N-1) の順。雪崩は front（山頂側）を消し、
    /// 新規湧きは back（麓側・最奥）に積む。先頭行が消えると次行が繰り上がるため、
    /// 削り続けると手前から奥へ進む（深さ制御）。
    /// 型 <typeparamref name="T"/> には上位層（Gameplay の Enemy 等）を充てる。
    /// </summary>
    public class EnemyMatrix<T>
    {
        private readonly List<List<T>> _rows = new List<List<T>>();
        private static readonly IReadOnlyList<T> EmptyRow = new List<T>();

        /// <param name="rowWidth">1 行の最大体数（rowWidth=3）。1 未満は 1 に丸める。</param>
        public EnemyMatrix(int rowWidth = 3)
        {
            RowWidth = rowWidth < 1 ? 1 : rowWidth;
        }

        /// <summary>1 行に並べられる最大体数。</summary>
        public int RowWidth { get; }

        /// <summary>現在の行数。</summary>
        public int RowCount => _rows.Count;

        /// <summary>マトリックスが空か。</summary>
        public bool IsEmpty => _rows.Count == 0;

        /// <summary>全体の体数。</summary>
        public int Count
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _rows.Count; i++) n += _rows[i].Count;
                return n;
            }
        }

        /// <summary>先頭行（山頂側＝次に雪崩で消える行）。空なら空リスト。</summary>
        public IReadOnlyList<T> FrontRow => _rows.Count > 0 ? _rows[0] : EmptyRow;

        /// <summary>
        /// 新規湧きを最奥（麓側）に積む。最奥行が満杯（RowWidth）なら新しい行を作る。
        /// </summary>
        public void AddToBack(T item)
        {
            if (_rows.Count == 0 || _rows[_rows.Count - 1].Count >= RowWidth)
                _rows.Add(new List<T>(RowWidth));
            _rows[_rows.Count - 1].Add(item);
        }

        /// <summary>
        /// 先頭行を消して、その中身（消えた敵）を返す。次行が先頭に繰り上がる。
        /// 空なら空リストを返す（雪崩ヒット 1 回 = 1 行消去）。
        /// </summary>
        public IReadOnlyList<T> RemoveFrontRow()
        {
            if (_rows.Count == 0) return EmptyRow;
            var front = _rows[0];
            _rows.RemoveAt(0);
            return front;
        }

        /// <summary>
        /// 全行を消して全要素を返す（Wave 終了時の残敵消去用）。返却後はマトリックスが空。
        /// </summary>
        public IReadOnlyList<T> RemoveAll()
        {
            var all = new List<T>(Count);
            for (int i = 0; i < _rows.Count; i++) all.AddRange(_rows[i]);
            _rows.Clear();
            return all;
        }
    }
}
