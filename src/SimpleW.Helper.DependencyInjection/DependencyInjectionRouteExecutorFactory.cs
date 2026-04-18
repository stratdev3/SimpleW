using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;


namespace SimpleW.Helper.DependencyInjection {

    /// <summary>
    /// Builds route executors for DI-enabled controllers.
    /// </summary>
    internal static class DependencyInjectionRouteExecutorFactory {

        private static readonly object?[] _noArguments = Array.Empty<object?>();
        private static readonly MethodInfo _createTaskWithResultAdapterMethod = typeof(DependencyInjectionRouteExecutorFactory).GetMethod(nameof(CreateTaskWithResultAdapter), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo _createValueTaskWithResultAdapterMethod = typeof(DependencyInjectionRouteExecutorFactory).GetMethod(nameof(CreateValueTaskWithResultAdapter), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo _createControllerMethod = typeof(DependencyInjectionRouteExecutorFactory).GetMethod(nameof(CreateController), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo _convertFromStringOrDefaultMethod = typeof(RouteExecutorFactory).GetMethod(nameof(RouteExecutorFactory.ConvertFromStringOrDefault), BindingFlags.Public | BindingFlags.Static)!;

        private delegate object? CompiledActionInvoker(HttpSession session, out Controller? controller);
        private delegate ValueTask ReturnAdapter(HttpSession session, HttpResultHandler resultHandler, object? result);

        /// <summary>
        /// Creates a route executor for one controller action.
        /// </summary>
        /// <param name="controllerType"></param>
        /// <param name="method"></param>
        /// <returns></returns>
        public static HttpRouteExecutor Create(Type controllerType, MethodInfo method) {
            ArgumentNullException.ThrowIfNull(controllerType);
            ArgumentNullException.ThrowIfNull(method);

            ObjectFactory controllerFactory = ActivatorUtilities.CreateFactory(controllerType, Type.EmptyTypes);
            CompiledActionInvoker actionInvoker = CreateActionInvoker(controllerType, method, controllerFactory);
            ReturnAdapter returnAdapter = CreateReturnAdapter(method.ReturnType);

            return (session, resultHandler) => InvokeAsync(session, resultHandler, actionInvoker, returnAdapter);
        }

        private static async ValueTask InvokeAsync(
            HttpSession session,
            HttpResultHandler resultHandler,
            CompiledActionInvoker actionInvoker,
            ReturnAdapter returnAdapter
        ) {
            Controller? controller = null;
            Exception? failure = null;

            try {
                object? rawResult = actionInvoker(session, out controller);
                await returnAdapter(session, resultHandler, rawResult).ConfigureAwait(false);
            }
            catch (Exception ex) {
                failure = ex;
                throw;
            }
            finally {
                if (controller != null) {
                    try {
                        await DisposeControllerAsync(controller).ConfigureAwait(false);
                    }
                    catch when (failure != null) {
                    }
                }
            }
        }

        private static CompiledActionInvoker CreateActionInvoker(Type controllerType, MethodInfo method, ObjectFactory controllerFactory) {
            ArgumentNullException.ThrowIfNull(controllerType);
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(controllerFactory);

            ParameterInfo[] parameters = method.GetParameters();

            ParameterExpression sessionParam = Expression.Parameter(typeof(HttpSession), "session");
            ParameterExpression controllerOutParam = Expression.Parameter(typeof(Controller).MakeByRefType(), "controller");

            MemberExpression requestProp = Expression.Property(sessionParam, nameof(HttpSession.Request));
            MemberExpression queryProp = Expression.Property(requestProp, nameof(HttpRequest.Query));
            Type queryType = queryProp.Type;
            MethodInfo? queryTryGetValueMethod = queryType.GetMethod(
                                                     "TryGetValue",
                                                     BindingFlags.Public | BindingFlags.Instance,
                                                     binder: null,
                                                     types: new[] { typeof(string), typeof(string).MakeByRefType() },
                                                     modifiers: null
                                                 );

            MemberExpression routeValuesProp = Expression.Property(requestProp, nameof(HttpRequest.RouteValues));
            Type routeValuesType = routeValuesProp.Type;
            MethodInfo? routeTryGetValueMethod = typeof(Dictionary<string, string>).GetMethod(
                                                     "TryGetValue",
                                                     BindingFlags.Public | BindingFlags.Instance,
                                                     binder: null,
                                                     types: new[] { typeof(string), typeof(string).MakeByRefType() },
                                                     modifiers: null
                                                 );

            ParameterExpression controllerVar = Expression.Variable(controllerType, "controller");
            MethodInfo onBeforeMethodInfo = typeof(Controller).GetMethod(
                                                nameof(Controller.OnBeforeMethod),
                                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                                            )!;

            List<ParameterExpression> variables = [ controllerVar ];
            List<Expression> argExpressions = new(parameters.Length);
            List<Expression> body = [
                Expression.Assign(controllerOutParam, Expression.Default(typeof(Controller))),
                Expression.Assign(
                    controllerVar,
                    Expression.Convert(
                        Expression.Call(
                            _createControllerMethod,
                            Expression.Constant(controllerFactory),
                            Expression.Constant(controllerType, typeof(Type)),
                            sessionParam
                        ),
                        controllerType
                    )
                ),
                Expression.Assign(controllerOutParam, Expression.Convert(controllerVar, typeof(Controller))),
                Expression.Assign(Expression.Property(controllerVar, nameof(Controller.Session)), sessionParam),
                Expression.Call(controllerVar, onBeforeMethodInfo)
            ];

            foreach (ParameterInfo p in parameters) {

                if (p.ParameterType == typeof(HttpSession)) {
                    argExpressions.Add(sessionParam);
                    continue;
                }

                ParameterExpression paramVar = Expression.Variable(p.ParameterType, p.Name!);
                variables.Add(paramVar);

                ParameterExpression rawVar = Expression.Variable(typeof(string), p.Name! + "_raw");
                variables.Add(rawVar);

                object? defaultObj = RouteExecutorFactory.GetDefaultValueForParameter(p);
                Expression defaultExpr;
                if (defaultObj == null && p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) == null) {
                    defaultExpr = Expression.Default(p.ParameterType);
                }
                else {
                    defaultExpr = Expression.Constant(defaultObj, p.ParameterType);
                }

                Expression assignExpr;
                if (!string.IsNullOrEmpty(p.Name)) {

                    MethodInfo convertGeneric = _convertFromStringOrDefaultMethod.MakeGenericMethod(p.ParameterType);

                    Expression convertedExpr = Expression.Call(convertGeneric, rawVar, defaultExpr);
                    Expression assignConverted = Expression.Assign(paramVar, convertedExpr);
                    Expression assignDefault = p.HasDefaultValue
                                                    ? Expression.Assign(paramVar, defaultExpr)
                                                    : Expression.Throw(
                                                          Expression.New(
                                                              typeof(InvalidOperationException).GetConstructor(new[] { typeof(string) })!,
                                                                                                               Expression.Constant($"Missing required parameter '{p.Name}'")
                                                          ),
                                                          p.ParameterType
                                                      );

                    Expression routeNotNull = Expression.NotEqual(routeValuesProp, Expression.Constant(null, routeValuesType));

                    Expression routeTryGet = (routeTryGetValueMethod == null)
                                                ? Expression.Constant(false)
                                                : Expression.Call(
                                                      Expression.Convert(routeValuesProp, typeof(Dictionary<string, string>)),
                                                      routeTryGetValueMethod,
                                                      Expression.Constant(p.Name, typeof(string)),
                                                      rawVar
                                                  );

                    ParameterExpression matchedRouteVar = Expression.Variable(typeof(bool), (p.Name ?? "arg") + "_matchedRoute");
                    variables.Add(matchedRouteVar);

                    Expression setMatchedFalse = Expression.Assign(matchedRouteVar, Expression.Constant(false));
                    Expression setMatchedTrue = Expression.Assign(matchedRouteVar, Expression.Constant(true));

                    Expression routeTryBlock = Expression.IfThenElse(
                                                   Expression.AndAlso(routeNotNull, routeTryGet),
                                                   Expression.Block(setMatchedTrue, assignConverted),
                                                   Expression.Empty()
                                               );

                    Expression queryBranch;
                    if (queryTryGetValueMethod != null) {
                        Expression queryNotNull = Expression.NotEqual(queryProp, Expression.Constant(null, queryType));

                        Expression queryTryGet = Expression.Call(
                                                     queryProp,
                                                     queryTryGetValueMethod,
                                                     Expression.Constant(p.Name, typeof(string)),
                                                     rawVar
                                                 );

                        queryBranch = Expression.IfThenElse(
                                          Expression.AndAlso(queryNotNull, queryTryGet),
                                          assignConverted,
                                          assignDefault
                                      );
                    }
                    else {
                        queryBranch = assignDefault;
                    }

                    Expression finalAssign = Expression.IfThenElse(matchedRouteVar, Expression.Empty(), queryBranch);
                    assignExpr = Expression.Block(setMatchedFalse, routeTryBlock, finalAssign);
                }
                else {
                    assignExpr = Expression.Assign(paramVar, defaultExpr);
                }

                body.Add(assignExpr);
                argExpressions.Add(paramVar);
            }

            Expression callExpr = Expression.Call(controllerVar, method, argExpressions);

            if (method.ReturnType == typeof(void)) {
                body.Add(callExpr);
                body.Add(Expression.Constant(null, typeof(object)));
            }
            else {
                Expression resultAsObject = method.ReturnType.IsValueType
                                                ? Expression.Convert(callExpr, typeof(object))
                                                : Expression.TypeAs(callExpr, typeof(object));
                body.Add(resultAsObject);
            }

            BlockExpression block = Expression.Block(variables, body);
            Expression<CompiledActionInvoker> lambda = Expression.Lambda<CompiledActionInvoker>(
                                                           block,
                                                           sessionParam,
                                                           controllerOutParam
                                                       );

            return lambda.Compile();
        }

        private static Controller CreateController(ObjectFactory controllerFactory, Type controllerType, HttpSession session) {
            ArgumentNullException.ThrowIfNull(controllerFactory);
            ArgumentNullException.ThrowIfNull(controllerType);
            ArgumentNullException.ThrowIfNull(session);

            object instance = controllerFactory(session.GetRequestServices(), _noArguments);

            if (instance is not Controller controller || !controllerType.IsInstanceOfType(controller)) {
                throw new InvalidOperationException($"Type '{controllerType.FullName}' must inherit from Controller.");
            }

            return controller;
        }

        private static async ValueTask DisposeControllerAsync(Controller controller) {
            if (controller is IAsyncDisposable asyncDisposable) {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (controller is IDisposable disposable) {
                disposable.Dispose();
            }
        }

        #region return adapters

        private static ReturnAdapter CreateReturnAdapter(Type returnType) {
            RouteExecutorFactory.HandlerReturnKind kind = RouteExecutorFactory.GetReturnKind(returnType);

            return kind switch {
                RouteExecutorFactory.HandlerReturnKind.Void => static (_, _, _) => RouteExecutorFactory.Completed(),
                RouteExecutorFactory.HandlerReturnKind.SyncResult => RouteExecutorFactory.CallResultHandler,
                RouteExecutorFactory.HandlerReturnKind.TaskNoResult => static (_, _, result) => RouteExecutorFactory.FromTask(EnsureResultType<Task>(result, typeof(Task))),
                RouteExecutorFactory.HandlerReturnKind.TaskWithResult => CreateClosedReturnAdapter(_createTaskWithResultAdapterMethod, returnType.GetGenericArguments()[0]),
                RouteExecutorFactory.HandlerReturnKind.ValueTaskNoResult => static (_, _, result) => RouteExecutorFactory.FromValueTask(EnsureResultType<ValueTask>(result, typeof(ValueTask))),
                RouteExecutorFactory.HandlerReturnKind.ValueTaskWithResult => CreateClosedReturnAdapter(_createValueTaskWithResultAdapterMethod, returnType.GetGenericArguments()[0]),
                _ => throw new NotSupportedException($"Unsupported handler return type: {returnType.FullName}")
            };
        }

        private static ReturnAdapter CreateClosedReturnAdapter(MethodInfo openGenericMethod, Type resultType) {
            MethodInfo closedMethod = openGenericMethod.MakeGenericMethod(resultType);
            return (ReturnAdapter)Delegate.CreateDelegate(typeof(ReturnAdapter), closedMethod);
        }

        private static ReturnAdapter CreateTaskWithResultAdapter<T>() {
            return static (session, resultHandler, result) => RouteExecutorFactory.FromTaskWithResult(EnsureResultType<Task<T>>(result, typeof(Task<T>)), session, resultHandler);
        }

        private static ReturnAdapter CreateValueTaskWithResultAdapter<T>() {
            return static (session, resultHandler, result) => RouteExecutorFactory.FromValueTaskWithResult(EnsureResultType<ValueTask<T>>(result, typeof(ValueTask<T>)), session, resultHandler);
        }

        private static T EnsureResultType<T>(object? result, Type expectedType) {
            if (result is T typed) {
                return typed;
            }

            throw new InvalidOperationException($"Controller action expected a non-null return value of type '{expectedType.FullName}'.");
        }

        #endregion return adapters

    }

}
