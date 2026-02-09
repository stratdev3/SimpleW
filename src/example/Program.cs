using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SimpleW;
using SimpleW.Modules;
using SimpleW.JsonEngine.Newtonsoft;
using SimpleW.Helper.Razor;
using SimpleW.Service.Firewall;
using SimpleW.Security;
using SimpleW.Service.Swagger;
using SimpleW.Service.Chaos;
using SimpleW.Service.Latency;


namespace example.rewrite {

    /// <summary>
    /// Example Program
    /// perf http : bombardier -c 200 -d 10s http://127.0.0.1:8080/api/test/hello
    /// perf https : bombardier -c 200 -d 10s -k https://127.0.0.1:8080/api/test/hello
    /// perf jwt : bombardier -c 200 -d 10s -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJjaGVmIiwiaWF0IjoxNzY3NDQxOTA4LCJleHAiOjE3Njc0NDkxMDgsInJvbGVzIjpbImJvc3MiXX0.qv9TnM7RKM1zu_QcPoTz_pmXMDzHT-OnP6xjs4nMgS4" http://127.0.0.1:8080/api/jwt/decode
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

            #region https

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
                X509Certificate2 cert = new(certificateFilePath, "password");
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

            #endregion https

            server.MapGet("/api/syslog/index", object (string? query = null) => {
                return new { query };
            });

            server.MapGet("/", () => {
                return "Hello World !";
            });

            server.MapGet("/api/test/hello", object (HttpSession session) => {
                return new { message = $"Hello World !" };
            });

            server.MapGet("/api/test/async", async (HttpSession session) => {
                try {
                    session.RequestAborted.Register(() =>
                        Console.WriteLine($"[ABORTED] session={session.Id} t={Environment.TickCount64}")
                    );

                    Console.WriteLine($"[START] session={session.Id} t={Environment.TickCount64}");

                    await Task.Delay(TimeSpan.FromSeconds(30), session.RequestAborted);

                    Console.WriteLine($"[END] session={session.Id} t={Environment.TickCount64}");
                    
                }
                catch (OperationCanceledException) when (session.RequestAborted.IsCancellationRequested) {
                    Console.WriteLine($"[OperationCanceledException] session={session.Id} t={Environment.TickCount64}");
                }
                catch (Exception ex) {
                    Console.WriteLine($"[Exception] session={session.Id} t={Environment.TickCount64}");
                }
                return new { message = "Hello World!" };
            });

            //server.UseStaticFilesModule(options => {
            //    options.Prefix = "/spa";
            //    options.Path = @"C:\www\spa\spa";
            //    //options.CacheTimeout = TimeSpan.FromMinutes(10);
            //});

            //server.UseLatencyModule(options => {
            //    options.GlobalLatency = TimeSpan.FromSeconds(1);
            //    options.Rules.Add(new LatencyRule("/api/*", TimeSpan.FromSeconds(2)));
            //});

            //server.UseRazorModule(o => {
            //    o.ViewsPath = @"C:\www\toto\views";
            //});
            //server.MapGet("/", () => {
            //    return RazorResults.View("Home", new { Title = "SimpleW", Name = "chef" })
            //                       .WithViewBag(vb => {
            //                           vb.Title = "Ma room";
            //                           vb.Footer = "SimpleW";
            //                       });
            //});

            //server.UseFirewallModule(options => {
            //    options.AllowRules.Add(IpRule.Cidr("192.168.1.0/24"));
            //    options.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));
            //    options.AllowRules.Add(IpRule.Single("127.0.0.1"));

            //    options.ClientIpResolver = (HttpSession session) => {

            //        // look for any X-Real-IP header (note: you should check this value come from a trust proxy)
            //        if (session.Request.Headers.TryGetValue("X-Real-IP", out string? XRealIp)) {
            //            return IPEndPoint.Parse(XRealIp).Address;
            //        }

            //        // client ip
            //        if (session.Socket.RemoteEndPoint is not IPEndPoint ep) {
            //            return null;
            //        }
            //        return ep.Address;
            //    };

            //});

