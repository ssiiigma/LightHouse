using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Auth;
using Lighthouse.Shared.Contracts.Profile;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class GoogleAuthApiTests(ApiFixture fixture)
{
    private static string NewEmail() => $"g-{Guid.NewGuid():N}@example.com";
    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    private static Task<HttpResponseMessage> GoogleLogin(HttpClient client, string token) =>
        client.PostAsJsonAsync("/api/auth/google", new GoogleLoginRequest(token));

    private static async Task<AuthResponse> GoogleAuthAsync(HttpClient client, string token)
    {
        var response = await GoogleLogin(client, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    [SqlServerFact]
    public async Task NewGoogleUser_IsCreatedConfirmed_AndSameSubjectReturnsTheSameAccount()
    {
        var client = fixture.Factory.CreateClient();
        var (subject, email) = (NewSubject(), NewEmail());

        var first = await GoogleAuthAsync(client, FakeGoogleTokenValidator.Token(subject, email, name: "Ірина"));
        Assert.Equal(email, first.User.Email);
        Assert.Equal("Ірина", first.User.Name);

        var api = fixture.Factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.True((await api.GetFromJsonAsync<ProfileDto>("/api/profile"))!.IsEmailConfirmed);

        var second = await GoogleAuthAsync(client, FakeGoogleTokenValidator.Token(subject, email));
        Assert.Equal(first.User.Id, second.User.Id);
    }

    [SqlServerFact]
    public async Task ExistingPasswordAccount_IsLinkedByVerifiedEmail_AndPasswordStillWorks()
    {
        var user = await fixture.Factory.RegisterAsync();
        var client = fixture.Factory.CreateClient();

        var google = await GoogleAuthAsync(
            client, FakeGoogleTokenValidator.Token(NewSubject(), user.Email.ToUpperInvariant()));

        Assert.Equal(user.Auth.User.Id, google.User.Id);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, user.Password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [SqlServerFact]
    public async Task AnotherGoogleAccountWithTheSameEmail_IsRejected()
    {
        var client = fixture.Factory.CreateClient();
        var email = NewEmail();
        await GoogleAuthAsync(client, FakeGoogleTokenValidator.Token(NewSubject(), email));

        var response = await GoogleLogin(client, FakeGoogleTokenValidator.Token(NewSubject(), email));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [SqlServerFact]
    public async Task UnverifiedEmailAndInvalidTokens_AreRejected()
    {
        var client = fixture.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await GoogleLogin(client, FakeGoogleTokenValidator.Token(NewSubject(), NewEmail(), verified: false))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GoogleLogin(client, "garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GoogleLogin(client, "")).StatusCode);
    }

    [SqlServerFact]
    public async Task GoogleAccount_HasNoUsablePassword_UntilItIsSetThroughRecovery()
    {
        var client = fixture.Factory.CreateClient();
        var email = NewEmail();
        await GoogleAuthAsync(client, FakeGoogleTokenValidator.Token(NewSubject(), email));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Guess!Password1"))).StatusCode);

        await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email, "en"));
        var mail = fixture.Factory.Emails.To(email).Single(m => m.Subject.Contains("Reset"));
        var token = System.Text.RegularExpressions.Regex.Match(mail.TextBody, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, "N3w!Password-Ok"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "N3w!Password-Ok"))).StatusCode);
    }

    [SqlServerFact]
    public async Task GoogleSession_CanBeRefreshed()
    {
        var client = fixture.Factory.CreateClient();
        var auth = await GoogleAuthAsync(client, FakeGoogleTokenValidator.Token(NewSubject(), NewEmail()));

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }
}