using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CardForge.DedicatedServer
{
    /// <summary>
    /// The ONLY CardForge server executable. Unity directly owns the complete
    /// account, asset, social, matchmaking and authoritative rule execution API.
    /// No legacy .NET gateway or child process is started.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DedicatedServerHost : MonoBehaviour
    {
        [SerializeField] private int port = 5168;
        [SerializeField] private int maxRequestsPerFrame = 256;

        private readonly ConcurrentQueue<PendingRequest> pending = new ConcurrentQueue<PendingRequest>();
        private HttpListener listener;
        private CancellationTokenSource cancellation;
        private NativeServerApi api;
        private bool ready;
        private const int MaximumBodyBytes = 8 * 1024 * 1024;
        private const int MaximumQueuedRequests = 8192;

        private sealed class PendingRequest
        {
            public string method, path, body, token, origin;
            public TaskCompletionSource<NativeReply> completed =
                new TaskCompletionSource<NativeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            var arguments = Environment.GetCommandLineArgs();
            var portArg = arguments.FirstOrDefault(a => a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase));
            if (portArg != null && int.TryParse(portArg.Substring(7), out var chosen) &&
                chosen > 0 && chosen <= 65535) port = chosen;
            var bind = arguments.FirstOrDefault(a => a.StartsWith("--bind=", StringComparison.OrdinalIgnoreCase));
            var address = bind == null ? "127.0.0.1" : bind.Substring(7);
            if (address != "localhost" && address != "127.0.0.1" && address != "+" && address != "*")
            {
                Debug.LogError("Unsupported listen address. Use localhost or + with a reverse proxy.");
                Application.Quit(2);
                return;
            }

            try
            {
                var demo = arguments.Any(arg => arg == "--demo-mode") ||
                    Environment.GetEnvironmentVariable("CARDFORGE_DEMO_MODE") == "1";
                if (demo && address != "localhost" && address != "127.0.0.1")
                    throw new InvalidOperationException(
                        "Demo accounts are only permitted on a loopback-only native server.");
                api = new NativeServerApi(Application.streamingAssetsPath,
                    Application.persistentDataPath, demo);
                listener = new HttpListener();
                listener.Prefixes.Add("http://" + address + ":" + port + "/");
                listener.Start();
                cancellation = new CancellationTokenSource();
                ready = true;
                _ = AcceptLoop(cancellation.Token);
                Debug.Log("CARDFORGE_NATIVE_SERVER_READY port=" + port +
                    " mode=1v1-1v4; all services running in Unity.");
            }
            catch (Exception ex)
            {
                Debug.LogError("Native CardForge server startup failed: " + ex);
                Application.Quit(3);
            }
        }

        private async Task AcceptLoop(CancellationToken stop)
        {
            while (!stop.IsCancellationRequested && listener != null && listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (Exception) { break; }
                _ = Receive(context);
            }
        }

        private async Task Receive(HttpListenerContext context)
        {
            var request = context.Request;
            var authorization = request.Headers["Authorization"] ?? "";
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                authorization = authorization.Substring(7).Trim();
            string body = null;
            if (request.HasEntityBody)
            {
                if (request.ContentLength64 > MaximumBodyBytes)
                {
                    await Respond(context, NativeReply.Error("Request too large.", 413));
                    return;
                }
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                    if (Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes)
                    {
                        await Respond(context, NativeReply.Error("Request too large.", 413));
                        return;
                    }
                }
            }
            if (pending.Count >= MaximumQueuedRequests)
            {
                await Respond(context, NativeReply.Error("Server overloaded; retry.", 503));
                return;
            }
            var origin = request.Url.GetLeftPart(UriPartial.Authority);
            var job = new PendingRequest
            {
                method = request.HttpMethod, path = request.Url.AbsolutePath,
                body = body, token = authorization, origin = origin
            };
            pending.Enqueue(job);
            var result = await Task.WhenAny(job.completed.Task, Task.Delay(20000));
            await Respond(context, result == job.completed.Task ?
                await job.completed.Task : NativeReply.Error("Request timed out.", 504));
        }

        private static async Task Respond(HttpListenerContext context, NativeReply reply)
        {
            try
            {
                context.Response.StatusCode = reply.status;
                context.Response.ContentType = reply.contentType;
                context.Response.Headers["Cache-Control"] =
                    reply.contentType == "application/json" ? "no-store" : "public, max-age=3600";
                context.Response.ContentLength64 = reply.bytes.Length;
                await context.Response.OutputStream.WriteAsync(reply.bytes, 0, reply.bytes.Length);
                context.Response.Close();
            }
            catch (Exception) { try { context.Response.Abort(); } catch (Exception) { } }
        }

        private void Update()
        {
            if (!ready) return;
            for (var i = 0; i < maxRequestsPerFrame && pending.TryDequeue(out var job); i++)
            {
                try { if (job.completed.Task.IsCompleted) continue;
                    job.completed.TrySetResult(api.Handle(job.method, job.path, job.body,
                    job.token, job.origin)); }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    job.completed.TrySetResult(NativeReply.Error("Internal server error.", 500));
                }
            }
            try { api.Tick(); }
            catch (Exception ex) { Debug.LogError("Matchmaking tick: " + ex); }
        }

        private void OnApplicationQuit()
        {
            ready = false;
            cancellation?.Cancel();
            if (listener != null)
            {
                try { listener.Stop(); listener.Close(); } catch (Exception) { }
                listener = null;
            }
            while (pending.TryDequeue(out var job))
                job.completed.TrySetResult(NativeReply.Error("Server shutting down.", 503));
            cancellation?.Dispose();
        }
    }
}

