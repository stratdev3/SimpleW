using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;


namespace SimpleW {

    /// <summary>
    /// DelegateExecutorFactory
    /// </summary>
    public static class DelegateExecutorFactory {

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

            // session.Request
            MemberExpression? requestProp = Expression.Property(sessionParam, nameof(HttpSession.Request));

            // session.Request.Query
            MemberExpression? queryProp = Expression.Property(requestProp, "Query");

            // TryGetValue(string, out string)
            Type queryType = queryProp.Type;
            MethodInfo? queryTryGetValueMethod = queryType.GetMethod(
                "TryGetValue",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(string), typeof(string).MakeByRefType() },
                modifiers: null
            );

            // session.Request.RouteValues
            MemberExpression routeValuesProp = Expression.Property(requestProp, nameof(HttpRequest.RouteValues));

            // TryGetValue(string, out string)
            Type routeValuesType = routeValuesProp.Type;
            MethodInfo? routeTryGetValueMethod = typeof(Dictionary<string, string>).GetMethod(
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
            //     2. value of the Request RouteValues String if exists (mapping by name)
            //     3. value of the Request Query String if exists (mapping by name)
            //     4. default value of the parameter
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
                if (p.Name is not null) {

                    //
                    // RouteValues
                    //

                    // routeValues != null
                    Expression routeNotNull = Expression.NotEqual(
                        routeValuesProp,
                        Expression.Constant(null, routeValuesType)
                    );

                    // routeValues.TryGetValue("name", out rawVar)
                    Expression routeTryGet = (routeTryGetValueMethod is null)
                                                ? Expression.Constant(false)
                                                : Expression.Call(
                                                    Expression.Convert(routeValuesProp, typeof(Dictionary<string, string>)),
                                                    routeTryGetValueMethod,
                                                    Expression.Constant(p.Name, typeof(string)),
                                                    rawVar
                                                );

                    // ConvertFromStringOrDefault<T>(rawVar, defaultExpr)
                    MethodInfo convertGeneric = typeof(DelegateExecutorFactory)
                                                    .GetMethod(nameof(ConvertFromStringOrDefault), BindingFlags.NonPublic | BindingFlags.Static)!
                                                    .MakeGenericMethod(p.ParameterType);

                    Expression convertedExpr = Expression.Call(convertGeneric, rawVar, defaultExpr);

                    Expression assignConverted = Expression.Assign(paramVar, convertedExpr);
                    Expression assignDefault = Expression.Assign(paramVar, defaultExpr);

                    // if (routeValues != null && routeTryGet) param = converted else ... (fallback query)
                    Expression routeBranch = Expression.IfThenElse(
                        Expression.AndAlso(routeNotNull, routeTryGet),
                        assignConverted,
                        Expression.Empty()
                    );

                    //
                    // QueryString
                    //
                    Expression queryBranch;

                    
                    if (queryTryGetValueMethod != null) {

                        // query != null
                        Expression queryNotNull = Expression.NotEqual(
                            queryProp,
                            Expression.Constant(null, queryType)
                        );

                        // query.TryGetValue("name", out rawVar)
                        Expression queryTryGet = Expression.Call(
                            queryProp,
                            queryTryGetValueMethod,
                            Expression.Constant(p.Name, typeof(string)),
                            rawVar
                        );

                        Expression queryAssign = Expression.IfThenElse(
                            Expression.AndAlso(queryNotNull, queryTryGet),
                            assignConverted,
                            assignDefault
                        );

                        queryBranch = queryAssign;
                    }
                    else {
                        queryBranch = assignDefault;
                    }

                    // Compose: try route; if route matched we must NOT overwrite with query.
                    // So: if route matched => assignConverted else => queryBranch
                    // To do that cleanly: make a bool local "matchedRoute".
                    ParameterExpression matchedRouteVar = Expression.Variable(typeof(bool), (p.Name ?? "arg") + "_matchedRoute");
                    variables.Add(matchedRouteVar);

                    Expression setMatchedFalse = Expression.Assign(matchedRouteVar, Expression.Constant(false));
                    Expression setMatchedTrue = Expression.Assign(matchedRouteVar, Expression.Constant(true));

                    Expression routeTryBlock = Expression.IfThenElse(
                        Expression.AndAlso(routeNotNull, routeTryGet),
                        Expression.Block(setMatchedTrue, assignConverted),
                        Expression.Empty()
                    );

                    Expression finalAssign = Expression.IfThenElse(
                        matchedRouteVar,
                        Expression.Empty(), // already assigned
                        queryBranch
                    );

                    assignExpr = Expression.Block(
                        setMatchedFalse,
                        routeTryBlock,
                        finalAssign
                    );
                }
                else {
                    // no name, then default value
                    assignExpr = Expression.Assign(paramVar, defaultExpr);
                }

                body.Add(assignExpr);
                argExpressions.Add(paramVar);
            }

