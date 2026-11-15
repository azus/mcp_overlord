using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using mcp_overlord.Configuration;

namespace mcp_overlord.Hosting
{
    /// <summary>
    /// A PKCS#12 file the listener can load, and the public certificate a remote client can trust.
    /// </summary>
    /// <param name="PfxPath">Absolute path of the PKCS#12 file.</param>
    /// <param name="CerPath">Absolute path of the public certificate.</param>
    /// <param name="Password">Password for <paramref name="PfxPath"/>. Empty for a generated file.</param>
    /// <param name="Thumbprint">Certificate thumbprint.</param>
    /// <param name="Created">True when this call wrote a new file.</param>
    public sealed record StoredCertificate(string PfxPath, string CerPath, string Password, string Thumbprint, bool Created);

    /// <summary>
    /// Writes a self-signed PKCS#12 file and reuses it on the next start.
    /// The certificate is not installed into a trust store.
    /// </summary>
    public sealed class SelfSignedCertificateStore
    {
        /// <summary>File name of the PKCS#12 certificate inside <see cref="DefaultDirectory"/>.</summary>
        public const string CertificateFileName = "server.pfx";

        /// <summary>File name of the public certificate beside <see cref="CertificateFileName"/>.</summary>
        public const string PublicCertificateFileName = "server.cer";

        private const string Password = "";

        private static readonly TimeSpan MinimumRemainingLifetime = TimeSpan.FromDays(14);

        private static readonly TimeSpan CertificateLifetime = TimeSpan.FromDays(730);

        private readonly string _directory;

        /// <summary>
        /// Creates a store that reads and writes <paramref name="directory"/>.
        /// </summary>
        /// <param name="directory">Directory that holds the certificate files.</param>
        public SelfSignedCertificateStore(string directory)
        {
            _directory = directory;
        }

        /// <summary>
        /// Reads the thumbprint of <see cref="PublicCertificateFileName"/> in this store.
        /// </summary>
        /// <returns>The thumbprint, or null when the file is missing or unreadable.</returns>
        public string? TryReadPublicThumbprint()
        {
            string path = Path.Combine(_directory, PublicCertificateFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(path);
                return string.IsNullOrEmpty(certificate.Thumbprint) ? null : certificate.Thumbprint;
            }
            catch (Exception exception) when (exception is CryptographicException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Default directory: <c>%LOCALAPPDATA%\mcp_overlord</c>.
        /// </summary>
        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "mcp_overlord");
            }
        }

        /// <summary>
        /// Returns the stored certificate when it is still usable, otherwise writes a new pair.
        /// </summary>
        /// <param name="settings">Listener settings. Host names and the bind address are copied into a new certificate.</param>
        /// <returns>The certificate to load.</returns>
        public StoredCertificate Ensure(ServerSettings settings)
        {
            Directory.CreateDirectory(_directory);
            string pfxPath = Path.Combine(_directory, CertificateFileName);
            string cerPath = Path.Combine(_directory, PublicCertificateFileName);
            StoredCertificate? existing = TryReuse(pfxPath, cerPath, settings);
            if (existing != null)
            {
                return existing;
            }

            return Create(pfxPath, cerPath, settings);
        }

