using System.Reflection;
using System.Globalization;


namespace SimpleW {

    /// <summary>
    /// HttpRouteExecutorFactory
    /// </summary>
    internal static class HttpRouteExecutorFactory {

        /// <summary>
        /// Create a HttpRouteExecutor from a Delegate
        /// Note : the reflection slow code is only called once
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        public static HttpRouteExecutor Create(Delegate handler) {
            ArgumentNullException.ThrowIfNull(handler);

            // get handler info (only called once)
            MethodInfo method = handler.Method;
            ParameterInfo[] parameters = method.GetParameters();
            HandlerReturnKind kind = GetReturnKind(method.ReturnType);

            // build the final executor that will be cached in a Route
            return async (session, handlerResult) => {
                object?[] args = BindArguments(session, parameters);

                object? result;
                try {
                    result = handler.DynamicInvoke(args);
                }
                catch (TargetInvocationException tie) {
                    throw tie.InnerException ?? tie;
                }

                switch (kind) {
                    case HandlerReturnKind.Void:
                        return;

                    case HandlerReturnKind.SyncResult:
                        if (result is not null) {
                            await handlerResult(session, result).ConfigureAwait(false);
                        }
                        return;

                    case HandlerReturnKind.TaskNoResult:
                        if (result is Task t1) {
                            await t1.ConfigureAwait(false);
                        }
                        return;

                    case HandlerReturnKind.TaskWithResult:
                        if (result is Task t2) {
                            await AwaitTaskWithResultAsync(t2, session, handlerResult).ConfigureAwait(false);
                        }
                        return;

                    case HandlerReturnKind.ValueTaskNoResult:
                        if (result is ValueTask vt1) {
                            await vt1.ConfigureAwait(false);
                        }
                        return;

                    case HandlerReturnKind.ValueTaskWithResult:
                        await AwaitValueTaskWithResultAsync(result, session, handlerResult).ConfigureAwait(false);
                        return;
                }
            };
        }

        #region return

        /// <summary>
        /// Type of Handlers
        /// </summary>
        private enum HandlerReturnKind {
            Void,
            SyncResult,
            TaskNoResult,
            TaskWithResult,
            ValueTaskNoResult,
            ValueTaskWithResult
        }

        /// <summary>
        /// Get the HandlerReturnKind from a Method.ReturnType
        /// </summary>
        /// <param name="returnType"></param>
        /// <returns></returns>
        private static HandlerReturnKind GetReturnKind(Type returnType) {

            if (returnType == typeof(void)) {
                return HandlerReturnKind.Void;
            }

            if (returnType == typeof(Task)) {
                return HandlerReturnKind.TaskNoResult;
            }

            if (returnType == typeof(ValueTask)) {
                return HandlerReturnKind.ValueTaskNoResult;
            }

            if (returnType.IsGenericType) {
                Type genericDef = returnType.GetGenericTypeDefinition();
                if (genericDef == typeof(Task<>)) {
                    return HandlerReturnKind.TaskWithResult;
                }
                if (genericDef == typeof(ValueTask<>)) {
                    return HandlerReturnKind.ValueTaskWithResult;
                }
            }

            return HandlerReturnKind.SyncResult;
        }

