using System.Net;

namespace CloudLogistics.Providers.Tests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<(HttpMethod Method, Uri? Uri, string? Authorization)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add((request.Method, request.RequestUri, request.Headers.Authorization?.Scheme));
        return Task.FromResult(response(request, Calls));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
