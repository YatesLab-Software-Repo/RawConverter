using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace RawConverter.Tests
{
    /// <summary>
    /// Update checks read version.json downloads, which GitHub does not rate-limit like its API, and fall back to the
    /// API only for releases without the file.
    /// </summary>
    public class VersionFileTests
    {
        private const string Repo = "https://github.com/YatesLab-Software-Repo/RawConverter/releases/";

        [Fact]
        public void ParsesVersionFile()
        {
            ReleaseInfo release = UpdateChecker.ParseVersionFile(
                "{\"version\": \"1.3.0-nightly.20261117.11\", \"tag\": \"nightly\", "
                + "\"installer\": \"RawConverter-1.3.0-nightly.20261117.11-win-x64-setup.exe\"}");

            Assert.Equal("1.3.0-nightly.20261117.11", release.Version);
            Assert.Equal(Repo + "tag/nightly", release.Url);
            Assert.Equal(Repo + "download/nightly/RawConverter-1.3.0-nightly.20261117.11-win-x64-setup.exe", release.InstallerUrl);
            Assert.Equal(Repo + "download/nightly/SHA256SUMS.txt", release.ChecksumsUrl);
        }

        [Fact]
        public void RejectsVersionFileWithoutVersion()
        {
            Assert.Null(UpdateChecker.ParseVersionFile("{\"tag\": \"nightly\"}"));
        }

        [Fact]
        public async Task UsesVersionFilesWithoutTheApi()
        {
            Handler handler = new Handler(new Dictionary<string, string>
            {
                [UpdateChecker.LatestVersionFile] = "{\"version\": \"1.3.0\", \"tag\": \"1.3.0\", \"installer\": \"RawConverter-1.3.0-win-x64-setup.exe\"}",
                [UpdateChecker.NightlyVersionFile] = "{\"version\": \"1.3.1-nightly.20261201.20\", \"tag\": \"nightly\", \"installer\": \"x-win-x64-setup.exe\"}",
            });

            ReleaseInfo update = await UpdateChecker.CheckForUpdateAsync("1.3.0-nightly.20261117.10", handler);

            Assert.Equal("1.3.1-nightly.20261201.20", update.Version);
            Assert.DoesNotContain(handler.Requested, u => u.StartsWith("https://api.github.com"));
        }

        [Fact]
        public async Task FallsBackToTheApiWithoutVersionFile()
        {
            Handler handler = new Handler(new Dictionary<string, string>
            {
                [UpdateChecker.LatestReleaseApi] = "{\"tag_name\": \"1.2.1\", \"html_url\": \"https://x/1.2.1\"}",
            });

            ReleaseInfo update = await UpdateChecker.CheckForUpdateAsync("1.2.0", handler);

            Assert.Equal("1.2.1", update.Version);
            Assert.Contains(UpdateChecker.LatestReleaseApi, handler.Requested);
        }

        [Fact]
        public async Task ExplainsTheApiRateLimit()
        {
            Handler handler = new Handler(new Dictionary<string, string>(), rateLimited: true);

            HttpRequestException e = await Assert.ThrowsAsync<HttpRequestException>(() =>
                UpdateChecker.CheckForUpdateAsync("1.2.0", handler));

            Assert.StartsWith("GitHub is limiting update checks from this network; try again in ", e.Message);
            Assert.EndsWith(" minutes.", e.Message);
        }

        /// <summary>
        /// Serves the given URLs; any other is 404, except the API when rate-limited (403, limit reset in 30 minutes).
        /// </summary>
        private class Handler : HttpMessageHandler
        {
            private readonly Dictionary<string, string> _responses;
            private readonly bool _rateLimited;

            public Handler(Dictionary<string, string> responses, bool rateLimited = false)
            {
                _responses = responses;
                _rateLimited = rateLimited;
            }

            public List<string> Requested { get; } = new List<string>();

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string uri = request.RequestUri.ToString();
                Requested.Add(uri);
                HttpResponseMessage response;
                if (_responses.TryGetValue(uri, out string body))
                {
                    response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
                }
                else if (_rateLimited && uri.StartsWith("https://api.github.com"))
                {
                    response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("rate limit exceeded") };
                    response.Headers.Add("X-RateLimit-Remaining", "0");
                    response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds().ToString());
                }
                else
                {
                    response = new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
                }
                return Task.FromResult(response);
            }
        }
    }
}
