using System.Collections.Generic;
using System;
using System.Collections.Concurrent;
using NetCoreServer;


namespace SimpleW {

    public interface ISimpleWServer {

        Router Router { get; }

        string DefaultDocument { get; set; }

        bool AutoIndex { get; set; }

        void AddMimeTypes(string extension, string contentType);

        void AddDynamicContent(string path = "/", IEnumerable<Type> excludes = null);
        void AddDynamicContent(Type controllerType, string path = "/");

        void SetToken(string tokenPassphrase, string issuer, DelegateSetTokenWebUser getWebUserCallback = null);

        void AddWebSocketContent(string path = "/websocket", IEnumerable<Type> excepts = null);
        void AddWebSocketContent(Type controllerType, string path = "/websocket");

        ConcurrentDictionary<Guid, IWebUser> WebUsers { get; }
        IEnumerable<IWebSocketSession> AllWebUsers(Func<KeyValuePair<Guid, IWebUser>, bool> where);

        IWebUser FindWebUser(Guid id);
        void RegisterWebUser(Guid id, IWebUser webuser);
        void UnregisterWebUser(Guid id);

        string cors_allow_origin { get; set; }
        string cors_allow_headers { get; set; }
        string cors_allow_methods { get; set; }
        string cors_allow_credentials { get; set; }

        void AddCORS(string origin = "*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials = "true");

    }

}
