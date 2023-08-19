using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nekres.Mistwar {
    public class AsyncCache<TKey, TValue>
    {
        private readonly Func<TKey, Task<TValue>> _valueFactory;

        private readonly ConcurrentDictionary<TKey, TaskCompletionSource<TValue>> _completionSourceCache =
            new ConcurrentDictionary<TKey, TaskCompletionSource<TValue>>();

        public AsyncCache(Func<TKey, Task<TValue>> valueFactory)
        {
            this._valueFactory = valueFactory;
        }

        public async Task<TValue> GetItem(TKey key)
        {
            var newSource = new TaskCompletionSource<TValue>();
            var currentSource = _completionSourceCache.GetOrAdd(key, newSource);

            if (currentSource != newSource)
                return await currentSource.Task;

            try {
                var result = await _valueFactory(key);
                newSource.SetResult(result);
            } catch (Exception e) {
                newSource.SetException(e);
            }

            return await newSource.Task;
        }

        public async Task<TValue> RemoveItem(TKey key) => _completionSourceCache.TryRemove(key, out TaskCompletionSource<TValue> item) ? await item.Task : default;

        public async Task Clear() {
            foreach (var key in _completionSourceCache.Keys) {
                var item = await this.RemoveItem(key);

                if (item == null) {
                    continue;
                }

                if (item is IDisposable disposable) {
                    disposable.Dispose();
                } else if (item is IEnumerable enumerable) {
                    foreach (var obj in enumerable) {
                        if (obj is IDisposable disposableObj) {
                            disposableObj.Dispose();
                        }
                    }
                }
            }
        }

        public bool ContainsKey(TKey key) => _completionSourceCache.ContainsKey(key);
    }
}
