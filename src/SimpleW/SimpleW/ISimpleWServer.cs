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

        Router Router { get; }

        #region static

        string DefaultDocument { get; set; }

        bool AutoIndex { get; set; }

        void AddMimeTypes(string extension, string contentType);

        #endregion static

        #region dynamic

        void AddDynamicContent(string path = "/", IEnumerable<Type> excludes = null);
        void AddDynamicContent(Type controllerType, string path = "/");

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

        string cors_allow_origin { get; set; }
        string cors_allow_headers { get; set; }
        string cors_allow_methods { get; set; }
        string cors_allow_credentials { get; set; }

        void AddCORS(string origin = "*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials = "true");

        #endregion cors

    }

}
