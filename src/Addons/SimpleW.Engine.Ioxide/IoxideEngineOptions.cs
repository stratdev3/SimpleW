using ioxide;
using ioxide.tls;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Minimal options for the alpha ioxide engine.
    /// </summary>
    public sealed class IoxideEngineOptions {

        /// <summary>
        /// Native ioxide configuration. The SimpleW port replaces Tcp.Port.
        /// </summary>
        public ServerConfig ServerConfig { get; set; } = new() {
            ReactorCount = Environment.ProcessorCount,
            Udp = null,
            Quic = null
        };

        /// <summary>
        /// Stage every response produced from one read and send it with one flush.
        /// </summary>
        public IoxideFlushPolicy FlushPolicy { get; set; } = IoxideFlushPolicy.EndOfReadBatch;

        /// <summary>
        /// Native OpenSSL TLS configuration. Null keeps every TCP listener in plaintext.
        /// When configured, TLS applies to the main port and every additional TCP port.
        /// </summary>
        public TlsOptions? Tls { get; set; }

    }

    /// <summary>
    /// Specifies when buffered ioxide writes are flushed to the network.
    /// </summary>
    public enum IoxideFlushPolicy {

        /// <summary>
        /// Flushes each completed write immediately.
        /// </summary>
        Immediate,

        /// <summary>
        /// Defers the flush until SimpleW finishes processing the current read batch.
        /// </summary>
        EndOfReadBatch

    }

}
