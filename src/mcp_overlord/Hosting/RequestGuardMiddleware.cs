using System.Security.Cryptography;
using System.Text;
using mcp_overlord.Logging;
using Microsoft.AspNetCore.Http;

namespace mcp_overlord.Hosting
{
    /// <summary>Attaches the current HTTP exchange to a rejected request.</summary>
    internal static class RejectionLog
    {
        /// <summary>
        /// Records <paramref name="statusCode"/> on the exchange and returns a detail for the log.
        /// </summary>
        /// <param name="context">The rejected request.</param>
        /// <param name="statusCode">Status about to be returned.</param>
        /// <returns>The detail, or null when no exchange was captured.</returns>
        public static LogDetail? Detail(HttpContext context, int statusCode)
        {
            HttpExchange? exchange = HttpExchangeAmbient.Exchange;
            if (exchange == null
                && context.Items.TryGetValue(HttpExchange.ItemKey, out object? value)
                && value is HttpExchange stored)
            {
                exchange = stored;
            }

            if (exchange == null)
            {
                return null;
            }

            exchange.ResponseStatus = statusCode;
            return new LogDetail
            {
                Exchange = exchange
            };
        }
    }

    /// <summary>API key and allowed origins captured when a listener starts. A later config reload does not change them.</summary>
    public sealed class ListenerSecurity
    {
        /// <summary>
        /// Creates the snapshot.
        /// </summary>
        /// <param name="apiKey">Shared secret, or null when authentication is off.</param>
        /// <param name="allowedOrigins">Origins this listener accepts.</param>
        public ListenerSecurity(string? apiKey, IReadOnlySet<string> allowedOrigins)
        {
            ApiKey = apiKey;
            AllowedOrigins = allowedOrigins;
        }

        /// <summary>Shared secret, or null when requests are accepted without a key.</summary>
        public string? ApiKey { get; }

        /// <summary>Origins that may call this listener.</summary>
        public IReadOnlySet<string> AllowedOrigins { get; }
    }

    /// <summary>Counts MCP requests currently inside the pipeline.</summary>
    public sealed class InFlightCounter
    {
        private int _count;

        /// <summary>Requests currently inside the pipeline.</summary>
        public int Count
        {
            get { return Volatile.Read(ref _count); }
        }

        /// <summary>Increments the count.</summary>
        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }

        /// <summary>Decrements the count.</summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref _count);
        }
    }

    /// <summary>
    /// Rejects a missing or wrong API key with HTTP 401. Comparison is constant time and the secret is not logged.
    /// </summary>
    public sealed class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ListenerSecurity _security;
        private readonly LogBuffer _log;
        private readonly StatsCounter _stats;

        /// <summary>
        /// Creates the middleware.
        /// </summary>
        public ApiKeyMiddleware(RequestDelegate next, ListenerSecurity security, LogBuffer log, StatsCounter stats)
        {
            _next = next;
            _security = security;
            _log = log;
            _stats = stats;
        }

        /// <summary>Invokes the middleware.</summary>
        /// <param name="context">Current request.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            if (string.IsNullOrEmpty(_security.ApiKey))
            {
                await _next(context);
                return;
            }

            if (!KeysMatch(ReadProvided(context), _security.ApiKey))
            {
                _log.Append(LogLevel.Warning, "server", "Rejected a request with a missing or wrong API key.", RejectionLog.Detail(context, StatusCodes.Status401Unauthorized));
                _stats.RecordHttpFailure();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await _next(context);
        }

        private static string ReadProvided(HttpContext context)
        {
            string? apiKeyHeader = context.Request.Headers["X-API-Key"].FirstOrDefault();
            if (!string.IsNullOrEmpty(apiKeyHeader))
            {
                return apiKeyHeader;
            }

            string? authorization = context.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authorization))
            {
                return "";
            }

            const string BearerPrefix = "Bearer ";
            if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return authorization.Substring(BearerPrefix.Length).Trim();
            }

            return "";
        }

        private static bool KeysMatch(string provided, string expected)
        {
            byte[] providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
            byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
        }
    }

    /// <summary>
    /// Rejects a browser Origin that is not one of this server's own endpoints. A missing Origin is allowed.
    /// </summary>
    public sealed class OriginCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ListenerSecurity _security;
        private readonly LogBuffer _log;
        private readonly StatsCounter _stats;

        /// <summary>
        /// Creates the middleware.
        /// </summary>
        public OriginCheckMiddleware(RequestDelegate next, ListenerSecurity security, LogBuffer log, StatsCounter stats)
        {
            _next = next;
            _security = security;
            _log = log;
            _stats = stats;
        }

        /// <summary>Invokes the middleware.</summary>
        /// <param name="context">Current request.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            string? origin = context.Request.Headers.Origin.FirstOrDefault();
            if (string.IsNullOrEmpty(origin))
            {
                await _next(context);
                return;
            }

            if (_security.AllowedOrigins.Contains(origin))
            {
                await _next(context);
                return;
            }

            _log.Append(LogLevel.Warning, "server", "Rejected origin " + origin + ".", RejectionLog.Detail(context, StatusCodes.Status403Forbidden));
            _stats.RecordHttpFailure();
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }
    }

    /// <summary>Tracks how many requests are inside the MCP pipeline and publishes that as the session count.</summary>
    public sealed class InFlightMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly InFlightCounter _counter;
        private readonly StatsCounter _stats;

        /// <summary>
        /// Creates the middleware.
        /// </summary>
        public InFlightMiddleware(RequestDelegate next, InFlightCounter counter, StatsCounter stats)
        {
            _next = next;
            _counter = counter;
            _stats = stats;
        }

        /// <summary>Invokes the middleware.</summary>
        /// <param name="context">Current request.</param>
        public async Task InvokeAsync(HttpContext context)
        {
            _counter.Increment();
            _stats.SetActiveSessions(_counter.Count);
            try
            {
                await _next(context);
            }
            finally
            {
                _counter.Decrement();
                _stats.SetActiveSessions(_counter.Count);
            }
        }
    }
}
