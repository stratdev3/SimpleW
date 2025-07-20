using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Interface for SimpleWSession
    /// </summary>
    public interface ISimpleWSession : IWebSocketSession, IHttpSession {
    
        /// <summary>
        /// The jwt string
        /// </summary>
        string jwt { get; set; }

        /// <summary>
        /// The webuser
        /// </summary>
        IWebUser webuser { get; set; }

        /// <summary>
        /// Set the current Session as SSE Session
        /// and add it to the server SSESessions
        /// </summary>
        public void AddSSESession();

    }

}
