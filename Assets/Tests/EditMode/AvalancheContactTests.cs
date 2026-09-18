using System.Collections.Generic;
using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 雪崩の粒→敵の接触選択（<see cref="AvalancheContact.FindHit"/>・ADR-32）。
    /// Covers: 半径外は当たらない、最も近い 1 体、死んだ敵は無視、高さ帯の内外、高さ無視（負の許容幅）、
    /// 前列ルールが無い（後列でも半径内なら当たる＝吸収の副作用で前から倒れるのは呼び出し側の責務）。
    /// </summary>
    public class AvalancheContactTests
    {
        private static int Hit(Vector3[] pos, bool[] alive, Vector3 particle, float radius, float heightTol = 2f)
            => AvalancheContact.FindHit(pos, alive, particle, radius, heightTol);

        [Test]
        public void OutsideRadius_ReturnsMinusOne()
        {
            var pos = new[] { new Vector3(0f, 0f, 7.5f) };
            Assert.That(Hit(pos, new[] { true }, new Vector3(1.2f, 0f, 7.5f), 1f), Is.EqualTo(-1));
        }

        [Test]
        public void InsideRadius_ReturnsIndex()
        {
            var pos = new[] { new Vector3(0f, 0f, 7.5f) };
            Assert.That(Hit(pos, new[] { true }, new Vector3(0.8f, 0f, 7.5f), 1f), Is.EqualTo(0));
        }

        [Test]
        public void PicksNearest_WhenSeveralInRadius()
        {
            var pos = new[] { new Vector3(-0.6f, 0f, 7.5f), new Vector3(0.3f, 0f, 7.5f), new Vector3(0.9f, 0f, 7.5f) };
            Assert.That(Hit(pos, new[] { true, true, true }, new Vector3(0.5f, 0f, 7.5f), 1.5f), Is.EqualTo(1));
        }

        [Test]
        public void DeadEnemy_Ignored()
        {
            var pos = new[] { new Vector3(0f, 0f, 7.5f), new Vector3(0.5f, 0f, 7.5f) };
            Assert.That(Hit(pos, new[] { false, true }, new Vector3(0f, 0f, 7.5f), 1f), Is.EqualTo(1));
        }

        [Test]
        public void BelowHeightBand_NotHit()
        {
            // 地形の下に落ちた粒（敵の足元より 3 下）は当たらない。
            var pos = new[] { new Vector3(0f, 1f, 7.5f) };
            Assert.That(Hit(pos, new[] { true }, new Vector3(0f, -2f, 7.5f), 1f, heightTol: 2f), Is.EqualTo(-1));
        }

        [Test]
        public void WithinHeightBand_Hit()
        {
            var pos = new[] { new Vector3(0f, 1f, 7.5f) };
            Assert.That(Hit(pos, new[] { true }, new Vector3(0f, 2.5f, 7.5f), 1f, heightTol: 2f), Is.EqualTo(0));
        }

        [Test]
        public void NegativeHeightTolerance_IgnoresHeight()
        {
            var pos = new[] { new Vector3(0f, 1f, 7.5f) };
            Assert.That(Hit(pos, new[] { true }, new Vector3(0f, -50f, 7.5f), 1f, heightTol: -1f), Is.EqualTo(0));
        }

        [Test]
        public void NoFrontRowRule_BackRowHitIfInRadius()
        {
            // 前列(7.5)と後列(9.3)。粒が後列の真横にあれば後列に当たる（前列ルールを持たない）。
            var pos = new[] { new Vector3(0f, 0f, 7.5f), new Vector3(0f, 0f, 9.3f) };
            Assert.That(Hit(pos, new[] { true, true }, new Vector3(0.3f, 0f, 9.3f), 1f), Is.EqualTo(1));
        }
    }
}
