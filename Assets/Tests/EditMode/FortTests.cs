using KakigoriTD.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="Fort"/>. Fort is a MonoBehaviour, so each test creates a real
    /// GameObject and destroys it in TearDown to keep the scene clean (a dirty scene aborts tests-run).
    /// HP is set explicitly via <see cref="Fort.Initialize(int)"/> because Awake does not run for
    /// AddComponent'd objects in EditMode.
    /// </summary>
    public class FortTests
    {
        private GameObject _go;
        private Fort _fort;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Fort");
            _fort = _go.AddComponent<Fort>();
            _fort.Initialize(100);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Initialize_SetsCurrentHpToMax()
        {
            Assert.That(_fort.CurrentHp, Is.EqualTo(100));
            Assert.That(_fort.MaxHp, Is.EqualTo(100));
            Assert.That(_fort.IsDestroyed, Is.False);
        }

        [Test]
        public void TakeDamage_SubtractsFromCurrentHp()
        {
            _fort.TakeDamage(30);

            Assert.That(_fort.CurrentHp, Is.EqualTo(70));
        }

        [Test]
        public void TakeDamage_DoesNotGoBelowZero()
        {
            _fort.TakeDamage(150);

            Assert.That(_fort.CurrentHp, Is.EqualTo(0));
            Assert.That(_fort.IsDestroyed, Is.True);
        }

        [Test]
        public void TakeDamage_RaisesOnHpChangedWithCurrentAndMax()
        {
            int observedCurrent = -1;
            int observedMax = -1;
            _fort.OnHpChanged += (current, max) => { observedCurrent = current; observedMax = max; };

            _fort.TakeDamage(40);

            Assert.That(observedCurrent, Is.EqualTo(60));
            Assert.That(observedMax, Is.EqualTo(100));
        }

        [Test]
        public void TakeDamage_RaisesOnDestroyedWhenHpReachesZero()
        {
            int destroyedCount = 0;
            _fort.OnDestroyed += () => destroyedCount++;

            _fort.TakeDamage(100);

            Assert.That(_fort.IsDestroyed, Is.True);
            Assert.That(destroyedCount, Is.EqualTo(1));
        }

        [Test]
        public void TakeDamage_AfterDestroyed_IsIgnoredAndDoesNotRaiseOnDestroyedAgain()
        {
            int destroyedCount = 0;
            _fort.OnDestroyed += () => destroyedCount++;
            _fort.TakeDamage(100); // -> 0, fires once

            _fort.TakeDamage(50); // already destroyed, ignored

            Assert.That(_fort.CurrentHp, Is.EqualTo(0));
            Assert.That(destroyedCount, Is.EqualTo(1));
        }

        [Test]
        public void TakeDamage_WithNonPositiveAmount_IsIgnored()
        {
            bool changed = false;
            _fort.OnHpChanged += (_, __) => changed = true;

            _fort.TakeDamage(0);
            _fort.TakeDamage(-10);

            Assert.That(_fort.CurrentHp, Is.EqualTo(100));
            Assert.That(changed, Is.False);
        }

        [Test]
        public void Normalized_ReflectsRemainingFraction()
        {
            _fort.TakeDamage(25);

            Assert.That(_fort.Normalized, Is.EqualTo(0.75f).Within(0.0001f));
        }
    }
}
