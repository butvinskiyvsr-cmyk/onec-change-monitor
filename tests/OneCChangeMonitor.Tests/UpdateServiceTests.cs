using System.Net;
using System.Security.Cryptography;
using System.Text;
using OneCChangeMonitor.Desktop;

namespace OneCChangeMonitor.Tests;

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task FindsInstallerAssetsAndVerifiesDownloadedFile()
    {
        var installer = Encoding.UTF8.GetBytes("verified installer payload");
        var checksum = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        var json = """
            {
              "tag_name": "v0.4.0",
              "name": "ConfigScope v0.4.0",
              "body": "Test release",
              "html_url": "https://example.invalid/releases/v0.4.0",
              "assets": [
                { "name": "ConfigScope-Setup-0.4.0.exe", "browser_download_url": "https://example.invalid/setup.exe" },
                { "name": "ConfigScope-Setup-0.4.0.exe.sha256", "browser_download_url": "https://example.invalid/setup.sha256" }
              ]
            }
            """;
        using var handler = new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/setup.exe" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) },
            "/setup.sha256" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{checksum}  ConfigScope-Setup-0.4.0.exe") },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
        });
        var root = Path.Combine(Path.GetTempPath(), "configscope-update-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new UpdateService(new HttpClient(handler), root);
            var update = await service.CheckAsync(CancellationToken.None);
            var path = await service.DownloadInstallerAsync(update, null, CancellationToken.None);

            Assert.True(update.CanInstall);
            Assert.Equal(new Version(0, 4, 0), update.Version);
            Assert.Equal(installer, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RejectsInstallerWithUnexpectedChecksum()
    {
        var installer = Encoding.UTF8.GetBytes("untrusted payload");
        using var handler = new StubHttpHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("sha256", StringComparison.OrdinalIgnoreCase)
                ? new StringContent($"{new string('0', 64)}  ConfigScope-Setup-9.0.0.exe")
                : new ByteArrayContent(installer)
        });
        var root = Path.Combine(Path.GetTempPath(), "configscope-update-tests", Guid.NewGuid().ToString("N"));
        var update = new UpdateInfo(
            new Version(9, 0, 0),
            "v9.0.0",
            "Test",
            string.Empty,
            new Uri("https://example.invalid/release"),
            new Uri("https://example.invalid/setup.exe"),
            new Uri("https://example.invalid/setup.sha256"),
            true);
        try
        {
            var service = new UpdateService(new HttpClient(handler), root);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DownloadInstallerAsync(update, null, CancellationToken.None));

            Assert.Contains("Контрольная сумма", exception.Message);
            Assert.False(File.Exists(Path.Combine(root, "9.0.0", "ConfigScope-Setup-9.0.0.exe")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
