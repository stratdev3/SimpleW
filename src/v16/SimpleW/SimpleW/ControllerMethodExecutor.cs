using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using NetCoreServer;


namespace SimpleW {

    public class ControllerMethodExecutor {

        #region target_delegate

        /// <summary>
        /// The Base Controller Class
        /// from which all other controllers depends
        /// </summary>
        private static readonly Type BaseController = typeof(Controller);

        /// <summary>
        /// MethodInfo to Controller.Initialize()
        /// </summary>
        private static readonly MethodInfo InitializeSetter = BaseController.GetMethod(nameof(Controller.Initialize), new Type[] { typeof(ISimpleWSession), typeof(HttpRequest) });

        /// <summary>
        /// MethodInfo to Controller.OnBeforeMethodInternal()
        /// </summary>
        private static readonly MethodInfo OnBeforeMethod = BaseController.GetMethod(nameof(Controller.OnBeforeMethod));

        /// <summary>
        /// MethodInfo to Controller.SendResponseAsync()
        /// </summary>
        private static readonly MethodInfo SendObjectResponseAsync = BaseController.GetMethod(nameof(Controller.SendResponseAsync), new Type[] { typeof(object) });

        /// <summary>
        /// MethodInfo to Controller.Dispose destructor
        /// </summary>
        private static readonly MethodInfo DisposeMethod = typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));

        #endregion target_delegate

        #region ExecuteMethod

        /// <summary>
        /// The Expression Tree to Execute Method with Parameters
        /// </summary>
        public readonly Action<ISimpleWSession, HttpRequest, object[]> ExecuteMethod;

        /// <summary>
        /// Parameters to pass to the ExecuteMethod
        /// </summary>
        public readonly Dictionary<ParameterInfo, object> Parameters;

        /// <summary>
        /// Initialize a new instance
        /// </summary>
        /// <param name="executeMethod"></param>
        /// <param name="parameters"></param>
        private ControllerMethodExecutor(Action<ISimpleWSession, HttpRequest, object[]> executeMethod, Dictionary<ParameterInfo, object> parameters) {
            ExecuteMethod = executeMethod;
            Parameters = parameters;
        }

        /// <summary>
        /// Create a ControllerMethodExecutor
        /// </summary>
        /// <param name="method">The MethodInfo to add</param>
        /// <returns></returns>
        public static ControllerMethodExecutor Create(MethodInfo method) {
            Type? controllerType = method.ReflectedType;

            // lambda parameters
            ParameterExpression sessionParam = Expression.Parameter(typeof(ISimpleWSession), "session");
            ParameterExpression requestParam = Expression.Parameter(typeof(HttpRequest), "request");
            ParameterExpression argsArray = Expression.Parameter(typeof(object[]), "args");

            // local var for controller
            ParameterExpression controllerVar = Expression.Variable(controllerType, "controller");

            List<Expression> body = new() {
                // var controller = new ControllerType();
                Expression.Assign(controllerVar, Expression.New(controllerType)),

                // controller.Initialize(session, request);
                Expression.Call(controllerVar, InitializeSetter, sessionParam, requestParam),

                // controller.OnBeforeMethod();
                Expression.Call(controllerVar, OnBeforeMethod)
            };

            // parameters
            (IEnumerable<Expression> callParameters, Dictionary<ParameterInfo, object> parameterDefaults) = BuildParameterExpressions(method, argsArray);

            // call controller method
            Expression call = Expression.Call(controllerVar, method, callParameters);

            // handle response types
            if (method.ReturnType == typeof(Task)) {
                // nothing special, just await implicitly
            }
            else if (method.ReturnType == typeof(void)) {
                // do nothing, user method handled response internally
            }
            else {
                // SendResponseAsync(result)
                call = Expression.Call(controllerVar, SendObjectResponseAsync, Expression.Convert(call, typeof(object)));
            }

            body.Add(call);

            // dispose
            if (DisposableControllers.Contains(controllerType)) {
                body.Add(Expression.Call(Expression.TypeAs(controllerVar, typeof(IDisposable)), DisposeMethod));
            }

            BlockExpression block = Expression.Block(new[] { controllerVar }, body);
            Expression<Action<ISimpleWSession, HttpRequest, object[]>> lambda = Expression.Lambda<Action<ISimpleWSession, HttpRequest, object[]>>(block, sessionParam, requestParam, argsArray);

            return new ControllerMethodExecutor(lambda.Compile(), parameterDefaults);
        }

        #endregion ExecuteMethod

        #region ExecuteFunc

        /// <summary>
        /// The Function to Execute
        /// </summary>
        public readonly Func<ISimpleWSession, HttpRequest, object[], object?> ExecuteFunc;

        /// <summary>
        /// Initialize a new instance
        /// </summary>
        /// <param name="executeFunc"></param>
        /// <param name="parameters"></param>
        private ControllerMethodExecutor(Func<ISimpleWSession, HttpRequest, object[], object?> executeFunc, Dictionary<ParameterInfo, object> parameters) {
            ExecuteFunc = executeFunc;
            Parameters = parameters;
        }

        /// <summary>
        /// Create a ControllerMethodExecutor
        /// </summary>
        /// <param name="executeFunc"></param>
        /// <returns></returns>
        public static ControllerMethodExecutor Create(Delegate executeFunc) {
            MethodInfo method = executeFunc.Method;

            // lambda parameters
            ParameterExpression sessionParam = Expression.Parameter(typeof(ISimpleWSession), "session");
            ParameterExpression requestParam = Expression.Parameter(typeof(HttpRequest), "request");
            ParameterExpression argsArray = Expression.Parameter(typeof(object[]), "args");

            // parameters
            (IEnumerable<Expression> callParameters, Dictionary<ParameterInfo, object> parameterDefaults) = BuildParameterExpressions(method, argsArray, sessionParam, requestParam);

            // call func
            Expression call = Expression.Invoke(Expression.Constant(executeFunc), callParameters);

            Expression<Func<ISimpleWSession, HttpRequest, object[], object?>> lambda = Expression.Lambda<Func<ISimpleWSession, HttpRequest, object[], object?>>(Expression.Block(typeof(object), call), sessionParam, requestParam, argsArray);

            return new ControllerMethodExecutor(lambda.Compile(), parameterDefaults);
        }

        #endregion ExecuteFunc

        #region helper

        /// <summary>
        /// Build Parameters Expression
        /// </summary>
        /// <param name="method"></param>
        /// <param name="argsArray"></param>
        /// <param name="sessionParam"></param>
        /// <param name="requestParam"></param>
        /// <returns></returns>
        private static (IEnumerable<Expression> callParams, Dictionary<ParameterInfo, object> defaultParamsValue) BuildParameterExpressions(MethodInfo method, ParameterExpression argsArray, ParameterExpression? sessionParam = null, ParameterExpression? requestParam = null) {
            ParameterInfo[] parameters = method.GetParameters();
            Expression[] callParameters = new Expression[parameters.Length];
            Dictionary<ParameterInfo, object> defaultParamsValue = new(parameters.Length);

            for (int i = 0; i < parameters.Length; i++) {
                ParameterInfo p = parameters[i];
                Expression expr;

                if (sessionParam != null && p.ParameterType == typeof(ISimpleWSession)) {
                    expr = sessionParam;
                    defaultParamsValue[p] = p.ParameterType;
                }
                else if (requestParam != null && p.ParameterType == typeof(HttpRequest)) {
                    expr = requestParam;
                    defaultParamsValue[p] = p.ParameterType;
                }
                else {
                    expr = Expression.Convert(Expression.ArrayIndex(argsArray, Expression.Constant(i)), p.ParameterType);
                    defaultParamsValue[p] = TryGetDefaultValue(p);
                }

                callParameters[i] = expr;
            }

            return (callParameters, defaultParamsValue);
        }

        /// <summary>
        /// Return the default value of ParameterInfo
        /// </summary>
        /// <param name="param"></param>
        /// <returns></returns>
        private static object TryGetDefaultValue(ParameterInfo param) {
            try {
                if (param.HasDefaultValue) {
                    return param.DefaultValue ?? (param.ParameterType.IsValueType ? Activator.CreateInstance(param.ParameterType) : null);
                }
            }
            catch (FormatException) when (param.ParameterType == typeof(DateTime)) {
                return default(DateTime);
            }

            return param.ParameterType;
        }

        /// <summary>
        /// Cached set of all loaded controller types
        /// </summary>
        private static readonly HashSet<Type> CachedControllers = new(
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => {
                    try { return a.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
                })
                .Where(t => BaseController.IsAssignableFrom(t) && t != BaseController)
                .OfType<Type>()
        );

        /// <summary>
        /// Return all Class that inherited from BaseController Class
        /// </summary>
        /// <param name="excepts">List of Controller to not auto load</param>
        /// <returns></returns>
        public static IEnumerable<Type> Controllers(IEnumerable<Type> excepts = null) {
            return excepts == null ? CachedControllers : CachedControllers.Where(t => !excepts.Contains(t));
        }

        /// <summary>
        /// List of Disposable Controllers
        /// </summary>
        private static readonly HashSet<Type> DisposableControllers = new(
            CachedControllers.Where(t => typeof(IDisposable).IsAssignableFrom(t))
        );

        #endregion helper

    }

}
