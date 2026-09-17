using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// 湧き種別の抽選（ADR-30: 通常 / 爆発兵 / 盾兵）。
    /// 確率帯は連続した区間として扱う（爆発兵 → 盾兵 → 通常）。
    /// </summary>
    public class SpawnSelectorTests
    {
        [Test]
        public void Select_RollBelowBomberChance_ReturnsBomber()
        {
            var result = SpawnSelector.Select(bomberChance: 0.2f, shieldChance: 0.2f, roll: 0.1f);
            Assert.That(result, Is.EqualTo(SpawnKind.Bomber));
        }

        [Test]
        public void Select_RollInShieldBand_ReturnsShield()
        {
            // 爆発兵 [0, 0.2) / 盾兵 [0.2, 0.4) / 通常 [0.4, 1)
            var result = SpawnSelector.Select(bomberChance: 0.2f, shieldChance: 0.2f, roll: 0.3f);
            Assert.That(result, Is.EqualTo(SpawnKind.Shield));
        }

        [Test]
        public void Select_RollAboveBothBands_ReturnsNormal()
        {
            var result = SpawnSelector.Select(bomberChance: 0.2f, shieldChance: 0.2f, roll: 0.95f);
            Assert.That(result, Is.EqualTo(SpawnKind.Normal));
        }

        [Test]
        public void Select_BomberChanceZero_NeverReturnsBomber()
        {
            var result = SpawnSelector.Select(bomberChance: 0f, shieldChance: 0.5f, roll: 0f);
            Assert.That(result, Is.Not.EqualTo(SpawnKind.Bomber));
        }

        [Test]
        public void Select_BothChancesZero_AlwaysNormal()
        {
            Assert.That(SpawnSelector.Select(0f, 0f, 0f), Is.EqualTo(SpawnKind.Normal));
            Assert.That(SpawnSelector.Select(0f, 0f, 0.999f), Is.EqualTo(SpawnKind.Normal));
        }

        [Test]
        public void Select_BandBoundaries_AreHalfOpen()
        {
            // 境界値: roll == bomberChance は盾兵側、roll == bomber+shield は通常側。
            Assert.That(SpawnSelector.Select(0.2f, 0.2f, 0.2f), Is.EqualTo(SpawnKind.Shield));
            Assert.That(SpawnSelector.Select(0.2f, 0.2f, 0.4f), Is.EqualTo(SpawnKind.Normal));
        }

        [Test]
        public void Select_FullBomberChance_AlwaysBomber()
        {
            Assert.That(SpawnSelector.Select(1f, 0f, 0.999f), Is.EqualTo(SpawnKind.Bomber));
        }
    }
}
