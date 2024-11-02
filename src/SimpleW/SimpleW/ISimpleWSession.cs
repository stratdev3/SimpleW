using NetCoreServer;


namespace SimpleW {

    public interface ISimpleWSession : IWebSocketSession, IHttpSession {
    
        string jwt { get; set; }

        IWebUser webuser { get; set; }

    }

}
