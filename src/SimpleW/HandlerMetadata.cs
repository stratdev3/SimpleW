namespace SimpleW {

    /// <summary>
    /// Marker interface for custom handler metadata.
    /// Implement this on attributes that should be collected at route registration time.
    /// </summary>
    public interface IHandlerMetadata {
    }

    /// <summary>
    /// Immutable collection of metadata attached to the matched handler.
    /// </summary>
    public sealed class HandlerMetadataCollection {

        /// <summary>
        /// Shared empty metadata collection.
        /// </summary>
        public static HandlerMetadataCollection Empty { get; } = new(Array.Empty<IHandlerMetadata>());

        /// <summary>
        /// Metadata items stored for the current handler.
        /// </summary>
        private readonly IHandlerMetadata[] _items;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="items"></param>
        internal HandlerMetadataCollection(IHandlerMetadata[] items) {
            _items = items ?? throw new ArgumentNullException(nameof(items));
        }

        /// <summary>
        /// Number of metadata items attached to the handler.
        /// </summary>
        public int Count => _items.Length;

        /// <summary>
        /// Returns true when at least one metadata item of the requested type exists.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public bool Has<T>() where T : class, IHandlerMetadata {
            for (int i = 0; i < _items.Length; i++) {
                if (_items[i] is T) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the most specific metadata item of the requested type.
        /// When both class and method metadata exist, method metadata wins.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T? Get<T>() where T : class, IHandlerMetadata {
            for (int i = _items.Length - 1; i >= 0; i--) {
                if (_items[i] is T typed) {
                    return typed;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all metadata items of the requested type in declaration order.
        /// Class metadata comes before method metadata.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public IReadOnlyList<T> GetAll<T>() where T : class, IHandlerMetadata {
            if (_items.Length == 0) {
                return Array.Empty<T>();
            }

            T? first = null;
            List<T>? many = null;

            // we are optimistic on the fact that most a of the time
            // it should have one attribut of a type
            for (int i = 0; i < _items.Length; i++) {
                if (_items[i] is not T typed) {
                    continue;
                }

                if (first == null) {
                    first = typed;
                    continue;
                }

                many ??= new List<T>(4) { first }; // 4 items at start should be enougth for most of use cases
                many.Add(typed);
            }

            if (many != null) {
                return many;
            }

            if (first != null) {
                return [first];
            }

            return Array.Empty<T>();
        }

    }

}