        /// <summary>
        /// Builds a PKCS#12 certificate for <paramref name="settings"/>. Tests use this to plant a short-lived file.
        /// </summary>
        /// <param name="settings">Names the certificate must contain.</param>
        /// <param name="notBefore">Start of the validity period.</param>
        /// <param name="notAfter">End of the validity period.</param>
        /// <returns>PKCS#12 bytes with an empty password.</returns>
        internal static byte[] ExportSelfSigned(ServerSettings settings, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
            var usages = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, false));
            request.CertificateExtensions.Add(BuildAlternativeNames(RequiredNames(settings)));
            using X509Certificate2 certificate = request.CreateSelfSigned(notBefore, notAfter);
            return certificate.Export(X509ContentType.Pfx, Password);
        }

        private StoredCertificate? TryReuse(string pfxPath, string cerPath, ServerSettings settings)
        {
            if (!File.Exists(pfxPath))
            {
                return null;
            }

            try
            {
                using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                    pfxPath,
                    Password,
                    X509KeyStorageFlags.EphemeralKeySet);
                if (!CanReuse(certificate, settings, DateTimeOffset.UtcNow))
                {
                    return null;
                }

                File.WriteAllBytes(cerPath, certificate.Export(X509ContentType.Cert));
                return new StoredCertificate(pfxPath, cerPath, Password, certificate.Thumbprint ?? "", false);
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        private StoredCertificate Create(string pfxPath, string cerPath, ServerSettings settings)
        {
            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.Add(CertificateLifetime);
            byte[] pfx = ExportSelfSigned(settings, notBefore, notAfter);
            File.WriteAllBytes(pfxPath, pfx);
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                Password,
                X509KeyStorageFlags.EphemeralKeySet);
            File.WriteAllBytes(cerPath, certificate.Export(X509ContentType.Cert));
            return new StoredCertificate(pfxPath, cerPath, Password, certificate.Thumbprint ?? "", true);
        }

        private static bool CanReuse(X509Certificate2 certificate, ServerSettings settings, DateTimeOffset now)
        {
            if (!certificate.HasPrivateKey)
            {
                return false;
            }

            if (certificate.NotAfter.ToUniversalTime() <= now.UtcDateTime.Add(MinimumRemainingLifetime))
            {
                return false;
            }

            if (certificate.NotBefore.ToUniversalTime() > now.UtcDateTime.AddMinutes(5))
            {
                return false;
            }

            return Covers(certificate, RequiredNames(settings));
        }

        private static bool Covers(X509Certificate2 certificate, IReadOnlyList<CertificateName> required)
        {
            var dnsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addresses = new HashSet<IPAddress>();
            ReadAlternativeNames(certificate, dnsNames, addresses);
            foreach (CertificateName name in required)
            {
                if (name.Address != null && !addresses.Contains(name.Address))
                {
                    return false;
                }

                if (name.DnsName != null && !dnsNames.Contains(name.DnsName))
                {
                    return false;
                }
            }

            return true;
        }

        private static void ReadAlternativeNames(X509Certificate2 certificate, HashSet<string> dnsNames, HashSet<IPAddress> addresses)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17")
                {
                    continue;
                }

                X509SubjectAlternativeNameExtension alternative = extension as X509SubjectAlternativeNameExtension
                    ?? new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
                foreach (string dnsName in alternative.EnumerateDnsNames())
                {
                    dnsNames.Add(dnsName);
                }

                foreach (IPAddress address in alternative.EnumerateIPAddresses())
                {
                    addresses.Add(address);
                }
            }
        }

        private static X509Extension BuildAlternativeNames(IReadOnlyList<CertificateName> required)
        {
            var alternative = new SubjectAlternativeNameBuilder();
            foreach (CertificateName name in required)
            {
                if (name.Address != null)
                {
                    alternative.AddIpAddress(name.Address);
                }
                else if (name.DnsName != null)
                {
                    alternative.AddDnsName(name.DnsName);
                }
            }

            return alternative.Build();
        }

        private static IReadOnlyList<CertificateName> RequiredNames(ServerSettings settings)
        {
            var names = new List<CertificateName>();
            AddDns(names, "localhost");
            AddAddress(names, IPAddress.Loopback);
            AddAddress(names, IPAddress.IPv6Loopback);
            AddDns(names, Environment.MachineName);
            if (IPAddress.TryParse(settings.BindAddress, out IPAddress? bindAddress)
                && !bindAddress.Equals(IPAddress.Any)
                && !bindAddress.Equals(IPAddress.IPv6Any))
            {
                AddAddress(names, bindAddress);
            }

            if (settings.Certificate != null)
            {
                foreach (string hostName in settings.Certificate.HostNames)
                {
                    AddConfigured(names, hostName);
                }
            }

            return names;
        }

        private static void AddConfigured(List<CertificateName> names, string hostName)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                return;
            }

            string trimmed = hostName.Trim();
            if (IPAddress.TryParse(trimmed, out IPAddress? address))
            {
                AddAddress(names, address);
                return;
            }

            AddDns(names, trimmed);
        }

        private static void AddDns(List<CertificateName> names, string? dnsName)
        {
            if (string.IsNullOrWhiteSpace(dnsName))
            {
                return;
            }

            string trimmed = dnsName.Trim();
            foreach (CertificateName existing in names)
            {
                if (string.Equals(existing.DnsName, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            names.Add(new CertificateName(trimmed, null));
        }

        private static void AddAddress(List<CertificateName> names, IPAddress address)
        {
            foreach (CertificateName existing in names)
            {
                if (existing.Address != null && existing.Address.Equals(address))
                {
                    return;
                }
            }

            names.Add(new CertificateName(null, address));
        }

        private readonly struct CertificateName
        {
            public CertificateName(string? dnsName, IPAddress? address)
            {
                DnsName = dnsName;
                Address = address;
            }

            public string? DnsName { get; }

            public IPAddress? Address { get; }
        }
    }
}
