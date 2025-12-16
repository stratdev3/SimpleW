using System.Net;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Xsl;
using SimpleW;


namespace example.rewrite {

    /// <summary>
    /// Example Program
    /// perf http : bombardier -c 200 -d 10s http://127.0.0.1:8080/api/test/hello
    /// perf https : bombardier -c 200 -d 10s -k https://127.0.0.1:8080/api/test/hello
    /// </summary>
    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {
            await Rewrite();
            //await Raw();
        }

        static async Task Rewrite() {

            SimpleWServer server = new(IPAddress.Any, 8080);

#pragma warning disable CS0162 // Code inaccessible détecté
            if (false) {
                // echo subjectAltName=DNS:localhost,IP:127.0.0.1 > san.cnf && openssl genrsa -out privkey.pem 2048 && openssl req -new -key privkey.pem -out cert.csr -subj "/C=FR/O=SimpleW/OU=Dev/CN=localhost/emailAddress=admin@localhost" && openssl x509 -req -days 1095 -in cert.csr -signkey privkey.pem -out cert.pem -extfile san.cnf && openssl pkcs12 -export -out simplew.pfx -inkey privkey.pem -in cert.pem -password pass:secret && del san.cnf
                string SslCertificateBase64 = "MIIKbwIBAzCCCiUGCSqGSIb3DQEHAaCCChYEggoSMIIKDjCCBHoGCSqGSIb3DQEHBqCCBGswggRnAgEAMIIEYAYJKoZIhvcNAQcBMF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBCcgkF/1RdTBBLJelCgFuYIAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFlAwQBKgQQWpDb8pHZGBkFm9S7F2U3ioCCA/Djx1HfZ7wklQ+owrhfpzLsXUALYPQhz/A8FsvqnR/mwAm3Z6kQ+Etj8LBLd1E8S7d2PWa4MK2QIXmusmLQm+5ElOTEqq3aFVwcEx5WKPFm9FRWEKZGOYgEwmfIbRHA5CDqeBIli3TLEaUt+7huYRjem/kY9zsSsAtat/fvnbl1zcbhOrqn2AqLZuiXbgr/iv3Pl1atL8ZJnxMNyGkFAzjy7k9x6wcResyJrmn5tOpOeOEkdMo8qtPGlrdAnUJxBFK/dApRaLcbJN7SAxo8WymS2mME/d10pArmnY7TLaQUzr0ZAOpqL9Wx2e5cadigMzKhumPNhHsIlr5JPr/y6y+khKyemGhUvpFbQoIr4jqLAyBA16nDITL1y2YT+xBhW5bKAcCK7us9gy7GIQ0ilS58UGUNogwr1/pqxhvtaTX4rlrHclLgYa6BD42q+50b90ji08txx+csp0MxMv+pljoNIKuPLFjWtTKI1YQPqIwMyybv6zZlqjfeoXfK3ng/FoeHLFKFwNXuSBUwvfyFJAo0e7nbPOLSliVtbV90JJJyQggqpvyvlWfPJZGKyjnMNkxb/5VGsC6EPLHf+0tgNQQ3Ahn2/J3DPLs4E3F+85utCDOmEnbC/x+kFd6oT8t5HdEq1QunYImo5F4bKoQ5FJ49Duggdj3l8RxMVZIQ9y9oEJU13jnu/crqfln69B6BQJdwbUjeeeI/ppJSeKo3y6Y4tAFLXlGeKlvfNGp1oxB7OPjfUz8TK3Z6eiTKCetJ2fpDTz3Xz4i1oZylH90BKR0USEIs/p8nPinbaUSgNyQI/6r9sMfpzEvnYFNluFg2V4cvo6xwCOUw9OR7/d32nIAZv4TXamA0ahtT969fvnHqpXaLdUJYQP9t5081BcATky1g0ozHWbmsrGJFRPJ7eCcEIse1PFmm7UbFR1l4rx7Yju/hQpGTTZl8Nlsu8YTSWMbkC6tSVKxaMoU/kOLZNURJOhJGJfVj798Po50tsjDtaS/PH4oyRGFs63aiR1MfXOpBov3yardC4+Rt00oAEwifxNSZ4ht7pgBxp9JQ3qpswvQpgTKTOo0LCkiw08B3osUwFOrmB5/F5gefKXXmfiGI1MPCoNXwifRILWVY2mxJtLqbQ6I8u4Tmc+rDoUn2iGPQkyvi3kBb7YA+tMtvdus+HfLY9Tn5jR570KU9+zvkJfWX/YVcVyUSXU++ol1R+mvzrazrBpGnuw5GsyciLX2+3Rs7gyKQZ8RpEfKNcgr5y7wj7xxVoOPisd0Y23MD11/nUewsPzrv40uHZAHQzwvmNN5/txK0LzethYQyunRzMtpg6RW34CMSKphncSI27FAwggWMBgkqhkiG9w0BBwGgggV9BIIFeTCCBXUwggVxBgsqhkiG9w0BDAoBAqCCBTkwggU1MF8GCSqGSIb3DQEFDTBSMDEGCSqGSIb3DQEFDDAkBBDFlr/U1Yd049XYXSNJ07MIAgIIADAMBggqhkiG9w0CCQUAMB0GCWCGSAFlAwQBKgQQ+AQwfFMD9dDLyFGBNg+KQgSCBNAnklPvRv1RTwRee/xKaJs69gqTpCcmBc9ctr9pkJDYsAN1BeZBYJ3BEbOqBvUS7oWhoLV/c7lNVpU2Vc2hxMlM02iE7p8L28C8WaAVo/2t52dRTSgnRAcjyWTj0LqTMVXsRD6abGSpSTwvczTF1TrAWCamCsirhZ6NrRLoKEXiLO1va5ZymSiyMpWKZOUrVgAc4NdFJwk4KZ5DfmMiB7z2WGoiK/1B3B/tfxjcpwYyvub/lxcH0S4LGbt3wbRmvLcp8IFlzE2y/ljRtVlupmCJGmwkvfoYmjuYvN/qsoarQc/JTbQVgLACITZCNINRvCxMv0t2Gr1qnV3nggslZmvQaAYNpxh1pHSXOV0PIZk9rs+MtMGNfKFRf39iW8Y/orZO82u4nm9AdjhCnTYADcyMhMEFb1wDtkIrble+onM81ir5OZJc76FyJoWZdXZFjamO8G8ZeLoRdQoJehcpkiO6ZFcQo/QUek3iRVzk90AU7CucL9gTuUuYMf1J+SSGdjt86sJqKNsS8dWWSK7HmCKdplrlrLjwP4K9jvC8iO4DADR6gsFjcsUQmX//JARQNabdsy1+MVS9Jr7hNznZ/dWZh3Ac+DnBO81CtgSLWn6YkB2YgHvcfwDGNYwFAEqPZIdKyA2FijygDD+ejU1ttqI/bEOHPqGHHBNmcYKsisBjTWKyHlDZlswQ+ZW5KzNopObcNoUUrhugAeGOnJOZKmFH8wxMmXMFnhUzMRnyvHyrcrq14HnaLSxEQ0+JmGiXmQKdiocc21ZgpKK4nvQv4huJV1pveGTMXtFlCjrH83t6UutU3s8xHIvKNVPxT4gVfaZRyuwvVtMgh/JizrFfZcwubyH9LWQT6k/lpNt/Oa/pwEEYACKMYiDO+vYw74a6xZrZv8Qt+IBtaI1fTWP2nPlbNEdjhkfEPIOhOwi/VbQl/eUHTRet58Y2ecCjUFO5ZcggqFolO3m6Xp5Y8MMdfI4TYXQmeohRcn5c0EpGJuXIogkoRGVUoQEsdqqGvmKeUacmKIQOvrsBfmTBjcPfIYMrVzX35rnjr03uatlRYWNFW+gE1DzwI9FKnU+qYdcVzxd0La6eyJ4EdZ7PS6PqELuvdnAKO5ZAEukCmTJHFVLb+xPTasNwWQrLokQ3LT0iKiXAzjz4PVTKZ1YDwj1xZOBOtNHzqgx9NrUC0NYJqCA7o0rtmb+mp8Y28Z+WrbHmNs4g56dhutPijTNMNZoA5nBvSXHfIkDAoQtDMF+NVdsvZFcgG+upxFIpSQh2xlOKyI9kYiZ53qT+5Bas60Gfqabn/iMX49O6ZYZx8ogE5lMh3KmWfQn9eHN4EfTbN0w4zUM7DzN0i0ISZ40DnjOHGVxSKbhlgSgbh9vxTuG0e2B15pDVj6PmaF43kpfPlOzod7pjUdCXw1wRcPIjDUD8SzCqaQ6dtO2G57/mWCg4FeqE25cjWNvaBwJkv0SoNk3yqKnB8aBzACUfoBxsZjhTf6vW+WOtbe041DWOtclqasrV2gF4y30C9QcwKzqmBqhcNREv6Sv482dEEJeNtKdskEqLyjuJYDFtkznkylX/UtjM15nI2OYU2jAUXST1rJOj3n5h7zeO2DUTzyXSIF8EQLkmYmdfQ+ZIEiSg/aHxEG0yRzElMCMGCSqGSIb3DQEJFTEWBBS1A/1FXc32gfjz5hngLiH9tzN8QDBBMDEwDQYJYIZIAWUDBAIBBQAEIFNZerNRQrLd0zxi70nGUE67qXQDF9ErIkihJYmEXfF4BAiM1ucYQzMS2AICCAA=";
                string certificateFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "simplew.pfx");
                Directory.CreateDirectory(Path.Combine(certificateFilePath, ".."));
                File.WriteAllBytes(certificateFilePath, Convert.FromBase64String(SslCertificateBase64));

                // create a context with certificate, support for password protection
#if NET9_0_OR_GREATER
                X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(certificateFilePath, "secret");
#else
                X509Certificate2 cert = new(@"C:\Users\SimpleW\ssl\domain.pfx", "password");
#endif
                SslContext sslContext = new(
                    SslProtocols.Tls12 | SslProtocols.Tls13,
                    cert,
                    clientCertificateRequired: false,
                    checkCertificateRevocation: false
                );
                server.UseHttps(sslContext);
            }
#pragma warning restore CS0162 // Code inaccessible détecté