            //
            // Call the handler
            //
            Expression call;

            // 1. static method
            if (method.IsStatic) {
                call = Expression.Call(method, argExpressions);
            }
            // 2. instance and non null target, close delegate instance
            else if (handler.Target is not null) {
                call = Expression.Call(Expression.Constant(handler.Target), method, argExpressions);
            }
            // 3. instance and null target, open delegate instance (fallback)
            else {
                if (argExpressions.Count == 0) {
                    throw new InvalidOperationException($"Instance method '{method.Name}' requires an instance parameter.");
                }
                Expression instanceExpr = argExpressions[0];
                if (!method.DeclaringType!.IsAssignableFrom(instanceExpr.Type)) {
                    throw new InvalidOperationException($"First parameter type '{instanceExpr.Type}' is not assignable to declaring type '{method.DeclaringType}'.");
                }

                List<Expression> callArgs;
                if (argExpressions.Count > 1) {
                    callArgs = argExpressions.GetRange(1, argExpressions.Count - 1);
                }
                else {
                    callArgs = new List<Expression>();
                }

                call = Expression.Call(
                    Expression.Convert(instanceExpr, method.DeclaringType),
                    method,
                    callArgs
                );
            }

            //
            // Convert handle depending on return type
            //
            Expression returnExpr;

