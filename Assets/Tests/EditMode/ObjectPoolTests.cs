using System.Collections.Generic;
using KakigoriTD.Factory;
using NUnit.Framework;
using UnityEngine;

namespace KakigoriTD.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="ObjectPool{T}"/>. These create real GameObjects in the editor
    /// scene, so every created object is tracked and destroyed in <see cref="TearDown"/> to keep the
    /// scene clean (a dirty scene aborts tests-run).
    /// </summary>
    public class ObjectPoolTests
    {
        // Dummy component so the pool has a concrete Component type to recycle.
        private class PoolDummy : MonoBehaviour { }

        // Every GameObject created during a test is recorded here for deterministic teardown.
        private List<GameObject> _created;

        // Counts how many times the create delegate actually minted a new instance.
        private int _createCount;

        [SetUp]
        public void SetUp()
        {
            _created = new List<GameObject>();
            _createCount = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _created.Clear();
        }

        // Helper create-func: mints a PoolDummy, records its GameObject for teardown, and counts the call.
        private PoolDummy CreateDummy()
        {
            var go = new GameObject("PoolDummy");
            _created.Add(go);
            _createCount++;
            return go.AddComponent<PoolDummy>();
        }

        [Test]
        public void Get_WhenStockEmpty_CreatesNewInstance()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy);

            // Act
            var instance = pool.Get();

            // Assert
            Assert.That(instance, Is.Not.Null);
            Assert.That(_createCount, Is.EqualTo(1));
        }

        [Test]
        public void Get_AfterRelease_ReusesSameInstance()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy);
            var a = pool.Get();
            pool.Release(a);

            // Act
            var b = pool.Get();

            // Assert
            Assert.That(b, Is.SameAs(a));
        }

        [Test]
        public void Get_ActivatesInstance()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy);

            // Act
            var instance = pool.Get();

            // Assert
            Assert.That(instance.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void Release_DeactivatesInstance()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy);
            var instance = pool.Get();

            // Act
            pool.Release(instance);

            // Assert
            Assert.That(instance.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void Prewarm_StocksRequestedCountInactive()
        {
            // Arrange / Act
            var pool = new ObjectPool<PoolDummy>(CreateDummy, prewarm: 3);

            // Assert
            Assert.That(pool.CountInactive, Is.EqualTo(3));
            foreach (var go in _created)
            {
                Assert.That(go.activeSelf, Is.False);
            }
        }

        [Test]
        public void Get_FromPrewarmed_DoesNotCreateNew()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy, prewarm: 2);
            int createCountAfterPrewarm = _createCount;

            // Act
            pool.Get();
            pool.Get();

            // Assert: both Gets reused prewarmed instances, so create was not invoked again.
            Assert.That(_createCount, Is.EqualTo(createCountAfterPrewarm));
        }

        [Test]
        public void Release_IncreasesInactiveCount()
        {
            // Arrange
            var pool = new ObjectPool<PoolDummy>(CreateDummy);
            var instance = pool.Get();
            int before = pool.CountInactive;

            // Act
            pool.Release(instance);

            // Assert
            Assert.That(pool.CountInactive, Is.EqualTo(before + 1));
        }
    }
}
