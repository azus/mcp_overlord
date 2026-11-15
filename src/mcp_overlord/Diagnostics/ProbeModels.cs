using System.Net;

namespace mcp_overlord.Diagnostics
{
    /// <summary>Stable names for a probe failure.</summary>
    public static class ProbeKinds
    {
        /// <summary>The probe reached the host.</summary>
        public const string Ok = "ok";

        /// <summary>The host refused the connection.</summary>
        public const string Refused = "refused";

        /// <summary>The probe timed out.</summary>
        public const string Timeout = "timeout";

        /// <summary>A ping came back because the time to live expired.</summary>
        public const string TtlExpired = "ttlExpired";

        /// <summary>The network reported the host as unreachable.</summary>
        public const string Unreachable = "unreachable";

        /// <summary>The name did not resolve.</summary>
        public const string Dns = "dns";

        /// <summary>The TLS handshake failed.</summary>
        public const string Tls = "tls";

        /// <summary>An HTTP response was received.</summary>
        public const string Http = "http";

        /// <summary>Any other failure.</summary>
        public const string Error = "error";
    }

    /// <summary>Result of a TCP connect.</summary>
    public sealed class ProbeOutcome
    {
        /// <summary>True when the connect succeeded.</summary>
        public bool Succeeded { get; init; }

        /// <summary>A <see cref="ProbeKinds"/> value.</summary>
        public string Kind { get; init; } = "";

        /// <summary>Text safe to show.</summary>
        public string Detail { get; init; } = "";

        /// <summary>How long the probe ran.</summary>
        public long ElapsedMilliseconds { get; init; }
    }

    /// <summary>Result of a name lookup.</summary>
    public sealed class DnsProbeResult
    {
        /// <summary>True when at least one address was found.</summary>
        public bool Succeeded { get; init; }

        /// <summary>Text safe to show.</summary>
        public string Detail { get; init; } = "";

        /// <summary>Resolved addresses. Empty on failure.</summary>
        public IReadOnlyList<IPAddress> Addresses { get; init; } = Array.Empty<IPAddress>();
    }

    /// <summary>Result of a TLS handshake that always continues far enough to inspect the certificate.</summary>
    public sealed class TlsProbeResult
    {
        /// <summary>True when the handshake completed.</summary>
        public bool Succeeded { get; init; }

        /// <summary>A <see cref="ProbeKinds"/> value.</summary>
        public string Kind { get; init; } = "";

        /// <summary>Text safe to show.</summary>
        public string Detail { get; init; } = "";

        /// <summary>Certificate subject. Empty when no certificate was seen.</summary>
        public string Subject { get; init; } = "";

        /// <summary>Subject alternative names, comma separated.</summary>
        public string AlternativeNames { get; init; } = "";

        /// <summary>Certificate expiry, when a certificate was seen.</summary>
        public DateTimeOffset? Expires { get; init; }

        /// <summary>Chain status text. Empty when the chain was not built.</summary>
        public string ChainStatus { get; init; } = "";

        /// <summary>True when the certificate name does not match the host.</summary>
        public bool HostNameMismatch { get; init; }

        /// <summary>True when the chain is not trusted.</summary>
        public bool Untrusted { get; init; }

        /// <summary>True when the certificate subject and issuer are the same.</summary>
        public bool SelfSigned { get; init; }

        /// <summary>Certificate thumbprint. Empty when no certificate was seen.</summary>
        public string Thumbprint { get; init; } = "";
    }

    /// <summary>Result of one HTTP POST.</summary>
    public sealed class HttpProbeResult
    {
        /// <summary>True when a response was received.</summary>
        public bool Succeeded { get; init; }

        /// <summary>A <see cref="ProbeKinds"/> value.</summary>
        public string Kind { get; init; } = "";

        /// <summary>Text safe to show.</summary>
        public string Detail { get; init; } = "";

        /// <summary>HTTP status. Zero when there was no response.</summary>
        public int StatusCode { get; init; }

        /// <summary>Response body.</summary>
        public string Body { get; init; } = "";

        /// <summary>Session id returned by the server, when present.</summary>
        public string? SessionId { get; init; }

        /// <summary>How long the call ran.</summary>
        public long ElapsedMilliseconds { get; init; }
    }

    /// <summary>Result of one ping, including a traceroute hop.</summary>
    public sealed class PingProbeResult
    {
        /// <summary>A <see cref="ProbeKinds"/> value.</summary>
        public string Kind { get; init; } = "";

        /// <summary>Address that answered. Empty when nothing answered.</summary>
        public string ReplyAddress { get; init; } = "";

        /// <summary>Round trip time.</summary>
        public long ElapsedMilliseconds { get; init; }

        /// <summary>Text safe to show.</summary>
        public string Detail { get; init; } = "";
    }

    /// <summary>How a connection-check step is shown.</summary>
    public enum CheckStepStatus
    {
        /// <summary>The step succeeded.</summary>
        Pass,

        /// <summary>The step finished, and the detail needs attention.</summary>
        Warning,

        /// <summary>The step failed.</summary>
        Fail
    }

    /// <summary>One line in a connection report.</summary>
    public sealed class ConnectionCheckStep
    {
        /// <summary>Step name, including the endpoint.</summary>
        public string Name { get; init; } = "";

        /// <summary>How the step is shown.</summary>
        public CheckStepStatus Status { get; init; }

        /// <summary>True when the step did not fail. A warning still counts as passed.</summary>
        public bool Passed
        {
            get { return Status != CheckStepStatus.Fail; }
        }

        /// <summary>What the step found.</summary>
        public string Detail { get; init; } = "";
    }

    /// <summary>The full connection check.</summary>
    public sealed class ConnectionReport
    {
        /// <summary>Monospace report, including insights.</summary>
        public string Text { get; init; } = "";

        /// <summary>Pass and fail lines, in the order they ran.</summary>
        public IReadOnlyList<ConnectionCheckStep> Steps { get; init; } = Array.Empty<ConnectionCheckStep>();
    }
}
