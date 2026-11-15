using System.Net;

namespace mcp_overlord.Diagnostics
{
    /// <summary>
    /// Network calls used by the connection check.
    /// Tests supply a fake so the report can be checked without a socket.
    /// </summary>
    public interface IDiagnosticProbes
    {
        /// <summary>True when any listener owns <paramref name="port"/>.</summary>
        /// <param name="port">TCP port.</param>
        /// <returns>True when the port is in the active listener table.</returns>
        bool IsPortListening(int port);

        /// <summary>Unicast addresses assigned to this machine.</summary>
        /// <returns>The addresses. Loopback may be included.</returns>
        IReadOnlyList<IPAddress> LocalAddresses();

        /// <summary>Resolves <paramref name="host"/>. An IP address is returned as itself.</summary>
        /// <param name="host">Host name or address.</param>
        /// <param name="cancellationToken">Cancels the lookup.</param>
        /// <returns>The lookup.</returns>
        Task<DnsProbeResult> ResolveAsync(string host, CancellationToken cancellationToken);

        /// <summary>Opens a TCP connection.</summary>
        /// <param name="address">Target address.</param>
        /// <param name="port">Target port.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">Cancels the connect.</param>
        /// <returns>The connect result.</returns>
        Task<ProbeOutcome> ConnectTcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>Handshakes TLS and records the certificate even when it is not trusted.</summary>
        /// <param name="host">Name sent in the handshake.</param>
        /// <param name="address">Address to connect to.</param>
        /// <param name="port">Target port.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">Cancels the handshake.</param>
        /// <returns>The handshake.</returns>
        Task<TlsProbeResult> HandshakeTlsAsync(string host, IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>Thumbprint of the self-signed certificate this server created, when that file exists.</summary>
        /// <returns>The thumbprint, or null.</returns>
        string? GeneratedCertificateThumbprint();

        /// <summary>Posts a JSON body and reads the response. Certificate errors are ignored so the status can be read.</summary>
        /// <param name="url">Target URL.</param>
        /// <param name="apiKey">API key, or null.</param>
        /// <param name="json">Request body.</param>
        /// <param name="sessionId">MCP session id, or null.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="cancellationToken">Cancels the call.</param>
        /// <returns>The response.</returns>
        Task<HttpProbeResult> PostJsonAsync(Uri url, string? apiKey, string json, string? sessionId, TimeSpan timeout, CancellationToken cancellationToken);

        /// <summary>Pings <paramref name="address"/> with the given time to live.</summary>
        /// <param name="address">Target address.</param>
        /// <param name="ttl">Time to live. A traceroute uses 1, then 2, and so on.</param>
        /// <param name="timeout">How long to wait for a reply.</param>
        /// <param name="cancellationToken">Cancels the ping.</param>
        /// <returns>The reply.</returns>
        Task<PingProbeResult> PingAsync(IPAddress address, int ttl, TimeSpan timeout, CancellationToken cancellationToken);
    }
}
