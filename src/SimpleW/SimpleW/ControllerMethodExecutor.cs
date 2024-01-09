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
        private static readonly MethodInfo InitializeSetter = BaseController.GetMethod(nameof(Controller.Initialize), new Type[] { typeof(SimpleWSession), typeof(HttpRequest) });

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

        /// <summary>
        /// The Expression Tree to Execute Method with Parameters
        /// </summary>
        public readonly Action<SimpleWSession, HttpRequest, object[]> ExecuteMethod;

        /// <summary>
        /// Parameters to pass to the ExecuteMethod
        /// </summary>
        public readonly Dictionary<ParameterInfo, object> Parameters;

        /// <summary>
        /// Initialize a new instance
        /// </summary>
        /// <param name="executeMethod"></param>
        /// <param name="parameters"></param>
        private ControllerMethodExecutor(Action<SimpleWSession, HttpRequest, object[]> executeMethod, Dictionary<ParameterInfo, object> parameters) {
            ExecuteMethod = executeMethod;
            Parameters = parameters;
        }

        /// <summary>
        /// Create a ControllerMethodExecutor
        /// </summary>
        /// <param name="method">The MethodInfo to add</param>
        /// <returns></returns>
        public static ControllerMethodExecutor Create(MethodInfo method) {
            var controllerType = method.ReflectedType;
            var body = new List<Expression>();
            var locals = new List<ParameterExpression>();

            // creation variable C# : `ControllerType controller;`
            var controller = Expression.Variable(controllerType);
            // add to expression tree locals
            locals.Add(controller);

            // assign variable C# : `ControllerType controller = new ControllerType();`
            // add to expression tree body
            body.Add(Expression.Assign(controller, Expression.New(controllerType)));

            // lambda parameters
            var sessionInLambda = Expression.Parameter(typeof(SimpleWSession));
            var requestInLambda = Expression.Parameter(typeof(HttpRequest));
            var argsInLambda = Expression.Parameter(typeof(object[]));

            // call method C# : `controller.Initialize(session, request);`
            // controller is the instance controller type
            // InitializeSetter is a methodinfo pointing to controller.Initialize()
            // sessionInLambda is the lambda parameter pointing to session
            // requestInLambda is the lambda parameter pointing to request
            // add to expression tree body
            body.Add(Expression.Call(controller, InitializeSetter, new List<ParameterExpression>() { sessionInLambda, requestInLambda }));

            // call method C# : `Controller.OnBeforeMethod();`
            body.Add(Expression.Call(controller, OnBeforeMethod));

            //
            // get method parameter
            //
            var parameters = method.GetParameters();
            var parameterCount = parameters.Length;
            var callParameters = new List<Expression>();
            var values = new Dictionary<ParameterInfo, object>();

            for (var i = 0; i < parameterCount; i++) {
                var parameter = parameters[i];

                callParameters.Add(
                    Expression.Convert(Expression.ArrayIndex(argsInLambda, Expression.Constant(i)), parameter.ParameterType)
                );

                bool hasDefaultValue;
                var tryToGetDefaultValue = true;
                object defaultValue = null;

                try {
                    hasDefaultValue = parameter.HasDefaultValue;
                }
                catch (FormatException) when (parameter.ParameterType == typeof(DateTime)) {
                    // Workaround for https://github.com/dotnet/corefx/issues/12338
                    // If HasDefaultValue throws FormatException for DateTime
                    // we expect it to have default value
                    hasDefaultValue = true;
                    tryToGetDefaultValue = false;
                }

                if (hasDefaultValue) {
                    if (tryToGetDefaultValue) {
                        defaultValue = parameter.DefaultValue;
                    }

                    // Workaround for https://github.com/dotnet/corefx/issues/11797
                    if (defaultValue == null && parameter.ParameterType.IsValueType) {
                        defaultValue = Activator.CreateInstance(parameter.ParameterType);
                    }

                    //callParameters.Add(Expression.Constant(defaultValue));
                    values.Add(parameter, defaultValue);
                }
                else {
                    //callParameters.Add(Expression.Default(parameter.ParameterType));
                    values.Add(parameter, parameter.ParameterType);
                }
            }

            //
            // call method
            //
            Expression call = Expression.Call(controller, method, callParameters);

            // specific reponse if : Task method()
            if (method.ReturnType == typeof(Task)) {
            }
            // specific reponse if : void method()
            else if (method.ReturnType == typeof(void)) {
                // convert void to Task by evaluating Task.CompletedTask
                //call = Expression.Block(typeof(Task), call, Expression.Constant(Task.CompletedTask));
            }
            // all others : T method()
            else {
                // call SendResponseAsync()
                call = Expression.Call(controller, SendObjectResponseAsync,
                                       new List<Expression>() {
                                           Expression.Convert(call, typeof(object))
                                       }
                );
            }
            body.Add(call);

            // call method C# : `(controller as IDisposable).Dispose();`
            if (typeof(IDisposable).IsAssignableFrom(controllerType)) {
                body.Add(Expression.Call(Expression.TypeAs(controller, typeof(IDisposable)), DisposeMethod));
            }

            // final lambda
            var lambda = Expression.Lambda<Action<SimpleWSession, HttpRequest, object[]>>(Expression.Block(locals, body), sessionInLambda, requestInLambda, argsInLambda);

            return new ControllerMethodExecutor(lambda.Compile(), values);
        }

        #region helper

        /// <summary>
        /// Return all Class that inherited from BaseController Class
        /// </summary>
        /// <param name="excepts">List of Controller to not auto load</param>
        /// <returns></returns>
        public static IEnumerable<Type> Controllers(IEnumerable<Type> excepts = null) {
            return AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes())
                                                .Where(p => BaseController.IsAssignableFrom(p))
                                                .Where(t => BaseController != t)
                                                .Where(t => excepts == null || !excepts.Contains(t));
        }

        #endregion helper

    }

}
