using System.Net;
using System.Security.Cryptography.X509Certificates;
using mcp_overlord.Configuration;
using mcp_overlord.Hosting;

namespace mcp_overlord.tests
{
    public class CertificateStoreTests
    {
        [Fact]
        public void EnsureCreatesReusesAndReplacesTheCertificate()
        {
            string directory = NewDirectory();
            var store = new SelfSignedCertificateStore(directory);
            ServerSettings settings = Settings(null);

            StoredCertificate created = store.Ensure(settings);
            using (X509Certificate2 certificate = Load(created.PfxPath))
            {
                Assert.True(created.Created);
                Assert.True(certificate.HasPrivateKey);
                Assert.Equal(created.Thumbprint, certificate.Thumbprint);
                Assert.Contains(ReadDnsNames(certificate), name => string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(ReadDnsNames(certificate), name => string.Equals(name, Environment.MachineName, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(ReadAddresses(certificate), address => address.Equals(IPAddress.Parse("10.0.0.8")));
            }

            using (X509Certificate2 publicCertificate = X509CertificateLoader.LoadCertificateFromFile(created.CerPath))
            {
                Assert.False(publicCertificate.HasPrivateKey);
            }

            StoredCertificate reused = store.Ensure(settings);
            Assert.False(reused.Created);
            Assert.Equal(created.Thumbprint, reused.Thumbprint);

            StoredCertificate replaced = store.Ensure(Settings(new[] { "play.example.com" }));
            Assert.True(replaced.Created);
            Assert.NotEqual(created.Thumbprint, replaced.Thumbprint);
            using (X509Certificate2 certificate = Load(replaced.PfxPath))
            {
                Assert.Contains(ReadDnsNames(certificate), name => string.Equals(name, "play.example.com", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void PublicThumbprintReadsTheWrittenCertificate()
        {
            string directory = NewDirectory();
            var store = new SelfSignedCertificateStore(directory);
            Assert.Null(store.TryReadPublicThumbprint());

            StoredCertificate created = store.Ensure(Settings(null));

            Assert.Equal(created.Thumbprint, store.TryReadPublicThumbprint());
        }

        [Fact]
        public void AnUnreadablePublicCertificateHasNoThumbprint()
        {
            string directory = NewDirectory();
            var store = new SelfSignedCertificateStore(directory);
            File.WriteAllBytes(Path.Combine(directory, SelfSignedCertificateStore.PublicCertificateFileName), "not a certificate"u8.ToArray());

            Assert.Null(store.TryReadPublicThumbprint());
        }

        [Fact]
        public void ACertificateThatExpiresSoonIsReplaced()
        {
            string directory = NewDirectory();
            var store = new SelfSignedCertificateStore(directory);
            ServerSettings settings = Settings(null);
            byte[] shortLived = SelfSignedCertificateStore.ExportSelfSigned(
                settings,
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(10));
            File.WriteAllBytes(Path.Combine(directory, SelfSignedCertificateStore.CertificateFileName), shortLived);

            StoredCertificate replaced = store.Ensure(settings);

            Assert.True(replaced.Created);
            using X509Certificate2 certificate = Load(replaced.PfxPath);
            Assert.True(certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(14));
        }

        [Fact]
        public void AnUnreadableCertificateIsReplaced()
        {
            string directory = NewDirectory();
            var store = new SelfSignedCertificateStore(directory);
            File.WriteAllBytes(Path.Combine(directory, SelfSignedCertificateStore.CertificateFileName), "not a certificate"u8.ToArray());

            StoredCertificate replaced = store.Ensure(Settings(null));

            Assert.True(replaced.Created);
            using X509Certificate2 certificate = Load(replaced.PfxPath);
            Assert.True(certificate.HasPrivateKey);
        }

        private static X509Certificate2 Load(string path)
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, "", X509KeyStorageFlags.EphemeralKeySet);
        }

        private static IReadOnlyList<string> ReadDnsNames(X509Certificate2 certificate)
        {
            var names = new List<string>();
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17")
                {
                    continue;
                }

                var alternative = new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
                names.AddRange(alternative.EnumerateDnsNames());
            }

            return names;
        }

        private static IReadOnlyList<IPAddress> ReadAddresses(X509Certificate2 certificate)
        {
            var addresses = new List<IPAddress>();
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != "2.5.29.17")
                {
                    continue;
                }

                var alternative = new X509SubjectAlternativeNameExtension(extension.RawData, extension.Critical);
                addresses.AddRange(alternative.EnumerateIPAddresses());
            }

            return addresses;
        }

        private static ServerSettings Settings(IReadOnlyList<string>? hostNames)
        {
            CertificateSettings? certificate = hostNames == null
                ? null
                : new CertificateSettings(null, "", hostNames);
            return new ServerSettings(5000, 5001, true, certificate, "10.0.0.8", "0123456789abcdef");
        }

        private static string NewDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "mcp-cert-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
