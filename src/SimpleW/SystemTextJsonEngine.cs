using System.Buffers;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;


namespace SimpleW {

    /// <summary>
    /// Implement with IJsonEngine using System.Text.Json
    /// </summary>
    public class SystemTextJsonEngine : IJsonEngine {

        /// <summary>
        /// Options Builder
        /// </summary>
        private readonly Func<JsonAction, JsonSerializerOptions?>? OptionsBuilder;

        /// <summary>
        /// Build
        /// </summary>
        /// <returns></returns>
        private JsonSerializerOptions? Build(JsonAction action) => OptionsBuilder?.Invoke(action);

        #region cache

        /// <summary>
        /// Cache Options for Serialize method
        /// </summary>
        private JsonSerializerOptions? SerializeOptionsCache;

        /// <summary>
        /// Cache Options for Deserialize method
        /// </summary>
        private JsonSerializerOptions? DeserializeOptionsCache;

        /// <summary>
        /// Cache Options for DeserializeAnonymous method
        /// </summary>
        private JsonSerializerOptions? DeserializeAnonymousOptionsCache;

        #endregion cache

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="optionsBuilder">the JsonSerializerOptions options</param>
        public SystemTextJsonEngine(Func<JsonAction, JsonSerializerOptions?>? optionsBuilder = null) {
            OptionsBuilder = optionsBuilder;
            SerializeOptionsCache = Build(JsonAction.Serialize);
            DeserializeOptionsCache = Build(JsonAction.Deserialize);
            DeserializeAnonymousOptionsCache = Build(JsonAction.DeserializeAnonymous);
        }

        /// <summary>
        /// Serialize an object instance into json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        public string Serialize<T>(T value) {
            return JsonSerializer.Serialize(value, SerializeOptionsCache);
        }

        /// <summary>
        /// Serialize an object instance into json (write directly into a IBufferWriter)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer"></param>
        /// <param name="value"></param>
        public void SerializeUtf8<T>(IBufferWriter<byte> writer, T value) {
            using (Utf8JsonWriter jsonWriter = new(writer, new JsonWriterOptions { SkipValidation = true, Indented = false })) {
                JsonSerializer.Serialize(jsonWriter, value, SerializeOptionsCache);
                jsonWriter.Flush();
            }
        }