            //server.UseStaticFilesModule(options => {
            //    options.Path = @"C:\www\spa\sse\";
            //    options.Prefix = "/";
            //    options.AutoIndex = true;
            //    options.CacheTimeout = TimeSpan.FromDays(1);
            //});

            //server.UseChaosModule(options => {
            //    options.Enabled = true;
            //    options.Prefix = "/api";
            //    options.Probability = 0.50;
            //});


            //server.UseSwaggerModule(options => {
            //    options.Title = "My API";
            //    options.Version = "v1";
            //    // optionnel: ne documenter que /api/*
            //    //options.RouteFilter = r => r.Path.StartsWith("/", StringComparison.Ordinal);
            //});

            server.UseWebSocketModule(ws => {
                ws.Prefix = "/ws";

                // Optionnel: si tu ne veux PAS le "__all" par defaut
                ws.AutoJoinRoom = null;

                // Petit "state" par connexion (alpha style)
                var userByConn = new ConcurrentDictionary<Guid, string>();
                var roomByConn = new ConcurrentDictionary<Guid, string>();

                static string ChatEvent(string kind, string room, string name, string? text = null) {
                    var payload = new { kind, room, name, text = text ?? "" };
                    var obj = new { op = "chat/event", payload };
                    return JsonSerializer.Serialize(obj);
                }

                ws.Map("chat/join", async (conn, ctx, msg) => {
                    if (!msg.TryGetPayload(out RoomName? m) || m == null) {
                        return;
                    }

                    userByConn[conn.Id] = m.name;
                    roomByConn[conn.Id] = m.room;

                    await ctx.JoinRoomAsync(m.room, conn);

                    // Broadcast aux autres: "X joined"
                    await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("join", m.room, m.name, $"{m.name} joined"), except: conn);

                    // Ack au client (facultatif)
                    await conn.SendTextAsync(ChatEvent("join", m.room, m.name, $"joined {m.room}"));
                });

                ws.Map("chat/leave", async (conn, ctx, msg) => {
                    if (!msg.TryGetPayload(out RoomName? m) || m == null) {
                        return;
                    }

                    await ctx.LeaveRoomAsync(m.room, conn);
                    await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("leave", m.room, m.name, $"{m.name} left"), except: conn);

                    roomByConn.TryRemove(conn.Id, out _);
                });

                ws.Map("chat/msg", async (conn, ctx, msg) => {
                    if (!msg.TryGetPayload(out RoomName? m) || m == null) {
                        return;
                    }

                    // Broadcast à toute la room (y compris l'émetteur ou non, au choix)
                    await ctx.Hub.BroadcastTextAsync(m.room, ChatEvent("msg", m.room, m.name, m.text));
                });

                ws.OnUnknown(async (conn, ctx, msg) => {
                    // Pratique quand tu débugges
                    await conn.SendTextAsync(msg.IsJson ? $"unknown op: {msg.Op}" : "bad message: expected JSON {op,payload}");
                });

