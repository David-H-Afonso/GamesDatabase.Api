using System.Diagnostics;
using System.Net;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Credential-free, bounded, verified raster downloads for ZIP and account network exports.</summary>
public sealed class ExportMediaDownloader(IHttpClientFactory factory, IOptions<ExportMediaOptions> options, ILogger<ExportMediaDownloader> logger)
{
    public const int MaximumImageBytes = 16 * 1024 * 1024;
    public const long MaximumExportMediaBytes = 128L * 1024 * 1024;
    private readonly HttpClient client = factory.CreateClient(ExportMediaHttpClient.Name);
    private readonly ExportMediaDestinationPolicy policy = new(options.Value);
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private long consumedBytes;

    public async Task<VerifiedImageContent?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (elapsed.Elapsed >= TimeSpan.FromMinutes(2) || consumedBytes >= MaximumExportMediaBytes
            || !Uri.TryCreate(url, UriKind.Absolute, out var current)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Min(10, Math.Max(0.001, 120 - elapsed.Elapsed.TotalSeconds))));
        try
        {
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                if (!policy.IsAllowedUri(current)) return null;
                using var request = new HttpRequestMessage(HttpMethod.Get, current)
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (redirects == 3 || location is null || !Uri.TryCreate(current, location, out var next)) return null;
                    current = next;
                    continue;
                }
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumImageBytes) return null;
                var declaredType = response.Content.Headers.ContentType?.MediaType;
                if (declaredType is not null && !declaredType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    && !declaredType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)) return null;
                using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var output = new MemoryStream();
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, deadline.Token)) != 0)
                {
                    consumedBytes += read;
                    if (output.Length + read > MaximumImageBytes || consumedBytes > MaximumExportMediaBytes) return null;
                    await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                }
                deadline.Token.ThrowIfCancellationRequested();
                return VerifiedImageContent.TryReadRaster(output.ToArray(), out var image) ? image : null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or ArgumentException)
        {
            // URL queries and exception text can contain signed URLs/credentials. Do
            // not include either in export diagnostics.
            logger.LogWarning("An export artwork download was rejected, unavailable or exceeded its limits.");
        }
        return null;
    }
}