        /// <summary>
        /// Deserialize a json string into an T object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public T Deserialize<T>(string json) {
            T? value = JsonSerializer.Deserialize<T>(json, DeserializeOptionsCache);
            if (value is null) {
                throw new JsonException($"Deserialization returned null for type '{typeof(T).FullName}'. JSON might be 'null' or incompatible.");
            }
            return value;
        }

        /// <summary>
        /// Deserialize a string into an anonymous object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="model"></param>
        public T DeserializeAnonymous<T>(string json, T model) {
            T? value = JsonSerializer.Deserialize<T>(json, DeserializeAnonymousOptionsCache);
            if (value is null) {
                throw new JsonException($"Deserialization returned null for type '{typeof(T).FullName}'. JSON might be 'null' or incompatible.");
            }
            return value;
        }

        /// <summary>
        /// Populate T object instance from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="target"></param>
        /// <param name="includeProperties"></param>
        /// <param name="excludeProperties"></param>
        public void Populate<T>(string json, T target, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null) {
            if (target is null) {
                return;
            }
            T source = Deserialize<T>(json);
            FastRecursivePropertyMapper.Copy(source, target, includeProperties?.ToList(), excludeProperties?.ToList());
        }

        /// <summary>
        /// System.Text.Json Recommanded Settings for SimpleW
        /// </summary>
        /// <returns></returns>
        public static Func<JsonAction, JsonSerializerOptions?> OptionsSimpleWBuilder() {
            return action => action switch {
                JsonAction.Serialize => new JsonSerializerOptions { IncludeFields = true },
                _ => null
            };
        }

        #region mapper

        /// <summary>
        /// Fast Recursive Property Mapper with mapping include/exclude properties
        /// </summary>
        public static class FastRecursivePropertyMapper {

            /// <summary>
            /// Cache delegate
            /// </summary>
            private static readonly ConcurrentDictionary<string, Delegate> _cache = new();

            /// <summary>
            /// Main Map method
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <param name="source"></param>
            /// <param name="target"></param>
            /// <param name="includeProperties"></param>
            /// <param name="excludeProperties"></param>
            public static void Copy<T>(T source, T target, List<string>? includeProperties = null, List<string>? excludeProperties = null) {
                if (source == null || target == null) {
                    return;
                }

                // Clé de cache = type + includes + excludes (normalisés)
                includeProperties = Normalize(includeProperties);
                excludeProperties = Normalize(excludeProperties);
                string cacheKey = BuildKey(typeof(T), includeProperties, excludeProperties);

                Action<T, T> copier = (Action<T, T>)_cache.GetOrAdd(
                    cacheKey,
                    _ => BuildCopier<T>(includeProperties, excludeProperties)
                );

                copier(source, target);
            }

            private static Action<T, T> BuildCopier<T>(List<string>? include, List<string>? exclude) {
                ParameterExpression src = Expression.Parameter(typeof(T), "src");
                ParameterExpression tgt = Expression.Parameter(typeof(T), "tgt");

                Expression body = BuildAssignments(
                    src, tgt, typeof(T),
                    include, exclude, parentPath: null
                );

                return Expression.Lambda<Action<T, T>>(body, src, tgt).Compile();
            }

            private static Expression BuildAssignments(Expression srcObj, Expression tgtObj, Type currentType, List<string>? include, List<string>? exclude, string? parentPath) {
                List<Expression> exprs = new();

                foreach (var prop in currentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    if (!prop.CanRead || !prop.CanWrite || prop.GetIndexParameters().Length > 0) {
                        continue;
                    }

                    string propPath = parentPath == null ? prop.Name : parentPath + "." + prop.Name;

                    // check for include/exclude at current level
                    if (!IncludedAtThisLevel(propPath, include)) {
                        continue;
                    }
                    if (ExcludedAtThisLevel(propPath, exclude)) {
                        continue;
                    }

                    MemberExpression srcProp = Expression.Property(srcObj, prop);
                    MemberExpression tgtProp = Expression.Property(tgtObj, prop);
                    Type propType = prop.PropertyType;

                    // child = value types, string, collections/dictionnaires
                    if (IsLeaf(propType)) {
                        exprs.Add(Expression.Assign(tgtProp, srcProp));
                        continue;
                    }

                    // complexe objects (class) : go deeper

                    // if (src.Prop != null) { ... }
                    BinaryExpression srcNotNull = Expression.NotEqual(srcProp, Expression.Constant(null, propType));

                    // if target.Prop == null and can be instanciate => new()
                    Expression? ensureTargetInstance = null;
                    bool hasParameterlessCtor = propType.GetConstructor(Type.EmptyTypes) != null && prop.CanWrite;
                    if (hasParameterlessCtor) {
                        BinaryExpression tgtIsNull = Expression.Equal(tgtProp, Expression.Constant(null, propType));
                        BinaryExpression assignNew = Expression.Assign(tgtProp, Expression.New(propType));
                        ensureTargetInstance = Expression.IfThen(tgtIsNull, assignNew);
                    }

                    // compute includes/excludes child
                    List<string>? childIncludes = ComputeChildIncludes(propPath, include, propType);
                    List<string>? childExcludes = ComputeChildExcludes(propPath, exclude, propType);

                    Expression nested = BuildAssignments(
                        srcProp, tgtProp, propType,
                        childIncludes, childExcludes, propPath
                    );

                    // if can't create, don't go deeper if target is null
                    if (!hasParameterlessCtor) {
                        // if (srcProp != null && tgtProp != null) { nested }
                        BinaryExpression tgtNotNull = Expression.NotEqual(tgtProp, Expression.Constant(null, propType));
                        BinaryExpression bothNotNull = Expression.AndAlso(srcNotNull, tgtNotNull);
                        exprs.Add(Expression.IfThen(bothNotNull, nested));
                    }
                    else {
                        // if (srcProp != null) { if (tgtProp == null) tgtProp = new(); nested }
                        exprs.Add(Expression.IfThen(
                            srcNotNull,
                            ensureTargetInstance != null ? Expression.Block(ensureTargetInstance, nested) : nested
                        ));
                    }
                }

                return Expression.Block(exprs);
            }

            #region helpers

            private static List<string>? Normalize(List<string>? items) {
                if (items == null) {
                    return null;
                }
                List<string> list = items.Where(s => !string.IsNullOrWhiteSpace(s))
                                         .Select(s => s.Trim())
                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                         .ToList();
                return list.Count == 0 ? null : list;
            }

            /// <summary>
            /// return True if included
            /// </summary>
            /// <param name="propPath"></param>
            /// <param name="include"></param>
            /// <returns></returns>
            private static bool IncludedAtThisLevel(string propPath, List<string>? include) {
                if (include == null || include.Count == 0) {
                    return true;
                }
                return include.Any(s =>
                    s.Equals(propPath, StringComparison.OrdinalIgnoreCase)
                    || propPath.StartsWith(s + ".", StringComparison.OrdinalIgnoreCase)
                    || (IsDirectParent(propPath, s) && s.Equals(GetFirstSegment(propPath), StringComparison.OrdinalIgnoreCase))
                );
            }

            /// <summary>
            /// Return true if excluded
            /// </summary>
            /// <param name="propPath"></param>
            /// <param name="exclude"></param>
            /// <returns></returns>
            private static bool ExcludedAtThisLevel(string propPath, List<string>? exclude) {
                if (exclude == null || exclude.Count == 0) {
                    return false;
                }
                return exclude.Any(s => s.Equals(propPath, StringComparison.OrdinalIgnoreCase));
            }

            private static List<string>? ComputeChildIncludes(string parentPath, List<string>? include, Type childType) {
                if (include == null || include.Count == 0) {
                    return null;
                }

                List<string> qualified = include.Where(s => s.StartsWith(parentPath + ".", StringComparison.OrdinalIgnoreCase))
                                                .Select(s => s.Substring(parentPath.Length + 1))
                                                .ToList();

                var hasParent = include.Contains(parentPath, StringComparer.OrdinalIgnoreCase);
                if (hasParent) {
                    HashSet<string> childNames = GetProps(childType).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    List<string> unqualified = include.Where(s => !s.Contains('.')).Where(childNames.Contains).ToList();

                    if (unqualified.Count > 0) {
                        return unqualified;
                    }
                    if (qualified.Count == 0) {
                        return null;
                    }
                }

                return qualified.Count > 0 ? qualified : new List<string>(0);
            }

            private static List<string>? ComputeChildExcludes(string parentPath, List<string>? exclude, Type childType) {
                if (exclude == null || exclude.Count == 0) {
                    return null;
                }

                List<string> qualified = exclude.Where(s => s.StartsWith(parentPath + ".", StringComparison.OrdinalIgnoreCase))
                                                .Select(s => s.Substring(parentPath.Length + 1))
                                                .ToList();

                HashSet<string> childNames = GetProps(childType).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                List<string> unqualified = exclude.Where(s => !s.Contains('.')).Where(childNames.Contains).ToList();

                if (qualified.Count == 0 && unqualified.Count == 0) {
                    return null;
                }
                qualified.AddRange(unqualified);
                return qualified;
            }

            private static bool IsDirectParent(string childPath, string maybeParent) {
                int idx = childPath.IndexOf('.');
                return idx > 0 && childPath.Substring(0, idx).Equals(maybeParent, StringComparison.OrdinalIgnoreCase);
            }

            private static string GetFirstSegment(string path) {
                int idx = path.IndexOf('.');
                return idx < 0 ? path : path.Substring(0, idx);
            }

            private static bool IsLeaf(Type t) {
                if (t == typeof(string)) {
                    return true;
                }
                if (t.IsValueType) {
                    return true; // DateOnly, TimeOnly, decimal, structs, ...
                }
                if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t)) {
                    return true; // List<>, Dict<,>, ... => copy ref
                }
                return false;
            }

            private static IEnumerable<PropertyInfo> GetProps(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                                                          .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

            private static string BuildKey(Type t, List<string>? include, List<string>? exclude) {
                string inc = include == null ? "" : string.Join(",", include);
                string exc = exclude == null ? "" : string.Join(",", exclude);
                return $"{t.AssemblyQualifiedName}|{inc}|{exc}";
            }

            #endregion helpers

        }

        #endregion mapper

    }

}