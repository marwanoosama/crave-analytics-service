namespace Crave.Analytics.Infrastructure.CoreClient;

using System.Net.Http.Json;
using Crave.Analytics.Infrastructure.Config;
using Crave.Analytics.Infrastructure.Http;
using Microsoft.AspNetCore.Http;

public sealed record CoreEnvelope<T>(bool Success, T? Data);

public sealed class CoreClient(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
{
    private const int Attempts = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(500);

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var delay = InitialDelay;
        AppException? lastError = null;

        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            HttpResponseMessage? response = null;
            lastError = null;
            try
            {
                response = await SendAsync(path, cancellationToken);
                if ((int)response.StatusCode >= 500)
                    lastError = new AppException($"core-service {(int)response.StatusCode}",
                        StatusCodes.Status503ServiceUnavailable);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // caller gave up — never swallow
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                lastError = new AppException("core-service unreachable",
                    StatusCodes.Status503ServiceUnavailable);
            }

            if (lastError is null && response is not null)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new AppException($"core-service {(int)response.StatusCode}: {body}",
                        (int)response.StatusCode);
                }

                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                    ?? throw new AppException("core-service returned an empty body",
                        StatusCodes.Status503ServiceUnavailable);
            }

            if (attempt < Attempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxDelay.TotalMilliseconds));
            }
        }

        throw lastError!;
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("core");
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("api-key", Env.CoreInternalApiKey);

        var correlationId = httpContextAccessor.HttpContext?
            .Request.Headers["X-CorrelationId"].FirstOrDefault();
        if (!string.IsNullOrEmpty(correlationId))
            request.Headers.Add("X-CorrelationId", correlationId);

        return await client.SendAsync(request, cancellationToken);
    }
}
