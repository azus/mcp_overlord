using mcp_overlord.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace mcp_overlord.Hosting
{
    /// <summary>
    /// Records the request and response bytes on the HTTP context and on <see cref="HttpExchangeAmbient"/>.
    /// A GET is not buffered, because that request is the long-lived event stream.
    /// </summary>
    public sealed class TrafficCaptureMiddleware
    {
        private readonly RequestDelegate _next;

        /// <summary>
        /// Creates the middleware.
        /// </summary>
        /// <param name="next">The rest of the pipeline.</param>
        public TrafficCaptureMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>
        /// Captures the current request.
        /// </summary>
        /// <param name="context">The request.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            HttpExchange exchange = Begin(context);
            context.Items[HttpExchange.ItemKey] = exchange;
            HttpExchangeAmbient.Exchange = exchange;
            try
            {
                await Capture(context, exchange);
            }
            finally
            {
                exchange.ResponseStatus = context.Response.StatusCode;
                exchange.ResponseHeaders = CopyHeaders(context.Response.Headers);
                exchange.Duration = DateTimeOffset.Now - exchange.Started;
                if (HttpExchangeAmbient.Exchange == exchange)
                {
                    HttpExchangeAmbient.Exchange = null;
                }
            }
        }

        private static HttpExchange Begin(HttpContext context)
        {
            ConnectionInfo connection = context.Connection;
            return new HttpExchange
            {
                ConnectionId = connection.Id ?? "",
                RemoteAddress = connection.RemoteIpAddress?.ToString() ?? "",
                RemotePort = connection.RemotePort,
                LocalAddress = connection.LocalIpAddress?.ToString() ?? "",
                LocalPort = connection.LocalPort,
                Scheme = context.Request.Scheme ?? "",
                Method = context.Request.Method ?? "",
                Path = context.Request.Path.ToString() + context.Request.QueryString.ToString(),
                RequestHeaders = CopyHeaders(context.Request.Headers),
                Started = DateTimeOffset.Now
            };
        }

        private async Task Capture(HttpContext context, HttpExchange exchange)
        {
            exchange.RequestBody = await ReadRequest(context);
            if (HttpMethods.IsGet(context.Request.Method))
            {
                exchange.ResponseBodyOmitted = true;
                await _next(context);
                return;
            }

            await CaptureResponse(context, exchange);
        }

        private async Task CaptureResponse(HttpContext context, HttpExchange exchange)
        {
            Stream original = context.Response.Body;
            IHttpResponseBodyFeature? feature = context.Features.Get<IHttpResponseBodyFeature>();
            var copy = new MemoryStream();
            var tee = new CapturingStream(original, copy);
            context.Response.Body = tee;
            context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(tee));
            try
            {
                await _next(context);
            }
            finally
            {
                context.Response.Body = original;
                context.Features.Set(feature);
                exchange.ResponseBody = copy.ToArray();
            }
        }

        private static async Task<byte[]> ReadRequest(HttpContext context)
        {
            context.Request.EnableBuffering();
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            using var copy = new MemoryStream();
            await context.Request.Body.CopyToAsync(copy, context.RequestAborted);
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }

            return copy.ToArray();
        }

        private static IReadOnlyList<HttpHeaderValue> CopyHeaders(IHeaderDictionary headers)
        {
            var list = new List<HttpHeaderValue>();
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in headers)
            {
                list.Add(new HttpHeaderValue
                {
                    Name = header.Key,
                    Value = Scrub(header.Key, header.Value.ToString())
                });
            }

            return list;
        }

        private static string Scrub(string name, string value)
        {
            if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("X-API-Key", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase))
            {
                return "***";
            }

            return value;
        }

        /// <summary>Copies every write to a second stream and to the original response stream.</summary>
        private sealed class CapturingStream : Stream
        {
            private readonly Stream _inner;
            private readonly MemoryStream _copy;

            public CapturingStream(Stream inner, MemoryStream copy)
            {
                _inner = inner;
                _copy = copy;
            }

            public override bool CanRead
            {
                get { return false; }
            }

            public override bool CanSeek
            {
                get { return false; }
            }

            public override bool CanWrite
            {
                get { return true; }
            }

            public override long Length
            {
                get { throw new NotSupportedException(); }
            }

            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }

            public override void Flush()
            {
                _copy.Flush();
                _inner.Flush();
            }

            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                await _copy.FlushAsync(cancellationToken).ConfigureAwait(false);
                await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _copy.Write(buffer, offset, count);
                _inner.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                _copy.Write(buffer);
                _inner.Write(buffer);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                _copy.Write(buffer.Span);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            protected override void Dispose(bool disposing)
            {
                // The server owns _inner. The middleware reads _copy before this stream is dropped.
            }
        }
    }
}