                ws.OnConnect = async (conn, ctx) => {
                    IWebUser? user = ctx.Session.Request.User;
                    Console.WriteLine($"websocket connect {user?.Id} {user?.Login}");
                    if (user == null) {
                        return;
                    }
                    //await ctx.JoinRoomAsync(user.Id.ToString(), conn);
                    //if (user.IsInRoles("admin, task:admin")) {
                    //    await ctx.JoinRoomAsync("task", conn);
                    //}
                };
                ws.OnDisconnect = async (conn, ctx) => {
                    // cleanup
                    if (roomByConn.TryRemove(conn.Id, out var room) && userByConn.TryRemove(conn.Id, out var name)) {
                        await ctx.Hub.BroadcastTextAsync(room, ChatEvent("leave", room, name, $"{name} disconnected"), except: conn);
                    }
                };
            });

            ServerSentEventsHub? hub = null;

            server.UseServerSentEventsModule(opt => {
                opt.Prefix = "/sse";
                opt.AllowAnyOrigin = true;                  // pratique si tu ouvres index.html en file:// ou autre origin
                opt.AutoJoinRoom = "__all";                 // tout le monde dans la room globale
                opt.KeepAliveInterval = TimeSpan.FromSeconds(15);

                hub = opt.Hub;

                opt.OnConnect = async (conn, ctx) => {
                    // Message de bienvenue (optionnel)
                    await conn.SendEventAsync("connected", @event: "status");
                };
            });

            // Timer: broadcast toutes les 5 secondes
            _ = Task.Run(async () => {
                int i = 0;
                while (true) {
                    await Task.Delay(TimeSpan.FromSeconds(5));

                    if (hub == null)
                        continue;

                    i++;
                    await hub.BroadcastAsync("__all", new ServerSentEventsMessage {
                        Event = "tick",
                        Payload = $"tick #{i} @ {DateTime.UtcNow:O}"
                    });
                }
            });

            //server.MapControllers<SubController>("/api");
            //server.MapController<HomeController>("/");

            server.Configure(options => {
                options.ReuseAddress = true;
                options.TcpNoDelay = true;
                options.TcpKeepAlive = true;
                options.AcceptPerCore = true;
                //options.SocketDisconnectPollInterval = TimeSpan.Zero;
                options.JwtOptions = new JwtOptions("azertyuiopqsdfghjklmwxcvbn") {
                    ValidateExp = false,
                    ValidateNbf = false,
                };
            });
            //server.ConfigureTelemetry(options => {
            //    options.IncludeStackTrace = true;
            //});
            //server.EnableTelemetry();

            //openTelemetryObserver("SimpleW*");

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

        private static TracerProvider? _tracerProvider;
        private static MeterProvider? _meterProvider;

        public static void openTelemetryObserver(string source) {
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                                 .AddSource(source)
                                 //.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.01)))
                                 //.AddProcessor(new LogProcessor()) // custom log processor
                                 .AddOtlpExporter((options) => {
                                     options.Endpoint = new Uri("https://api.uptrace.dev/v1/traces");
                                     options.Headers = "uptrace-dsn=https://CqLctwdpOwM0Ayv64cKzVg@api.uptrace.dev?grpc=4317";
                                     options.Protocol = OtlpExportProtocol.HttpProtobuf;
                                 })
                                 .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(serviceName: "Sample", serviceVersion: "26"))
                                 .Build();

            _meterProvider = Sdk.CreateMeterProviderBuilder()
                            //.AddMeter("*") // all meters
                            .AddMeter(source) // only my meters
                            .SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(serviceName: "Sample", serviceVersion: "26"))
                            .AddOtlpExporter((exporterOptions, metricReaderOptions) => {
                                exporterOptions.Endpoint = new Uri("https://api.uptrace.dev/v1/metrics");
                                exporterOptions.Headers = "uptrace-dsn=https://CqLctwdpOwM0Ayv64cKzVg@api.uptrace.dev?grpc=4317";
                                exporterOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                                metricReaderOptions.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                                //metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                            })
//#if DEBUG
//                          .AddConsoleExporter((consoleExporterOptions, metricReaderOptions) => {
//                              metricReaderOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = ExportIntervalMilliseconds;
//                          })
//#endif
                            .Build();
        }

    }

    class RoomName {
        public string room { get; set; }
        public string name { get; set; }
        public string text { get; set; }
    }

    public sealed class HomeController : Controller {

        [Route("GET", "home")]
        public object Index(string? name = null) {
            return "Home";
        }

    }

    public sealed class HomeRazorController : RazorController {

        [Route("GET", "/index")]
        public object Index() {
            return View("Home", new { Title = "SimpleW", Name = "chef" })
                   .WithViewBag(vb => {
                        //vb.Title = "Ma room";
                        vb.Footer = "SimpleW";
                    });
        }

    }

    class LogProcessor : BaseProcessor<Activity> {
        // write log to console
        public override void OnEnd(Activity activity) {
            // WARNING : use for local debug only not production
            Console.WriteLine(
                 $"{activity.GetTagItem("http.request.method")} " +
                 $"\"{activity.GetTagItem("url.path")}" +
                 $"{(!string.IsNullOrWhiteSpace(activity.GetTagItem("url.query")?.ToString()) ? "?"+activity.GetTagItem("url.query") : "")}" +
                 $"\" " +
                 $"{activity.GetTagItem("http.response.status_code")} " +
                 $"{(int)activity.Duration.TotalMilliseconds}ms "
            );

        }
    }

}
