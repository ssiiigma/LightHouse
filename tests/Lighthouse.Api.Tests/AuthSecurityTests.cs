using System.Net;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Auth;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthSecurityTests(ApiFixture fixture)
{
    [SqlServerFact]
    public async Task Login_AfterFiveWrongPasswords_LocksAccount_EvenForCorrectPassword()
    {
        var user = await fixture.Factory.RegisterAsync();
        var client = fixture.Factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, "wrong-password"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        var correct = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, user.Password));

        Assert.Equal(HttpStatusCode.Locked, correct.StatusCode);
    }

    [SqlServerFact]
    public async Task Refresh_ReusingRotatedToken_IsRejected_WhileTheNewTokenWorks()
    {
        var user = await fixture.Factory.RegisterAsync();
        var client = fixture.Factory.CreateClient();
        var oldToken = user.Auth.RefreshToken;

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(oldToken));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var renewed = (await first.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(oldToken, renewed.RefreshToken);

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(oldToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var next = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(renewed.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [SqlServerFact]
    public async Task Logout_RevokesRefreshToken()
    {
        var user = await fixture.Factory.RegisterAsync();
        var client = fixture.Factory.CreateClient();

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(user.Auth.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(user.Auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [SqlServerFact]
    public async Task AuthEndpoints_AreRateLimited_WhileHealthAndOtherEndpointsAreNot()
    {
        using var limited = new ApiFactory(options =>
        {
            options.AuthPermitLimit = 3;
            options.AuthWindowSeconds = 600;
        });
        var client = limited.CreateClient();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "x"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody@example.com", "x"));
        Assert.Equal((HttpStatusCode)429, blocked.StatusCode);
        Assert.True(blocked.Headers.Contains("Retry-After"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tasks")).StatusCode);
    }
}