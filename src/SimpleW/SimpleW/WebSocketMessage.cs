using System;


namespace SimpleW {

    /// <summary>
    /// WebSocketMessage is a class used between
    /// client and server to communicate data
    /// in a more structured form.
    /// </summary>
    public class WebSocketMessage {

        public string url { get; set; }

        public string entity { get; set; }

        public string action { get; set; }

        public object data { get; set; }

        public DateTime datetime { get; set; }
        
        public string jwt { get; set; }

    }

}
