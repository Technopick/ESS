using System.Net;
using System.Text.Json;
using Ess.Application.Abstractions;
using Ess.Infrastructure.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ess.Api.Tests;

public sealed class SmsGatewayTests
{
    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Reply = """{"status":true,"responseCode":"0200","message":"Accepted","messageId":"MSG-1"}""";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply) };
        }
    }

    private static (ConfigurableSmsSender Sender, Capture Handler) Create(SmsOptions o)
    {
        var h = new Capture();
        return (new ConfigurableSmsSender(new HttpClient(h), Options.Create(o), NullLogger<ConfigurableSmsSender>.Instance), h);
    }

    [Fact]
    public async Task Mobile_Sasa_gets_a_bearer_token_sender_and_international_number()
    {
        var (sender, h) = Create(new SmsOptions { Provider = "MobileSasa", ApiKey = "mbs_test", SenderId = "COMPANY" });
        Assert.True(sender.IsConfigured);
        var id = await sender.SendAsync(new SmsMessage("0712 345 678", "Your leave was approved."));
        Assert.Equal("MSG-1", id);
        Assert.Equal(HttpMethod.Post, h.Request!.Method);
        Assert.Equal(SmsOptions.MobileSasaDefaultUrl, h.Request.RequestUri!.ToString());
        Assert.Equal("Bearer", h.Request.Headers.Authorization!.Scheme);
        Assert.Equal("mbs_test", h.Request.Headers.Authorization.Parameter);
        using var json = JsonDocument.Parse(h.Body!);
        Assert.Equal("COMPANY", json.RootElement.GetProperty("senderID").GetString());
        Assert.Equal("254712345678", json.RootElement.GetProperty("phone").GetString());
        Assert.Equal("Your leave was approved.", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Mobile_Sasa_local_number_format_is_available()
    {
        var (sender, h) = Create(new SmsOptions { Provider = "MobileSasa", ApiKey = "mbs_test", SenderId = "COMPANY", PhoneFormat = "Local" });
        await sender.SendAsync(new SmsMessage("+254712345678", "Hello"));
        using var json = JsonDocument.Parse(h.Body!);
        Assert.Equal("0712345678", json.RootElement.GetProperty("phone").GetString());
    }

    [Fact]
    public void Mobile_Sasa_needs_a_token_and_sender_name()
    {
        Assert.False(Create(new SmsOptions { Provider = "MobileSasa", ApiKey = "mbs_test" }).Sender.IsConfigured);
        Assert.False(Create(new SmsOptions { Provider = "MobileSasa", SenderId = "COMPANY" }).Sender.IsConfigured);
    }

    [Fact]
    public async Task Mobile_Sasa_rejections_are_thrown_so_the_dispatcher_retries()
    {
        var (sender, h) = Create(new SmsOptions { Provider = "MobileSasa", ApiKey = "mbs_test", SenderId = "COMPANY" });
        h.Reply = """{"status":false,"responseCode":"0401","message":"Insufficient balance"}""";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new SmsMessage("0712345678", "Hi")));
        Assert.Contains("Insufficient balance", ex.Message);

        h.Status = HttpStatusCode.Unauthorized;
        h.Reply = """{"message":"Unauthenticated."}""";
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(new SmsMessage("0712345678", "Hi")));
    }

    [Theory]
    [InlineData("""{"status":true,"messageId":"A1"}""", "A1")]
    [InlineData("""{"status":"success","message_id":"B2"}""", "B2")]
    [InlineData("", null)]
    public void Mobile_Sasa_replies_are_read_tolerantly(string body, string? id) =>
        Assert.Equal(id, ConfigurableSmsSender.ParseMobileSasaResponse(200, body));
}
