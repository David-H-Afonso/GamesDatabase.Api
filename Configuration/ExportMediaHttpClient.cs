using System.Net;
using System.Net.Sockets;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Configuration;

public static class ExportMediaHttpClient
{
    public const string Name = "export-media";

    public static IServiceCollection AddExportMediaDownloads(this IServiceCollection services, IConfiguration? configuration)
    {
        if (configuration is not null)
        {
            services.Configure<ExportMediaOptions>(configuration.GetSection(ExportMediaOptions.SectionName));
            services.PostConfigure<ExportMediaOptions>(options =>
            {
                options.AllowedPrivateBaseUrls ??= [];
                // Existing configured image servers remain usable, but only their
                // artwork subtree, never arbitrary private-network URLs/ports.
                foreach (var key in new[] { "ImageSettings:BaseUrl", "NetworkSync:ImageBaseUrl" })
                {
                    var value = configuration[key];
                    if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo))
                        options.AllowedPrivateBaseUrls.Add(value!.TrimEnd('/') + "/game-images/");
                }
            });
        }
        else services.AddOptions<ExportMediaOptions>();
        services.AddHttpClient(Name, client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan; // bounded across headers + body by the downloader
            client.DefaultRequestHeaders.Accept.ParseAdd("image/*");
        }).ConfigurePrimaryHttpMessageHandler(provider => CreateHandler(provider.GetRequiredService<IOptions<ExportMediaOptions>>().Value));
        services.AddScoped<ExportMediaDownloader>();
        return services;
    }

    public static HttpMessageHandler CreateHandler(ExportMediaOptions options)
    {
        var policy = new ExportMediaDestinationPolicy(options);
        var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Credentials = null,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            MaxResponseHeadersLength = 16,
            MaxResponseDrainSize = 0, // rejection/redirect must not read an unbudgeted body on disposal
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var uri = context.InitialRequestMessage.RequestUri;
                if (uri is null || !policy.IsAllowedUri(uri)
                    || !context.DnsEndPoint.Host.Trim('[', ']').Equals(uri.IdnHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase)
                    || context.DnsEndPoint.Port != uri.Port) throw new HttpRequestException("Media destination is not permitted.");
                var addresses = IPAddress.TryParse(context.DnsEndPoint.Host.Trim('[', ']'), out var literal)
                    ? [literal] : await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                // Reject mixed public/private DNS answers instead of selecting a convenient
                // answer. Connect to the checked IP, not the hostname (no DNS-rebinding gap).
                if (addresses.Length == 0 || addresses.Any(address => !policy.IsAllowedAddress(uri, address)))
                    throw new HttpRequestException("Media address is not permitted.");
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, uri.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException) { socket.Dispose(); }
                    catch { socket.Dispose(); throw; }
                }
                throw new HttpRequestException("Media server could not be reached.");
            }
        };
        return new CredentialFreeMediaHandler(policy) { InnerHandler = sockets };
    }

    // Defense in depth if someone accidentally adds default headers to the named client.
    public sealed class CredentialFreeMediaHandler(ExportMediaDestinationPolicy policy) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null || !policy.IsAllowedUri(request.RequestUri)) throw new HttpRequestException("Media destination is not permitted.");
            request.Headers.Remove("Authorization");
            request.Headers.Remove("Proxy-Authorization");
            request.Headers.Remove("Cookie");
            request.Headers.Host = null;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
