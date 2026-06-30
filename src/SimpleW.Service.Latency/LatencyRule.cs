namespace SimpleW.Service.Latency {

    /// <summary>
    /// LatencyRule
    /// </summary>
    /// <param name="Path"></param>
    /// <param name="Latency"></param>
    public readonly record struct LatencyRule(string Path, TimeSpan Latency);

}