            server.MapGet("/api/test/hello", static (HttpSession session) => {
                return session.Response.Json(new { message = "Hello World !" }).SendAsync();
            });
            //server.UseModule(new StaticFilesModule(
            //    @"C:\www\toto\embediotest", "/html"
            //    ,timeout: TimeSpan.FromDays(1)
            //) {
            //    AutoIndex = true
            //});

            //server.MapGet("/api/test/hello2", static async (HttpSession session) => {
            //    await session.SendJsonAsync(new { message = "Hello World !" });
            //});
            //server.MapGet("/api/test/hello2", static (HttpSession session, DateTime? date = null) => {
            //    date ??= DateTime.Now;
            //    return session.SendJsonAsync(new { message = $"Hello {date.Value.ToString("o")} !" });
            //});
            //server.MapGet("/api/test/hello3", static (HttpSession session, Guid id = new Guid()) => {
            //    if (id == Guid.Empty) {
            //        id = Guid.NewGuid();
            //    }
            //    return session.SendJsonAsync(new { message = $"Hello {id} !" });
            //});
            server.UseControllers<Controller>("/api");
            //server.MapGet("/api/test/hello3", static (string? name = null) => {
            //    return new { message = $"Hello {name} !" };
            //});
            //server.MapGet("/api/test/hello4", static async (string? name = null) => {
            //    await Task.Delay(2_000);
            //    return new { message = $"Hello {name} !" };
            //});
            //server.MapGet("/api/test/hello5", static async (HttpSession session, string? name = null) => {
            //    await Task.Delay(2_000);
            //    await session.SendJsonAsync(new { message = $"Hello {name} !" });
            //});
            //server.MapGet("/api/test/text", static (HttpSession session) => {
            //    return session.SendTextAsync("Hello World !");
            //});
            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;
            //server.OptionSessionTimeout = TimeSpan.MinValue;
            //server.OptionRunAcceptSocketPerCore = true;
            //server.OptionReceiveStrategy = SimpleW.ReceivedStrategy.ReceiveLoopBuffer;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.RunAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

        static async Task Raw() {
            RawServer.RawServer server = new(IPAddress.Any, 8080);

            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.StartAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

    }

    [Route("/user")]
    public class UserController : Controller {

        [Route("GET", "/details")]
        public object Details(string id = "1", int page = 1) {
            return new {
                Id = id,
                Page = page,
            };
        }
    }

}
