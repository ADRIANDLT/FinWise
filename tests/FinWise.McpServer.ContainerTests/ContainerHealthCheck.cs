namespace FinWise.McpServer.ContainerTests;

/// <summary>
/// Utility for checking if the Dockerized FinWise MCP server is reachable.
/// </summary>
public static class ContainerHealthCheck
{
    private static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Polls the server health endpoint until it responds or the timeout expires.
    /// </summary>
    public static async Task<bool> IsServerReachableAsync(string baseUrl, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        using var client = new HttpClient { Timeout = MaxRequestTimeout };

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                using var cts = new CancellationTokenSource(remaining < MaxRequestTimeout ? remaining : MaxRequestTimeout);
                var response = await client.GetAsync($"{baseUrl}/health", cts.Token);
                if (response.IsSuccessStatusCode)
                    return true;
            }
            catch (HttpRequestException)
            {
                // Server not ready yet
            }
            catch (TaskCanceledException)
            {
                // Timeout on individual request
            }

            await Task.Delay(500);
        }

        return false;
    }

    /// <summary>
    /// Waits for the server to become reachable. Throws TimeoutException if not reachable within timeout.
    /// </summary>
    public static async Task WaitForServerAsync(string baseUrl, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(60);
        if (!await IsServerReachableAsync(baseUrl, effectiveTimeout))
        {
            throw new TimeoutException(
                $"FinWise MCP server at {baseUrl} did not become reachable within {effectiveTimeout.TotalSeconds}s");
        }
    }
}
