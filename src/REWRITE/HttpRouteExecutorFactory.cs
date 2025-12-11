using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;


namespace SimpleW {

    /// <summary>
    /// HttpRouteExecutorFactory
    /// </summary>
    internal static class HttpRouteExecutorFactory {

        /// <summary>
        /// Create a HttpRouteExecutor from a Delegate
        /// Note : this slow reflection code is only called once
        ///        to create an Expression Tree of the call to Delegate
        /// </summary>
        /// <param name="handler"></param>
        /// <returns></returns>
        /// <exception cref="NotSupportedException"></exception>
        public static HttpRouteExecutor Create(Delegate handler) {
            ArgumentNullException.ThrowIfNull(handler);

            // get handler info (only called once)
            MethodInfo method = handler.Method;
            ParameterInfo[] parameters = method.GetParameters();
            HandlerReturnKind kind = GetReturnKind(method.ReturnType);

            // lambda parameters (session + handlerResult)
            ParameterExpression sessionParam = Expression.Parameter(typeof(HttpSession), "session");
            ParameterExpression handlerResultParam = Expression.Parameter(typeof(HttpHandlerResult), "handlerResult");

            // session.Request.Query
            MemberExpression? requestProp = Expression.Property(sessionParam, nameof(HttpSession.Request));
            MemberExpression? queryProp = Expression.Property(requestProp, "Query");

            // TryGetValue(string, out string)
            Type queryType = queryProp.Type;
            MethodInfo? tryGetValueMethod = queryType.GetMethod(
                "TryGetValue",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(string).MakeByRefType() },
                modifiers: null
            );

            //
            // Build handler arguments
            // For each arguments, return a value that can be
            //     1. session if parameter is HttpSesssion (special case)
            //     2. value of the Request Query String if exists (mapping by name)
            //     3. default value of the parameter
            //

            List<ParameterExpression> variables = new();
            List<Expression> argExpressions = new();
            List<Expression> body = new();

            foreach (ParameterInfo p in parameters) {

                // 1.) HttpSession type is a special parameter
                if (p.ParameterType == typeof(HttpSession)) {
                    argExpressions.Add(sessionParam);
                    continue;
                }

                // local var for final param
                ParameterExpression paramVar = Expression.Variable(p.ParameterType, p.Name ?? "arg");
                variables.Add(paramVar);

                // local var for raw string
                ParameterExpression rawVar = Expression.Variable(typeof(string), (p.Name ?? "arg") + "_raw");
                variables.Add(rawVar);

                // "default value" expression for the param
                object? defaultObj = GetDefaultValueForParameter(p);
                Expression defaultExpr;

                if (defaultObj is null && p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) == null) {
                    // non nullable value type without default -> default(T)
                    defaultExpr = Expression.Default(p.ParameterType);
                }
                else {
                    defaultExpr = Expression.Constant(defaultObj, p.ParameterType);
                }

                Expression assignExpr;
                if (tryGetValueMethod != null && p.Name is not null) {

                    // query != null
                    Expression queryNotNull = Expression.NotEqual(
                        queryProp,
                        Expression.Constant(null, queryType)
                    );

                    // query.TryGetValue("name", out rawVar)
                    Expression tryGet = Expression.Call(
                        queryProp,
                        tryGetValueMethod,
                        Expression.Constant(p.Name, typeof(string)),
                        rawVar
                    );

                    // ConvertFromStringOrDefault<T>(rawVar, defaultExpr)
                    MethodInfo convertGeneric = typeof(HttpRouteExecutorFactory)
                                                    .GetMethod(nameof(ConvertFromStringOrDefault), BindingFlags.NonPublic | BindingFlags.Static)!
                                                    .MakeGenericMethod(p.ParameterType);

                    Expression convertedExpr = Expression.Call(
                        convertGeneric,
                        rawVar,
                        defaultExpr
                    );

                    Expression assignConverted = Expression.Assign(paramVar, convertedExpr);
                    Expression assignDefault = Expression.Assign(paramVar, defaultExpr);

                    assignExpr = Expression.IfThenElse(
                        Expression.AndAlso(queryNotNull, tryGet),
                        assignConverted,
                        assignDefault
                    );
                }
                else {
                    // no TryGetValue, then default value
                    assignExpr = Expression.Assign(paramVar, defaultExpr);
                }

                body.Add(assignExpr);
                argExpressions.Add(paramVar);
            }

            //
            // Call the handler
            //
            Expression call;
            if (handler.Target is not null) {
                call = Expression.Call(Expression.Constant(handler.Target), method, argExpressions);
            }
            else {
                call = Expression.Call(method, argExpressions);
            }

            //
            // Convert handle depending on return type
            //
            Expression returnExpr;

