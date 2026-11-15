using System.Net;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Profile;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class ProfileApiTests(ApiFixture fixture)
{
    [SqlServerFact]
    public async Task TimeZone_ValidId_IsAccepted_UnknownId_IsRejected()
    {
        var user = await fixture.Factory.RegisterAsync();

        var ok = await user.Client.PutAsJsonAsync("/api/profile/timezone", new TimeZoneDto("Europe/London"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Europe/London", (await ok.Content.ReadFromJsonAsync<TimeZoneDto>())!.TimeZoneId);

        var bad = await user.Client.PutAsJsonAsync("/api/profile/timezone", new TimeZoneDto("Mars/Phobos"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [SqlServerFact]
    public async Task TimeZone_RequiresAuthentication()
    {
        var anonymous = fixture.Factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync("/api/profile/timezone", new TimeZoneDto("Europe/London"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}