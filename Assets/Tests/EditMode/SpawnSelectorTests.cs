using NUnit.Framework;
using KakigoriTD.Core;

namespace KakigoriTD.Tests.EditMode
{
    public class SpawnSelectorTests
    {
        // ---- 変装 -------------------------------------------------------

        [Test]
        public void Select_RollBelowDisguiseChance_ReturnsDisguised()
        {
            var result = SpawnSelector.Select(allyChance: 0f, disguiseChance: 0.2f,
                hasAlly: false, hasDisguise: true, roll: 0.1f);
            Assert.That(result, Is.EqualTo(SpawnKind.Disguised));
        }

        [Test]
        public void Select_HasDisguiseFalse_NeverReturnsDisguised()
        {
            // disguise 種別が設定されていなければ roll が 0 でも Enemy か Ally
            var result = SpawnSelector.Select(allyChance: 0f, disguiseChance: 0.9f,
                hasAlly: false, hasDisguise: false, roll: 0.0f);
            Assert.That(result, Is.Not.EqualTo(SpawnKind.Disguised));
        }

        [Test]
        public void Select_DisguiseChanceZero_NeverReturnsDisguised()
        {
            var result = SpawnSelector.Select(allyChance: 0f, disguiseChance: 0f,
                hasAlly: false, hasDisguise: true, roll: 0.0f);
            Assert.That(result, Is.Not.EqualTo(SpawnKind.Disguised));
        }

        // ---- 味方 -------------------------------------------------------

        [Test]
        public void Select_RollBelowAllyChance_ReturnsAlly()
        {
            // disguise 抽選を外れた roll=0.15、allyChance=0.3 → Ally
            var result = SpawnSelector.Select(allyChance: 0.3f, disguiseChance: 0.05f,
                hasAlly: true, hasDisguise: true, roll: 0.15f);
            Assert.That(result, Is.EqualTo(SpawnKind.Ally));
        }

        [Test]
        public void Select_HasAllyFalse_NeverReturnsAlly()
        {
            var result = SpawnSelector.Select(allyChance: 0.9f, disguiseChance: 0f,
                hasAlly: false, hasDisguise: false, roll: 0.0f);
            Assert.That(result, Is.Not.EqualTo(SpawnKind.Ally));
        }

        [Test]
        public void Select_AllyChanceZero_NeverReturnsAlly()
        {
            var result = SpawnSelector.Select(allyChance: 0f, disguiseChance: 0f,
                hasAlly: true, hasDisguise: false, roll: 0.0f);
            Assert.That(result, Is.Not.EqualTo(SpawnKind.Ally));
        }

        // ---- 敵（フォールスルー）-----------------------------------------

        [Test]
        public void Select_RollAboveAllChances_ReturnsEnemy()
        {
            var result = SpawnSelector.Select(allyChance: 0.2f, disguiseChance: 0.2f,
                hasAlly: true, hasDisguise: true, roll: 0.9f);
            Assert.That(result, Is.EqualTo(SpawnKind.Enemy));
        }

        [Test]
        public void Select_AllChancesZero_AlwaysReturnsEnemy()
        {
            // Wave1/2 回帰: chance=0 同士では常に Enemy
            for (int i = 0; i <= 10; i++)
            {
                float roll = i * 0.1f;
                var result = SpawnSelector.Select(allyChance: 0f, disguiseChance: 0f,
                    hasAlly: false, hasDisguise: false, roll: roll);
                Assert.That(result, Is.EqualTo(SpawnKind.Enemy),
                    $"roll={roll} should return Enemy");
            }
        }

        // ---- 優先順位 ----------------------------------------------------

        [Test]
        public void Select_RollHitsDisguise_DisguiseTakesPriorityOverAlly()
        {
            // roll=0.05 は disguiseChance=0.2 と allyChance=0.2 の両方に入る
            // → 変装が優先されるべき
            var result = SpawnSelector.Select(allyChance: 0.2f, disguiseChance: 0.2f,
                hasAlly: true, hasDisguise: true, roll: 0.05f);
            Assert.That(result, Is.EqualTo(SpawnKind.Disguised));
        }

        [Test]
        public void Select_HasDisguiseFalseRollHitsAllyChance_ReturnsAlly()
        {
            // hasDisguise=false で変装スキップ → allyChance 帯なら Ally
            var result = SpawnSelector.Select(allyChance: 0.3f, disguiseChance: 0.5f,
                hasAlly: true, hasDisguise: false, roll: 0.1f);
            Assert.That(result, Is.EqualTo(SpawnKind.Ally));
        }
    }
}
