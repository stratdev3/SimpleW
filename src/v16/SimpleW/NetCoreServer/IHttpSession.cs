using System;

namespace NetCoreServer
{

    /// <summary>
    /// IHttpSession interface
    /// </summary> 
    public interface IHttpSession
    {

        HttpRequest Request { get; }

        HttpResponse Response { get; }


        long SendResponse();
        long SendResponse(HttpResponse response);
        long SendResponseBody(string body);
        long SendResponseBody(ReadOnlySpan<char> body);
        long SendResponseBody(byte[] buffer);
        long SendResponseBody(byte[] buffer, long offset, long size);
        long SendResponseBody(ReadOnlySpan<byte> buffer);
        bool SendResponseAsync();
        bool SendResponseAsync(HttpResponse response);
        bool SendResponseBodyAsync(string body);
        bool SendResponseBodyAsync(ReadOnlySpan<char> body);
        bool SendResponseBodyAsync(byte[] buffer);
        bool SendResponseBodyAsync(byte[] buffer, long offset, long size);
        bool SendResponseBodyAsync(ReadOnlySpan<byte> buffer);

    }

}