            switch (kind) {
                case HandlerReturnKind.Void: {
                    // -> Completed()
                    MethodInfo completedMethod = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.Completed), BindingFlags.Public | BindingFlags.Static)!;
                    body.Add(call);
                    returnExpr = Expression.Call(completedMethod);
                    break;
                }

                case HandlerReturnKind.SyncResult: {
                    // result -> object -> RouteExecutorHelpers.InvokeHandlerResult(session, handlerResult, (object)result)
                    MethodInfo invokeResultMethod = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.InvokeHandlerResult), BindingFlags.Public | BindingFlags.Static)!;
                    Expression resultAsObject = method.ReturnType.IsValueType
                                                    ? Expression.Convert(call, typeof(object))
                                                    : Expression.TypeAs(call, typeof(object));
                    returnExpr = Expression.Call(
                        invokeResultMethod,
                        sessionParam,
                        handlerResultParam,
                        resultAsObject
                    );
                    break;
                }

                case HandlerReturnKind.TaskNoResult: {
                    // Task -> RouteExecutorHelpers.FromTask(task)
                    MethodInfo fromTaskMethod = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.FromTask), BindingFlags.Public | BindingFlags.Static)!;
                    returnExpr = Expression.Call(fromTaskMethod, call);
                    break;
                }

                case HandlerReturnKind.TaskWithResult: {
                    // Task<T> -> RouteExecutorHelpers.FromTaskWithResult<T>(task, session, handlerResult)
                    Type[] args = method.ReturnType.GetGenericArguments();
                    MethodInfo fromTaskWithResultGeneric = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.FromTaskWithResult), BindingFlags.Public | BindingFlags.Static)!
                                                                                       .MakeGenericMethod(args[0]);
                    returnExpr = Expression.Call(
                        fromTaskWithResultGeneric,
                        call,
                        sessionParam,
                        handlerResultParam
                    );
                    break;
                }

                case HandlerReturnKind.ValueTaskNoResult: {
                    // ValueTask -> RouteExecutorHelpers.FromValueTask(vt)
                    MethodInfo fromValueTaskMethod = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.FromValueTask), BindingFlags.Public | BindingFlags.Static)!;
                    returnExpr = Expression.Call(fromValueTaskMethod, call);
                    break;
                }

                case HandlerReturnKind.ValueTaskWithResult: {
                    // ValueTask<T> -> RouteExecutorHelpers.FromValueTaskWithResult<T>(vt, session, handlerResult)
                    Type[] args = method.ReturnType.GetGenericArguments();
                    MethodInfo fromValueTaskWithResultGeneric = typeof(RouteExecutorHelpers).GetMethod(nameof(RouteExecutorHelpers.FromValueTaskWithResult), BindingFlags.Public | BindingFlags.Static)!
                                                                                            .MakeGenericMethod(args[0]);
                    returnExpr = Expression.Call(
                        fromValueTaskWithResultGeneric,
                        call,
                        sessionParam,
                        handlerResultParam
                    );
                    break;
                }

                default:
                    throw new NotSupportedException($"Unsupported handler return type: {method.ReturnType.FullName}");
            }

            // final expression
            body.Add(returnExpr);
            BlockExpression block = Expression.Block(variables, body);
            Expression<HttpRouteExecutor>? lambda = Expression.Lambda<HttpRouteExecutor>(
                block,
                sessionParam,
                handlerResultParam
            );

            return lambda.Compile();
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

            // tout le reste = sync avec résultat
            return HandlerReturnKind.SyncResult;
        }

        #endregion return

        #region argument

        /// <summary>
        /// Get the default value of ParameterInfo
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        private static object? GetDefaultValueForParameter(ParameterInfo p) {
            if (p.HasDefaultValue) {
                return p.DefaultValue;
            }

            Type t = p.ParameterType;
            Type? underlying = Nullable.GetUnderlyingType(t);
            if (!t.IsValueType || underlying != null) {
                return null;
            }

            return Activator.CreateInstance(t);
        }

        /// <summary>
        /// Convert a raw string into the target type else use its default value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="raw"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        private static T ConvertFromStringOrDefault<T>(string raw, T defaultValue) {
            if (TryConvertFromString(raw, typeof(T), out object? value)) {
                return (T)value!;
            }
            return defaultValue;
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

        #endregion argument

    }

    /// <summary>
    /// RouteExecutorHelpers
    /// Mostly contains methods to convert a Result into a ValueTask
    /// </summary>
    public static class RouteExecutorHelpers {

        /// <summary>
        /// Convert a void to a ValueTask
        /// </summary>
        /// <returns></returns>
        public static ValueTask Completed() => default;

        /// <summary>
        /// Call HandlerResult if Result is not null else a return ValueTask
        /// </summary>
        /// <param name="session"></param>
        /// <param name="handlerResult"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public static ValueTask InvokeHandlerResult(HttpSession session, HttpHandlerResult handlerResult, object? result) {
            if (result is null) {
                return default;
            }
            return handlerResult(session, result);
        }

        /// <summary>
        /// Convert a Task without Result to a ValueTask
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public static ValueTask FromTask(Task task) => new(task);

        /// <summary>
        /// Convert a ValueTask without Result to a ValueTask
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public static ValueTask FromValueTask(ValueTask task) => task;

        /// <summary>
        /// Convert a Task with Result to a ValueTask
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task"></param>
        /// <param name="session"></param>
        /// <param name="handlerResult"></param>
        /// <returns></returns>
        public static ValueTask FromTaskWithResult<T>(Task<T> task, HttpSession session, HttpHandlerResult handlerResult) {
            return AwaitTaskWithResultAsync(task, session, handlerResult);
        }

        private static async ValueTask AwaitTaskWithResultAsync<T>(Task<T> task, HttpSession session, HttpHandlerResult handlerResult) {
            T? result = await task.ConfigureAwait(false);
            if (result is not null) {
                await handlerResult(session, result!).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Convert a ValueTask with Result to a ValueTask
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task"></param>
        /// <param name="session"></param>
        /// <param name="handlerResult"></param>
        /// <returns></returns>
        public static ValueTask FromValueTaskWithResult<T>(ValueTask<T> task, HttpSession session, HttpHandlerResult handlerResult) {
            return AwaitValueTaskWithResultAsync(task, session, handlerResult);
        }

        private static async ValueTask AwaitValueTaskWithResultAsync<T>(ValueTask<T> task, HttpSession session, HttpHandlerResult handlerResult) {
            T? result = await task.ConfigureAwait(false);
            if (result is not null) {
                await handlerResult(session, result!).ConfigureAwait(false);
            }
        }

    }

}