            switch (kind) {
                case HandlerReturnKind.Void: {
                    // -> Completed()
                    MethodInfo completedMethod = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.Completed), BindingFlags.Public | BindingFlags.Static)!;
                    body.Add(call);
                    returnExpr = Expression.Call(completedMethod);
                    break;
                }

                case HandlerReturnKind.SyncResult: {
                    // result -> object -> RouteExecutorHelpers.InvokeHandlerResult(session, handlerResult, (object)result)
                    MethodInfo invokeResultMethod = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.InvokeHandlerResult), BindingFlags.Public | BindingFlags.Static)!;
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
                    MethodInfo fromTaskMethod = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.FromTask), BindingFlags.Public | BindingFlags.Static)!;
                    returnExpr = Expression.Call(fromTaskMethod, call);
                    break;
                }

                case HandlerReturnKind.TaskWithResult: {
                    // Task<T> -> RouteExecutorHelpers.FromTaskWithResult<T>(task, session, handlerResult)
                    Type[] args = method.ReturnType.GetGenericArguments();
                    MethodInfo fromTaskWithResultGeneric = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.FromTaskWithResult), BindingFlags.Public | BindingFlags.Static)!
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
                    MethodInfo fromValueTaskMethod = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.FromValueTask), BindingFlags.Public | BindingFlags.Static)!;
                    returnExpr = Expression.Call(fromValueTaskMethod, call);
                    break;
                }

                case HandlerReturnKind.ValueTaskWithResult: {
                    // ValueTask<T> -> RouteExecutorHelpers.FromValueTaskWithResult<T>(vt, session, handlerResult)
                    Type[] args = method.ReturnType.GetGenericArguments();
                    MethodInfo fromValueTaskWithResultGeneric = typeof(DelegateExecutorFactory).GetMethod(nameof(DelegateExecutorFactory.FromValueTaskWithResult), BindingFlags.Public | BindingFlags.Static)!
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

            // string
            if (targetType == typeof(string)) {
                value = raw;
                return true;
            }

            // bool
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

            // int
            if (targetType == typeof(int)) {
                if (int.TryParse(raw, style, culture, out int i)) {
                    value = i;
                    return true;
                }
                return false;
            }

            // long
            if (targetType == typeof(long)) {
                if (long.TryParse(raw, style, culture, out long l)) {
                    value = l;
                    return true;
                }
                return false;
            }

            // double
            if (targetType == typeof(double)) {
                if (double.TryParse(raw, style, culture, out double d)) {
                    value = d;
                    return true;
                }
                return false;
            }

            // float
            if (targetType == typeof(float)) {
                if (float.TryParse(raw, style, culture, out float f)) {
                    value = f;
                    return true;
                }
                return false;
            }

            // DateTime
            if (targetType == typeof(DateTime)) {
                // ISO
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt)) {
                    value = dt;
                    return true;
                }
                // fallback
                if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt)) {
                    value = dt;
                    return true;
                }
                return false;
            }

            // DateOnly
            if (targetType == typeof(DateOnly)) {
                if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dOnly)) {
                    value = dOnly;
                    return true;
                }
                if (DateOnly.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out dOnly)) {
                    value = dOnly;
                    return true;
                }
                return false;
            }

            // TimeOnly
            if (targetType == typeof(TimeOnly)) {
                if (TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly tOnly)) {
                    value = tOnly;
                    return true;
                }
                if (TimeOnly.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out tOnly)) {
                    value = tOnly;
                    return true;
                }
                return false;
            }

            // DateTimeOffset
            if (targetType == typeof(DateTimeOffset)) {
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset dto)) {
                    value = dto;
                    return true;
                }
                if (DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dto)) {
                    value = dto;
                    return true;
                }
                return false;
            }

            // TimeSpan
            if (targetType == typeof(TimeSpan)) {
                if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out TimeSpan ts)) {
                    value = ts;
                    return true;
                }
                if (TimeSpan.TryParse(raw, CultureInfo.CurrentCulture, out ts)) {
                    value = ts;
                    return true;
                }
                return false;
            }

            // Guid
            if (targetType == typeof(Guid)) {
                if (Guid.TryParse(raw, out Guid g)) {
                    value = g;
                    return true;
                }
                return false;
            }

            // Enum
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

        #region helpers

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

        #endregion helpers

    }

    /// <summary>
    /// ControllerDelegateFactory
    /// </summary>
    public static class ControllerDelegateFactory {

        /// <summary>
        /// Register a Controller type and map all its routes
        /// </summary>
        /// <param name="controllerType"></param>
        /// <param name="router"></param>
        /// <param name="basePrefix"></param>
        /// <exception cref="ArgumentException"></exception>
        public static void RegisterController(Type controllerType, Router router, string? basePrefix) {
            ArgumentNullException.ThrowIfNull(controllerType);
            ArgumentNullException.ThrowIfNull(router);

            if (controllerType.IsAbstract) {
                throw new ArgumentException($"Type '{controllerType.FullName}' must not be abstract.", nameof(controllerType));
            }
            if (!typeof(Controller).IsAssignableFrom(controllerType)) {
                throw new ArgumentException($"Type '{controllerType.FullName}' must inherit from Controller.", nameof(controllerType));
            }

            // RouteAttribut
            RouteAttribute? classRoute = controllerType.GetCustomAttribute<RouteAttribute>(inherit: true);
            string controllerPrefix = classRoute?.Path ?? string.Empty;

            foreach (MethodInfo method in controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {

                RouteAttribute[] methodRoutes = method.GetCustomAttributes<RouteAttribute>(inherit: true).ToArray();
                if (methodRoutes.Length == 0) {
                    continue;
                }

                foreach (RouteAttribute attr in methodRoutes.Where(a => !string.IsNullOrWhiteSpace(a.Method) && a.Method != "*")) {
                    Delegate handlerDelegate = Create(controllerType, method);
                    string fullPath = BuildFullPath(basePrefix ?? string.Empty, controllerPrefix, attr);
                    router.Map(attr.Method, fullPath, handlerDelegate);
                }
            }
        }

        /// <summary>
        /// Build Path from controller
        /// </summary>
        /// <param name="basePrefix"></param>
        /// <param name="controllerPrefix"></param>
        /// <param name="methodAttr"></param>
        /// <returns></returns>
        private static string BuildFullPath(string basePrefix, string controllerPrefix, RouteAttribute methodAttr) {

            // absolute path
            if (methodAttr.IsAbsolutePath) {
                string absolutePath = methodAttr.Path;
                return string.IsNullOrEmpty(absolutePath) ? "/" : absolutePath;
            }

            // full path
            string fullPath = string.Empty;

            if (!string.IsNullOrEmpty(basePrefix)) {
                fullPath += basePrefix;
            }
            if (!string.IsNullOrEmpty(controllerPrefix)) {
                string c = controllerPrefix; 
                fullPath += c;
            }
            if (!string.IsNullOrEmpty(methodAttr.Path)) {
                string m = methodAttr.Path;
                fullPath += m;
            }
            if (string.IsNullOrEmpty(fullPath)) {
                fullPath = "/";
            }

            return fullPath;
        }

        /// <summary>
        /// Create a Delegate from a ControllerType
        /// </summary>
        /// <param name="controllerType"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        private static Delegate Create(Type controllerType, MethodInfo method) {

            // get parameter info
            ParameterInfo[] methodParams = method.GetParameters();

            // lambda parameters (session + methodParams)
            ParameterExpression[] lambdaParams = new ParameterExpression[1 + methodParams.Length];
            lambdaParams[0] = Expression.Parameter(typeof(HttpSession), "session");
            for (int i = 0; i < methodParams.Length; i++) {
                ParameterInfo p = methodParams[i];
                lambdaParams[i + 1] = Expression.Parameter(p.ParameterType, p.Name ?? $"arg{i}");
            }

            // declare var controller
            ParameterExpression controllerVar = Expression.Variable(controllerType, "controller");

            // instanciate a new Type instance and assign to controller var
            MethodInfo createInstanceMethod = typeof(Activator).GetMethod(
                nameof(Activator.CreateInstance),
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Type) },
                modifiers: null
            )!;
            Expression newControllerExpr = Expression.Convert(
                Expression.Call(createInstanceMethod, Expression.Constant(controllerType)),
                controllerType
            );
            Expression assignController = Expression.Assign(controllerVar, newControllerExpr);

            // assign controller.Session = session;
            PropertyInfo sessionProp = typeof(Controller).GetProperty(nameof(Controller.Session), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Expression assignSession = Expression.Assign(Expression.Property(controllerVar, sessionProp), lambdaParams[0]); // lambdaParams[0] is session

            // call controller.OnBeforeMethod()
            MethodInfo onBeforeMethodInfo = typeof(Controller).GetMethod(nameof(Controller.OnBeforeMethod), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            Expression callOnBefore = Expression.Call(controllerVar, onBeforeMethodInfo);

            // call controller.Method(p1, p2, ...)
            Expression[]? callArgs = new Expression[methodParams.Length];
            for (int i = 0; i < methodParams.Length; i++) {
                callArgs[i] = lambdaParams[i + 1];
            }
            MethodCallExpression call = Expression.Call(controllerVar, method, callArgs);

            // body
            Expression body;
            if (method.ReturnType == typeof(void)) {
                body = Expression.Block(
                    new[] { controllerVar },
                    assignController,
                    assignSession,
                    callOnBefore,
                    call
                );
            }
            else {
                body = Expression.Block(
                    new[] { controllerVar },
                    assignController,
                    assignSession,
                    callOnBefore,
                    call
                );
            }

            Type delegateType;
            // delegate return void
            if (method.ReturnType == typeof(void)) {
                Type[] paramTypes = lambdaParams.Select(p => p.Type).ToArray();
                delegateType = Expression.GetActionType(paramTypes);
            }
            // delegate return
            else {
                Type[] typeArgs = new Type[lambdaParams.Length + 1];
                for (int i = 0; i < lambdaParams.Length; i++) {
                    typeArgs[i] = lambdaParams[i].Type;
                }
                typeArgs[^1] = method.ReturnType;
                delegateType = Expression.GetDelegateType(typeArgs);
            }

            // final expression
            LambdaExpression lambda = Expression.Lambda(
                delegateType,
                body,
                lambdaParams
            );

            return lambda.Compile();
        }

    }

}
