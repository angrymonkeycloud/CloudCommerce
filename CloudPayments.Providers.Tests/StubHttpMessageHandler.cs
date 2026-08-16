using System.Net;

namespace CloudPayments.Providers.Tests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> response) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Requests.Add(request);
        return Task.FromResult(response(request, Calls));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}
