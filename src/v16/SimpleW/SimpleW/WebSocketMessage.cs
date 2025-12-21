using System;


namespace SimpleW {

    /// <summary>
    /// WebSocketMessage is a class used between
    /// client and server to communicate data
    /// in a more structured form.
    /// </summary>
    public class WebSocketMessage {

        // relative url from websocket endpoint to method
        public string url { get; set; }

        public object body { get; set; }

        public DateTime datetime { get; set; }
        
        public string jwt { get; set; }

    }

}
