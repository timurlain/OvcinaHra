using System.Net;
using System.Text;

namespace OvcinaHra.Api.Tests.Fixtures;

public sealed class SlowHttpMessageHandler(TimeSpan delay) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
    }
}
