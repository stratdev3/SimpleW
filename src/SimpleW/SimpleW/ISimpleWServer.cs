using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Interface for SimpleWServer
    /// </summary>
    public interface ISimpleWServer {

        /// <summary>
        /// Main Router instance
        /// </summary>
        Router Router { get; }

        #region static

        string DefaultDocument { get; set; }

        bool AutoIndex { get; set; }

        void AddMimeTypes(string extension, string contentType);

        #endregion static

        #region func

        /// <summary>
        /// Add Func content for GET request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="handler"></param>
        void MapGet(string url, Delegate handler);

        /// <summary>
        /// Add Func content for POST request
        /// </summary>
        /// <param name="url"></param>
        /// <param name="handler"></param>
        void MapPost(string url, Delegate handler);

        #endregion func

        #region dynamic

        /// <summary>
        /// Add dynamic content by registered all controllers which inherit from Controller
        /// </summary>
        /// <param name="path">path (default is "/")</param>
        /// <param name="excludes">List of Controller to not auto load</param>
        void AddDynamicContent(string path = "/", IEnumerable<Type> excludes = null);

        /// <summary>
        /// Add dynamic content for a controller type which inherit from Controller
        /// </summary>
        /// <param name="controllerType">controllerType</param>
        /// <param name="path">path (default is "/")</param>
        void AddDynamicContent(Type controllerType, string path = "/");

        /// <summary>
        /// Set Token settings (passphrase and issuer).
        /// a delegate can be defined to redress webuser called by Controller.JwtToWebUser().
        /// </summary>
        /// <param name="tokenPassphrase">The String token secret passphrase (min 17 chars).</param>
        /// <param name="issuer">The String issuer.</param>
        /// <param name="getWebUserCallback">The DelegateSetTokenWebUser getWebUserCallback</param>
        void SetToken(string tokenPassphrase, string issuer, DelegateSetTokenWebUser getWebUserCallback = null);

        #endregion dynamic

        #region sse

        public void AddSSESession(ISimpleWSession session);

        public void RemoveSSESession(ISimpleWSession session);

        public void BroadcastSSESessions(string evt, string data, Expression<Func<ISimpleWSession, bool>> filter = null);

        #endregion sse

        #region websocket

        void AddWebSocketContent(string path = "/websocket", IEnumerable<Type> excepts = null);
        void AddWebSocketContent(Type controllerType, string path = "/websocket");

        ConcurrentDictionary<Guid, IWebUser> WebSocketUsers { get; }
        IEnumerable<IWebSocketSession> AllWebSocketUsers(Func<KeyValuePair<Guid, IWebUser>, bool> where);

        IWebUser FindWebSocketUser(Guid id);
        void RegisterWebSocketUser(Guid id, IWebUser webuser);
        void UnregisterWebSocketUser(Guid id);

        #endregion websocket

        #region cors

        /// <summary>
        /// CORS Header Origin
        /// </summary>
        string cors_allow_origin { get; set; }

        /// <summary>
        /// CORS Header headers
        /// </summary>
        string cors_allow_headers { get; set; }

        /// <summary>
        /// CORS Header methods
        /// </summary>
        string cors_allow_methods { get; set; }

        /// <summary>
        /// CORS Header credentials
        /// </summary>
        string cors_allow_credentials { get; set; }

        /// <summary>
        /// Setup CORS
        /// </summary>
        /// <param name="origin">Access-Control-Allow-Origin</param>
        /// <param name="headers">Access-Control-Allow-Headers</param>
        /// <param name="methods">Access-Control-Allow-Methods</param>
        /// <param name="credentials">Access-Control-Allow-Credentials</param>
        void AddCORS(string origin = "*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials = "true");

        #endregion cors

        #region telemetry

        /// <summary>
        /// True to enable telemetry
        /// </summary>
        bool EnableTelemetry { get; set; }

        #endregion telemetry

    }

}
