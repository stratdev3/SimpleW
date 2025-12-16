namespace SimpleW {

    /// <summary>
    /// IHttpModule
    /// </summary>
    public interface IHttpModule {

        /// <summary>
        /// Callback to Install Module in server
        /// </summary>
        /// <param name="server"></param>
        void Install(SimpleWServer server);

    }

}
