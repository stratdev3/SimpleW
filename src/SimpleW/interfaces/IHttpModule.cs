namespace SimpleW.Modules {

    /// <summary>
    /// IHttpModule
    /// </summary>
    public interface IHttpModule {

        /// <summary>
        /// Callback invoked by <see cref="SimpleWServer.UseModule"/> to install the module.
        /// </summary>
        /// <param name="server"></param>
        void Install(SimpleWServer server);

    }

}
