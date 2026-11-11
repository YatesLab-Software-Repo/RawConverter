using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RawConverter.Gui;
using Xunit;

namespace RawConverter.Tests
{
    public class UpdateCheckerTests
    {
        [Theory]
        [InlineData("1.2.1", "1.2.0", true)]
        [InlineData("1.10.0", "1.9.9", true)]
        [InlineData("2.0.0", "1.99.99", true)]
        [InlineData("v1.2.1", "1.2.0", true)]
        [InlineData("1.2.1", "1.2.1+abcdef", false)]
        [InlineData("1.2.0", "1.2.0", false)]
        [InlineData("1.2.0", "1.2.1", false)]
        [InlineData("1.2", "1.2.0", false)]
        [InlineData("1.2.0", "1.2.0-dev.5", true)]
        [InlineData("1.2.0-beta1", "1.2.0", false)]
        [InlineData("1.3.0-beta2", "1.3.0-beta1", true)]
        [InlineData("1.3.0-rc.10", "1.3.0-rc.9", true)]
        [InlineData("1.3.0-rc.1", "1.3.0-beta.1", true)]
        [InlineData("not-a-version", "1.2.0", false)]
        [InlineData("1.2.0", null, true)]
        public void ComparesVersions(string a, string b, bool newer)
        {
            Assert.Equal(newer, UpdateChecker.IsNewer(a, b));
        }

        [Fact]
        public void ParsesRelease()
        {
            ReleaseInfo release = UpdateChecker.ParseRelease(
                "{\"tag_name\":\"v1.2.2\",\"html_url\":\"https://github.com/x/y/releases/tag/v1.2.2\",\"assets\":[]}");

            Assert.Equal("1.2.2", release.Version);
            Assert.Equal("https://github.com/x/y/releases/tag/v1.2.2", release.Url);
        }

        [Fact]
        public void ParsesReleaseWithoutPage()
        {
            Assert.Equal(UpdateChecker.ReleasesPage, UpdateChecker.ParseRelease("{\"tag_name\":\"1.2.2\"}").Url);
            Assert.Null(UpdateChecker.ParseRelease("{}"));
        }

        [Fact]
        public async Task RequestsLatestReleaseWithUserAgent()
        {
            FakeHandler handler = new FakeHandler(HttpStatusCode.OK, "{\"tag_name\":\"1.2.2\",\"html_url\":\"u\"}");

            ReleaseInfo release = await UpdateChecker.GetLatestReleaseAsync(handler);

            Assert.Equal("1.2.2", release.Version);
            Assert.Equal(UpdateChecker.LatestReleaseApi, handler.Request.RequestUri.ToString());
            Assert.Equal("RawConverter", handler.Request.Headers.UserAgent.First().Product.Name);
        }

        [Fact]
        public async Task NoReleaseIsNull()
        {
            Assert.Null(await UpdateChecker.GetLatestReleaseAsync(new FakeHandler(HttpStatusCode.NotFound, "{}")));
        }

        [Fact]
        public async Task ServerErrorThrows()
        {
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                UpdateChecker.GetLatestReleaseAsync(new FakeHandler(HttpStatusCode.Forbidden, "rate limited")));
        }

        [Fact]
        public void SettingsRoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"), "settings.json");
            try
            {
                Assert.True(GuiSettings.Load(path).CheckForUpdatesAtStartup);
                new GuiSettings { CheckForUpdatesAtStartup = false }.Save(path);
                Assert.False(GuiSettings.Load(path).CheckForUpdatesAtStartup);

                File.WriteAllText(path, "not json");
                Assert.True(GuiSettings.Load(path).CheckForUpdatesAtStartup);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(path), true);
            }
        }

        private class FakeHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public FakeHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public HttpRequestMessage Request { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
            }
        }
    }
}
