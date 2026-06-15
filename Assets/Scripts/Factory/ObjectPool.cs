using System;
using System.Collections.Generic;
using UnityEngine;

namespace KakigoriTD.Factory
{
    /// <summary>
    /// Generic pool that recycles <see cref="Component"/>-backed GameObjects instead of
    /// creating/destroying them on the hot path. Enemies and projectiles are produced at high
    /// frequency, so reusing instances avoids GC spikes that would cause frame drops (ADR-4).
    /// Idle instances are kept inactive in the stock and re-activated only when handed out.
    /// </summary>
    /// <typeparam name="T">Component type whose owning GameObject is pooled.</typeparam>
    public class ObjectPool<T> where T : Component
    {
        // Idle (inactive) instances waiting to be reused. A stack is enough: order does not matter
        // and pop/push are O(1), keeping Get/Release off the allocation path.
        private readonly Stack<T> _stock = new();

        // Factory used to mint a fresh instance when the stock is empty.
        private readonly Func<T> _create;

        /// <summary>
        /// Creates the pool.
        /// </summary>
        /// <param name="create">Factory delegate that mints a new instance. Must not be null.</param>
        /// <param name="prewarm">Number of instances to create up front to avoid first-use hitches.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="create"/> is null.</exception>
        public ObjectPool(Func<T> create, int prewarm = 0)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));

            for (int i = 0; i < prewarm; i++)
            {
                var instance = _create();
                // Correctness improvement over the doc skeleton: pooled-but-idle objects must be
                // inactive, otherwise prewarmed instances would run/render while sitting in stock.
                instance.gameObject.SetActive(false);
                _stock.Push(instance);
            }
        }

        /// <summary>
        /// Returns an active instance: reuses one from the stock if available, otherwise mints a new
        /// one via the create delegate. The returned GameObject is always active.
        /// </summary>
        public T Get()
        {
            var instance = _stock.Count > 0 ? _stock.Pop() : _create();
            instance.gameObject.SetActive(true);
            return instance;
        }

        /// <summary>
        /// Returns an instance to the pool, deactivating it so it stops running/rendering while idle.
        /// Null is ignored so callers can release defensively.
        /// </summary>
        public void Release(T o)
        {
            if (o == null) return;
            o.gameObject.SetActive(false);
            _stock.Push(o);
        }

        /// <summary>Number of idle instances currently held in the stock (observability for tests).</summary>
        public int CountInactive => _stock.Count;
    }
}
