using System.Runtime.CompilerServices;


namespace SimpleW {

    /// <summary>
    /// Per-request storage shared across middlewares/handlers.
    /// Lazy-created from HttpSession.Bag
    /// </summary>
    public sealed class HttpBag {

        private readonly Dictionary<string, object?> _items = new(StringComparer.Ordinal);

        /// <summary>
        /// Number of items currently stored
        /// </summary>
        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _items.Count;
        }

        /// <summary>
        /// Raw object access
        /// </summary>
        public object? this[string key] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _items[key];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _items[key] = value;
        }

        /// <summary>
        /// Store or replace a value
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set<T>(string key, T value) {
            ArgumentNullException.ThrowIfNull(key);
            _items[key] = value;
        }

        /// <summary>
        /// Returns true if key exists
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(string key) {
            ArgumentNullException.ThrowIfNull(key);
            return _items.ContainsKey(key);
        }

        /// <summary>
        /// Try get a typed value
        /// Returns false when :
        /// - key does not exist
        /// - stored value type does not match T
        /// - stored value is null and T is a non-nullable value type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet<T>(string key, out T? value) {
            ArgumentNullException.ThrowIfNull(key);

            if (_items.TryGetValue(key, out object? raw)) {
                if (raw is T typed) {
                    value = typed;
                    return true;
                }

                // null is valid only for ref type or nullable value type
                if (raw is null && default(T) is null) {
                    value = default;
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Get a typed value or throw if missing / invalid type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get<T>(string key) {
            if (TryGet<T>(key, out T? value)) {
                return value!;
            }

            throw new KeyNotFoundException($"Bag key '{key}' was not found or is not assignable to '{typeof(T).FullName}'.");
        }

        /// <summary>
        /// Get a typed value or return defaultValue if missing / invalid type
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T? GetOrDefault<T>(string key, T? defaultValue = default) {
            return TryGet<T>(key, out T? value) ? value : defaultValue;
        }

        /// <summary>
        /// Remove a key
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Remove(string key) {
            ArgumentNullException.ThrowIfNull(key);
            return _items.Remove(key);
        }

        /// <summary>
        /// Remove all values
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear() {
            if (_items.Count != 0) {
                _items.Clear();
            }
        }

    }

}
