using ioxide;
using ioxide.tls;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// Options for the default ioxide engine.
    /// </summary>
    public sealed class IoxideEngineOptions {

        /// <summary>
        /// ServerConfig
        /// </summary>
        public ServerConfig ServerConfig { get; set; } = CreateSharedRingConfig();

        /// <summary>
        /// Dedicated kTLS listener port.
        /// </summary>
        public ushort TlsPort { get; set; } = 443;

        /// <summary>
        /// kTLS configuration for the ioxide TLS listener.
        /// When set, the engine starts TlsService on each reactor and routes TlsPort through ioxide.tls.
        /// </summary>
        public TlsOptions? Tls { get; set; }

        /// <summary>
        /// Engine execution mode.
        /// </summary>
        public IoxideEngineMode Mode { get; set; } = IoxideEngineMode.SimpleW;

        /// <summary>
        /// Flush policy used by the real SimpleW mode.
        /// EndOfReadBatch keeps the response path hot by staging writes and flushing once after the current read batch.
        /// </summary>
        public IoxideFlushPolicy FlushPolicy { get; set; } = IoxideFlushPolicy.EndOfReadBatch;

        /// <summary>
        /// Maximum request header size used by ParserTest mode.
        /// </summary>
        public int ParserTestMaxRequestHeaderSize { get; set; } = 64 * 1024;

        /// <summary>
        /// Maximum request body size used by ParserTest mode.
        /// </summary>
        public long ParserTestMaxRequestBodySize { get; set; } = 10 * 1024 * 1024;

        #region helpers

        /// <summary>
        /// Default high-throughput shared-ring configuration.
        /// SimpleW uses ioxide pipes on top of this mode so unconsumed request bytes stay zero-copy.
        /// </summary>
        /// <returns></returns>
        public static ServerConfig CreateSharedRingConfig() => new() {
            ReactorCount = Environment.ProcessorCount,
            RingEntries = 8192,
            ListenBacklog = 8192,
            RecvBufferSize = 32 * 1024,
            BufferRingEntries = 4096,
            WriteSlabSize = 16 * 1024,
            PoolMax = 1024,
            RecvQueueEntries = 64,
            Incremental = false,
            MaxConnections = 4096,
            ConnBufRingEntries = 16,
            IncRecvBufferSize = 4096,
            WriteOverflow = WriteOverflowStrategy.Segmented
        };

#if DEBUG

        /// <summary>
        /// High-throughput incremental buffer-ring configuration for Linux kernel 6.12+.
        /// </summary>
        /// <returns></returns>
        public static ServerConfig CreateIncrementalConfig() => CreateSharedRingConfig() with {
            Incremental = true
        };

#endif

        #endregion helpers

    }

}
