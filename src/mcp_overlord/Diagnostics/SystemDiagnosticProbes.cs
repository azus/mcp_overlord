using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using mcp_overlord.Hosting;

namespace mcp_overlord.Diagnostics
{
    /// <summary>Probes that use the operating system and the network.</summary>
    public sealed class SystemDiagnosticProbes : IDiagnosticProbes
    {
        /// <inheritdoc />
        public bool IsPortListening(int port)
        {
            try
            {
                foreach (IPEndPoint endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                {
                    if (endpoint.Port == port)
                    {
                        return true;
                    }
                }
            }
            catch (NetworkInformationException)
            {
                return false;
            }

            return false;
        }

        /// <inheritdoc />
        public IReadOnlyList<IPAddress> LocalAddresses()
        {
            var addresses = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    AddAddresses(adapter, addresses);
                }
            }
            catch (NetworkInformationException)
            {
                return addresses;
            }

            return addresses;
        }

        /// <inheritdoc />
        public Task<DnsProbeResult> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out IPAddress? parsed))
            {
                return Task.FromResult(new DnsProbeResult
                {
                    Succeeded = true,
                    Detail = parsed.ToString(),
                    Addresses = new[] { parsed }
                });
            }

            return ResolveNameAsync(host, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<ProbeOutcome> ConnectTcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            using var client = new TcpClient();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await client.ConnectAsync(address, port, timeoutSource.Token).ConfigureAwait(false);
                return Ok(watch, "Connected in " + watch.ElapsedMilliseconds + " ms.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail(ProbeKinds.Timeout, "The connection timed out after " + timeout.TotalSeconds.ToString("0") + " seconds.", watch);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return Fail(ProbeKinds.Refused, "The connection was refused.", watch);
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
            {
                return Fail(ProbeKinds.Timeout, "The connection timed out.", watch);
            }
            catch (SocketException exception)
            {
                return Fail(ProbeKinds.Error, exception.Message, watch);
            }
        }

        /// <inheritdoc />
        public async Task<TlsProbeResult> HandshakeTlsAsync(
            string host,
            IPAddress address,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var captured = new CertificateCapture();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(address, port, timeoutSource.Token).ConfigureAwait(false);
                using var ssl = new SslStream(client.GetStream(), false, captured.Validate);
                var options = new SslClientAuthenticationOptions
                {
                    TargetHost = string.IsNullOrEmpty(host) ? address.ToString() : host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    RemoteCertificateValidationCallback = captured.Validate
                };
                await ssl.AuthenticateAsClientAsync(options, timeoutSource.Token).ConfigureAwait(false);
                return captured.Result(true, ProbeKinds.Ok, "The handshake completed.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return captured.Result(false, ProbeKinds.Timeout, "The TLS handshake timed out.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or AuthenticationException or SocketException)
            {
                return captured.Result(false, ProbeKinds.Tls, exception.Message);
            }
        }

        /// <inheritdoc />
        public string? GeneratedCertificateThumbprint()
        {
            var store = new SelfSignedCertificateStore(SelfSignedCertificateStore.DefaultDirectory);
            return store.TryReadPublicThumbprint();
        }

        /// <inheritdoc />
        public async Task<HttpProbeResult> PostJsonAsync(
            Uri url,
            string? apiKey,
            string json,
            string? sessionId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var http = new HttpClient(handler)
            {
                Timeout = timeout
            };
            using var request = BuildRequest(url, apiKey, json, sessionId);
            var watch = Stopwatch.StartNew();
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                string? session = null;
                if (response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
                {
                    session = values.FirstOrDefault();
                }

                return new HttpProbeResult
                {
                    Succeeded = true,
                    Kind = ProbeKinds.Http,
                    Detail = "HTTP " + (int)response.StatusCode + " in " + watch.ElapsedMilliseconds + " ms.",
                    StatusCode = (int)response.StatusCode,
                    Body = body,
                    SessionId = session,
                    ElapsedMilliseconds = watch.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HttpFailure(ProbeKinds.Timeout, "The HTTP call timed out.", watch);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                return HttpFailure(Classify(exception), exception.Message, watch);
            }
        }

        /// <inheritdoc />
        public async Task<PingProbeResult> PingAsync(IPAddress address, int ttl, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var ping = new Ping();
            var options = new PingOptions(Math.Clamp(ttl, 1, 255), true);
            try
            {
                PingReply reply = await ping.SendPingAsync(address, timeout, new byte[32], options, cancellationToken).ConfigureAwait(false);
                return Describe(reply);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PingException exception)
            {
                return new PingProbeResult
                {
                    Kind = ProbeKinds.Error,
                    Detail = exception.InnerException?.Message ?? exception.Message
                };
            }
        }

        private static async Task<DnsProbeResult> ResolveNameAsync(string host, CancellationToken cancellationToken)
        {
            try
            {
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
                if (addresses.Length == 0)
                {
                    return new DnsProbeResult
                    {
                        Succeeded = false,
                        Detail = "The name did not resolve."
                    };
                }

                return new DnsProbeResult
                {
                    Succeeded = true,
                    Detail = string.Join(", ", addresses.Select(address => address.ToString())),
                    Addresses = addresses
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                return new DnsProbeResult
                {
                    Succeeded = false,
                    Detail = exception.Message
                };
            }
        }

        private static void AddAddresses(NetworkInterface adapter, List<IPAddress> addresses)
        {
            foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
            {
                if (!addresses.Any(existing => existing.Equals(address.Address)))
                {
                    addresses.Add(address.Address);
                }
            }
        }

        private static HttpRequestMessage BuildRequest(Uri url, string? apiKey, string json, string? sessionId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json ?? "", System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ConnectionDiagnostics.ProtocolVersion);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            }

            return request;
        }

        private static string Classify(HttpRequestException exception)
        {
            if (exception.InnerException is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return ProbeKinds.Refused;
            }

            return ProbeKinds.Error;
        }

        private static ProbeOutcome Ok(Stopwatch watch, string detail)
        {
            return new ProbeOutcome
            {
                Succeeded = true,
                Kind = ProbeKinds.Ok,
                Detail = detail,
                ElapsedMilliseconds = watch.ElapsedMilliseconds
            };
        }

        private static ProbeOutcome Fail(string kind, string detail, Stopwatch watch)
        {
            return new ProbeOutcome
            {
                Succeeded = false,
                Kind = kind,
                Detail = detail,
                ElapsedMilliseconds = watch.ElapsedMilliseconds
            };
        }

        private static HttpProbeResult HttpFailure(string kind, string detail, Stopwatch watch)
        {
            return new HttpProbeResult
            {
                Succeeded = false,
                Kind = kind,
                Detail = detail,
                ElapsedMilliseconds = watch.ElapsedMilliseconds
            };
        }

        private static PingProbeResult Describe(PingReply reply)
        {
            string address = reply.Address?.ToString() ?? "";
            if (reply.Status == IPStatus.Success)
            {
                return new PingProbeResult
                {
                    Kind = ProbeKinds.Ok,
                    ReplyAddress = address,
                    ElapsedMilliseconds = reply.RoundtripTime,
                    Detail = address + " " + reply.RoundtripTime + " ms"
                };
            }

            if (reply.Status == IPStatus.TtlExpired)
            {
                return new PingProbeResult
                {
                    Kind = ProbeKinds.TtlExpired,
                    ReplyAddress = address,
                    ElapsedMilliseconds = reply.RoundtripTime,
                    Detail = string.IsNullOrEmpty(address) ? "ttl expired" : address
                };
            }

            if (reply.Status == IPStatus.TimedOut)
            {
                return new PingProbeResult
                {
                    Kind = ProbeKinds.Timeout,
                    Detail = "timed out"
                };
            }

            return new PingProbeResult
            {
                Kind = ProbeKinds.Unreachable,
                ReplyAddress = address,
                Detail = reply.Status.ToString()
            };
        }

        private sealed class CertificateCapture
        {
            private X509Certificate2? _certificate;
            private string _alternativeNames = "";
            private string _chain = "";
            private string _thumbprint = "";
            private bool _mismatch;
            private bool _untrusted;
            private bool _selfSigned;

            public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
            {
                Remember(certificate, chain, errors);
                return true;
            }

            public TlsProbeResult Result(bool succeeded, string kind, string detail)
            {
                return new TlsProbeResult
                {
                    Succeeded = succeeded,
                    Kind = kind,
                    Detail = detail,
                    Subject = _certificate?.Subject ?? "",
                    AlternativeNames = _alternativeNames,
                    Expires = _certificate == null ? null : _certificate.NotAfter,
                    ChainStatus = _chain,
                    HostNameMismatch = _mismatch,
                    Untrusted = _untrusted,
                    SelfSigned = _selfSigned,
                    Thumbprint = _thumbprint
                };
            }

            private void Remember(X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
            {
                if (certificate != null)
                {
                    _certificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                    _alternativeNames = AlternativeNames(_certificate);
                    _thumbprint = _certificate.Thumbprint;
                    _selfSigned = _certificate.SubjectName.RawData.SequenceEqual(_certificate.IssuerName.RawData);
                }

                _mismatch = (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0;
                _untrusted = (errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0;
                _chain = ChainText(chain);
            }

            private static string AlternativeNames(X509Certificate2 certificate)
            {
                foreach (X509Extension extension in certificate.Extensions)
                {
                    if (extension is X509SubjectAlternativeNameExtension names)
                    {
                        return string.Join(", ", names.EnumerateDnsNames().Concat(names.EnumerateIPAddresses().Select(address => address.ToString())));
                    }
                }

                return "";
            }

            private static string ChainText(X509Chain? chain)
            {
                if (chain == null || chain.ChainStatus.Length == 0)
                {
                    return "";
                }

                var parts = new List<string>();
                foreach (X509ChainStatus status in chain.ChainStatus)
                {
                    if (status.Status == X509ChainStatusFlags.NoError)
                    {
                        continue;
                    }

                    string information = status.StatusInformation.Trim();
                    parts.Add(string.IsNullOrEmpty(information) ? status.Status.ToString() : information);
                }

                return string.Join("; ", parts);
            }
        }
    }
}
