using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Ess.Api.Tests;

/// <summary>Look and feel: public read for the sign-in page, admin-only changes, readable colours and safe logos.</summary>
[Collection("api")]
public sealed class BrandingTests : ApiTestBase
{
    public BrandingTests(EssApiFactory f) : base(f) { }

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [SkippableFact]
    public async Task Admin_changes_the_look_and_feel_and_everyone_sees_it()
    {
        Skip.If(EssApiFactory.BaseCs is null);
        var anon = _f.Anonymous();
        var initial = await anon.GetFromJsonAsync<JsonElement>("/api/branding", Json);
        initial.GetProperty("appName").GetString().Should().NotBeNullOrEmpty();

        var body = new { appName = "Technopick ESS", welcomeText = "Karibu", primary = "#1B5E20", secondary = "#8d6e00", defaultMode = "light", cornerRadius = 12 };
        (await anon.PutAsJsonAsync("/api/branding", body, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var staff = await UserAsync(Baraka);
        (await staff.PutAsJsonAsync("/api/branding", body, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await _f.SignInAsync(EssApiFactory.AdminUser, EssApiFactory.AdminPassword);
        var pale = await admin.PutAsJsonAsync("/api/branding", body with { primary = "#ffeb3b" }, Json);
        pale.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pale.Content.ReadAsStringAsync()).Should().Contain("too hard to read");
        (await admin.PutAsJsonAsync("/api/branding", body with { primary = "red" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync("/api/branding", body with { defaultMode = "sepia" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.PutAsJsonAsync("/api/branding", body, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        var seen = await anon.GetFromJsonAsync<JsonElement>("/api/branding", Json);
        seen.GetProperty("appName").GetString().Should().Be("Technopick ESS");
        seen.GetProperty("primary").GetString().Should().Be("#1b5e20");
        seen.GetProperty("defaultMode").GetString().Should().Be("light");
        seen.GetProperty("logoUrl").ValueKind.Should().Be(JsonValueKind.Null);

        // Logos: SVG (can carry script) and non-images are refused; PNG is stored and served with its type.
        static MultipartFormDataContent Form(byte[] bytes, string name, string type)
        {
            var c = new ByteArrayContent(bytes);
            c.Headers.ContentType = new MediaTypeHeaderValue(type);
            return new MultipartFormDataContent { { c, "file", name } };
        }
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();
        (await admin.PostAsync("/api/branding/logo", Form(svg, "logo.svg", "image/svg+xml"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/branding/logo", Form(Png, "logo.png", "image/png"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var withLogo = await anon.GetFromJsonAsync<JsonElement>("/api/branding", Json);
        var url = withLogo.GetProperty("logoUrl").GetString()!;
        var logo = await anon.GetAsync(url);
        logo.StatusCode.Should().Be(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await logo.Content.ReadAsByteArrayAsync()).Should().Equal(Png);

        (await admin.DeleteAsync("/api/branding/logo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anon.GetAsync("/api/branding/logo")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Put the defaults back for other tests.
        (await admin.PutAsJsonAsync("/api/branding", new { appName = "Employee Self-Service", welcomeText = (string?)null, primary = "#0b5394", secondary = "#38761d", defaultMode = "system", cornerRadius = 10 }, Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
