using System;

namespace NetCoreServer
{
    /// <summary>
    /// IWebSocketSession interface
    /// </summary> 
    public interface IWebSocketSession : IWebSocket
    {

        #region WebSocket send text methods

        long SendText(string text);
        long SendText(ReadOnlySpan<char> text);
        long SendText(byte[] buffer);
        long SendText(byte[] buffer, long offset, long size);
        long SendText(ReadOnlySpan<byte> buffer);
        bool SendTextAsync(string text);
        bool SendTextAsync(ReadOnlySpan<char> text);
        bool SendTextAsync(byte[] buffer);
        bool SendTextAsync(byte[] buffer, long offset, long size);
        bool SendTextAsync(ReadOnlySpan<byte> buffer);

        #endregion

    }
}