        /// <summary>
        /// Convert a Task with Result to a ValueTask
        /// </summary>
        /// <param name="task"></param>
        /// <param name="session"></param>
        /// <param name="handlerResult"></param>
        /// <returns></returns>
        private static async ValueTask AwaitTaskWithResultAsync(Task task, HttpSession session, HttpHandlerResult handlerResult) {
            await task.ConfigureAwait(false);

            PropertyInfo? resultProp = task.GetType().GetProperty("Result");
            if (resultProp != null) {
                object? val = resultProp.GetValue(task);
                if (val is not null) {
                    await handlerResult(session, val).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Convert a ValueTask with Result to a ValueTask
        /// </summary>
        /// <param name="valueTaskObj"></param>
        /// <param name="session"></param>
        /// <param name="handlerResult"></param>
        /// <returns></returns>
        private static async ValueTask AwaitValueTaskWithResultAsync(object? valueTaskObj, HttpSession session, HttpHandlerResult handlerResult) {
            if (valueTaskObj is null) {
                return;
            }

            Type? vtType = valueTaskObj.GetType();
            MethodInfo? asTaskMethod = vtType.GetMethod("AsTask", Type.EmptyTypes);
            if (asTaskMethod == null) {
                return;
            }

            if (asTaskMethod.Invoke(valueTaskObj, null) is Task task) {
                await task.ConfigureAwait(false);
                PropertyInfo? resultProp = task.GetType().GetProperty("Result");
                if (resultProp != null) {
                    object? val = resultProp.GetValue(task);
                    if (val is not null) {
                        await handlerResult(session, val).ConfigureAwait(false);
                    }
                }
            }
        }

        #endregion return

        #region argument binding

        /// <summary>
        /// For each parameter of the handler, return a value.
        /// The value will be :
        ///     1. session if parameter is HttpSesssion (special case)
        ///     2. value of the Request Query String if exists (mapping by name)
        ///     3. default value of the parameter
        /// </summary>
        /// <param name="session"></param>
        /// <param name="parameters"></param>
        /// <returns></returns>
        private static object?[] BindArguments(HttpSession session, ParameterInfo[] parameters) {
            // no parameter
            if (parameters.Length == 0) {
                return Array.Empty<object?>();
            }

            // arguments to return
            object?[] args = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++) {
                ParameterInfo p = parameters[i];

                // 1.) HttpSession type is a special parameter
                if (p.ParameterType == typeof(HttpSession)) {
                    args[i] = session;
                    continue;
                }

                string name = p.Name ?? string.Empty;

                // 2.) value of the Request Query String
                string? raw = null;
                if (session.Request.Query is not null && !string.IsNullOrEmpty(name) && session.Request.Query.TryGetValue(name, out string? v)) {
                    raw = v;
                }

                // 3.) default value of the parameter
                if (raw is null) {
                    args[i] = GetDefaultValueForParameter(p);
                    continue;
                }

                // 2.) value of the Request Query String
                if (TryConvertFromString(raw, p.ParameterType, out object? converted)) {
                    args[i] = converted;
                }
                // 3.) default value of the parameter
                else {
                    args[i] = GetDefaultValueForParameter(p);
                }
            }

            return args;
        }

        /// <summary>
        /// Get the default value of ParameterInfo
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        private static object? GetDefaultValueForParameter(ParameterInfo param) {
            if (param.HasDefaultValue) {
                return param.DefaultValue;
            }

            Type t = param.ParameterType;
            Type? underlying = Nullable.GetUnderlyingType(t);
            if (!t.IsValueType || underlying != null) {
                return null;
            }

            return Activator.CreateInstance(t);
        }

        /// <summary>
        /// Try to convert a raw string into the target type
        /// Output the object value
        /// </summary>
        /// <param name="raw"></param>
        /// <param name="targetType"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private static bool TryConvertFromString(string raw, Type targetType, out object? value) {
            value = null;

            Type? underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null) {
                if (string.IsNullOrEmpty(raw)) {
                    value = null;
                    return true;
                }
                targetType = underlying;
            }

            if (targetType == typeof(string)) {
                value = raw;
                return true;
            }

            if (targetType == typeof(bool)) {
                if (bool.TryParse(raw, out bool b)) {
                    value = b;
                    return true;
                }
                if (raw == "0") { value = false; return true; }
                if (raw == "1") { value = true; return true; }
                return false;
            }

            NumberStyles style = NumberStyles.Any;
            CultureInfo? culture = CultureInfo.InvariantCulture;

            if (targetType == typeof(int)) {
                if (int.TryParse(raw, style, culture, out int i)) {
                    value = i;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(long)) {
                if (long.TryParse(raw, style, culture, out long l)) {
                    value = l;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(double)) {
                if (double.TryParse(raw, style, culture, out double d)) {
                    value = d;
                    return true;
                }
                return false;
            }

            if (targetType == typeof(float)) {
                if (float.TryParse(raw, style, culture, out float f)) {
                    value = f;
                    return true;
                }
                return false;
            }

            if (targetType.IsEnum) {
                try {
                    value = Enum.Parse(targetType, raw, ignoreCase: true);
                    return true;
                }
                catch {
                    return false;
                }
            }

            return false;
        }

        #endregion argument binding
    }

}
