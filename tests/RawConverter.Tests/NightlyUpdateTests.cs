using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RawConverter.Tests
{
    /// <summary>
    /// The nightly channel: nightly builds look for newer nightlies, and the Windows installer is downloaded and
    /// verified against SHA256SUMS.txt.
    /// </summary>
    public class NightlyUpdateTests : IDisposable
    {
        private const string Installer = "RawConverter-1.3.0-nightly.20261113.6-win-x64-setup.exe";
        private const string Base = "https://github.com/YatesLab-Software-Repo/RawConverter/releases/download/nightly/";

        private readonly string _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        [Theory]
        [InlineData("1.3.0-nightly.20261113.5", "1.3.0-nightly.20261112.9", true)]
        [InlineData("1.3.0-nightly.20261112.10", "1.3.0-nightly.20261112.9", true)]
        [InlineData("1.3.0", "1.3.0-nightly.20261112.9", true)]
        [InlineData("1.3.0-nightly.20261112.9", "1.2.1", true)]
        [InlineData("1.2.1", "1.3.0-nightly.20261112.9", false)]
        public void OrdersNightlies(string a, string b, bool newer)
        {
            Assert.Equal(newer, UpdateChecker.IsNewer(a, b));
        }

        [Fact]
        public void ReadsNightlyVersionFromTitleAndFindsInstaller()
        {
            ReleaseInfo release = UpdateChecker.ParseRelease(NightlyJson("1.3.0-nightly.20261113.6"));

            Assert.Equal("1.3.0-nightly.20261113.6", release.Version);
            Assert.True(release.IsNightly);
            Assert.Equal(Base + Installer, release.InstallerUrl);
            Assert.Equal(Base + "SHA256SUMS.txt", release.ChecksumsUrl);
        }

        [Fact]
        public async Task NightlyBuildGetsNewerNightly()
        {
            RoutingHandler handler = Routes(stable: "1.2.1", nightly: "1.3.0-nightly.20261113.6");

            ReleaseInfo update = await UpdateChecker.CheckForUpdateAsync("1.3.0-nightly.20261112.5", handler);

            Assert.Equal("1.3.0-nightly.20261113.6", update.Version);
        }

        [Fact]
        public async Task NightlyBuildGetsNewerRelease()
        {
            RoutingHandler handler = Routes(stable: "1.3.0", nightly: "1.3.0-nightly.20261113.6");

            ReleaseInfo update = await UpdateChecker.CheckForUpdateAsync("1.3.0-nightly.20261112.5", handler);

            Assert.Equal("1.3.0", update.Version);
        }

        [Fact]
        public async Task ReleaseBuildIgnoresNightlies()
        {
            RoutingHandler handler = Routes(stable: "1.2.1", nightly: "1.3.0-nightly.20261113.6");

            Assert.Null(await UpdateChecker.CheckForUpdateAsync("1.2.1", handler));
            Assert.DoesNotContain(handler.Requested, u => u.EndsWith("/tags/nightly"));
        }

        [Fact]
        public async Task NoNightlyReleaseIsNoUpdate()
        {
            RoutingHandler handler = Routes(stable: "1.2.1", nightly: null);

            Assert.Null(await UpdateChecker.CheckForUpdateAsync("1.3.0-nightly.20261112.5", handler));
        }

        [Fact]
        public void FindsChecksumInListing()
        {
            string hash = new string('a', 64);
            string listing = new string('b', 64) + "  other.zip\n" + hash + " *" + Installer + "\n";

            Assert.Equal(hash, UpdateChecker.ExpectedChecksum(listing, Installer));
            Assert.Null(UpdateChecker.ExpectedChecksum(listing, "missing.exe"));
        }

        [Fact]
        public async Task DownloadsAndVerifiesInstaller()
        {
            byte[] content = Encoding.ASCII.GetBytes("installer bytes");
            RoutingHandler handler = Downloads(content, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

            string path = await UpdateChecker.DownloadInstallerAsync(Release(), _dir, handler);

            Assert.Equal(Path.Combine(_dir, Installer), path);
            Assert.Equal(content, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task RejectsInstallerWithWrongChecksum()
        {
            RoutingHandler handler = Downloads(Encoding.ASCII.GetBytes("tampered"), new string('0', 64));

            await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.DownloadInstallerAsync(Release(), _dir, handler));
            Assert.False(File.Exists(Path.Combine(_dir, Installer)));
        }

        [Fact]
        public async Task RejectsReleaseWithoutChecksums()
        {
            ReleaseInfo release = new ReleaseInfo("1.3.0", "u", Base + Installer, null);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                UpdateChecker.DownloadInstallerAsync(release, _dir, Routes(null, null)));
        }

        private static ReleaseInfo Release()
        {
            return new ReleaseInfo("1.3.0-nightly.20261113.6", "u", Base + Installer, Base + "SHA256SUMS.txt");
        }

        private static string NightlyJson(string version)
        {
            return "{\"tag_name\":\"nightly\",\"name\":\"RawConverter " + version + "\",\"html_url\":\"https://x/nightly\","
                + "\"prerelease\":true,\"assets\":["
                + "{\"name\":\"" + Installer + "\",\"browser_download_url\":\"" + Base + Installer + "\"},"
                + "{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"" + Base + "SHA256SUMS.txt\"}]}";
        }

        private static RoutingHandler Routes(string stable, string nightly)
        {
            return new RoutingHandler(uri =>
            {
                if (uri == UpdateChecker.LatestReleaseApi && stable != null)
                {
                    return (HttpStatusCode.OK, "{\"tag_name\":\"" + stable + "\",\"html_url\":\"https://x/" + stable + "\"}");
                }
                if (uri == UpdateChecker.NightlyReleaseApi && nightly != null)
                {
                    return (HttpStatusCode.OK, NightlyJson(nightly));
                }
                return (HttpStatusCode.NotFound, "{}");
            });
        }

        private static RoutingHandler Downloads(byte[] installer, string hash)
        {
            return new RoutingHandler(uri => uri.EndsWith("SHA256SUMS.txt")
                ? (HttpStatusCode.OK, hash + "  " + Installer + "\n")
                : (HttpStatusCode.OK, null), installer);
        }

        private class RoutingHandler : HttpMessageHandler
        {
            private readonly Func<string, (HttpStatusCode, string)> _route;
            private readonly byte[] _binary;

            public RoutingHandler(Func<string, (HttpStatusCode, string)> route, byte[] binary = null)
            {
                _route = route;
                _binary = binary;
            }

            public List<string> Requested { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string uri = request.RequestUri.ToString();
                Requested.Add(uri);
                (HttpStatusCode status, string body) = _route(uri);
                HttpContent content = body == null ? new ByteArrayContent(_binary ?? Array.Empty<byte>()) : new StringContent(body);
                return Task.FromResult(new HttpResponseMessage(status) { Content = content });
            }
        }
    }
}
