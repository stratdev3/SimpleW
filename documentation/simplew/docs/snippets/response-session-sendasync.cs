using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/raw")]
        public async ValueTask Raw() {
            return Session.SendAsync(RawResponse());
        }

        private byte[] RawResponse() {

            var headerBuilder = new StringBuilder();
            headerBuilder.Append("HTTP/1.1 200 OK\r\n");
            headerBuilder.Append("Content-Type: application/json; charset=utf-8\r\n");
            headerBuilder.Append("Content-Length: ");
            headerBuilder.Append(Encoding.UTF8.GetByteCount("""{ "message": "Hello world !" }"""));
            headerBuilder.Append("\r\n");
            headerBuilder.Append("\r\n");

            var headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
            var bodyBytes = Encoding.UTF8.GetBytes(body);

            data = new byte[headerBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, data, 0, headerBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, data, headerBytes.Length, bodyBytes.Length);

            return data;
        }

    }

}