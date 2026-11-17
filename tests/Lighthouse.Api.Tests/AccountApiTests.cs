using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Lighthouse.Api.Data;
using Lighthouse.Api.Services.Email;
using Lighthouse.Shared.Contracts.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AccountApiTests(ApiFixture fixture)
{
    private const string NewPassword = "N3w!Password-Ok";

    private ApiFactory Factory => fixture.Factory;

    private static string TokenFrom(EmailMessage message) =>
        Regex.Match(message.TextBody, @"token=([A-Za-z0-9_\-]+)").Groups[1].Value;

    private static Task<HttpResponseMessage> Forgot(HttpClient client, string email) =>
        client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequest(email, "en"));

    private static Task<HttpResponseMessage> Reset(HttpClient client, string token, string password) =>
        client.PostAsJsonAsync("/api/auth/reset-password", new ResetPasswordRequest(token, password));

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    private static IEnumerable<EmailMessage> ResetMails(IReadOnlyList<EmailMessage> all) =>
        all.Where(m => m.Subject.Contains("Reset"));

    [SqlServerFact]
    public async Task Register_QueuesConfirmationEmail_AndLinkWorksOnlyOnce()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();

        var mail = Assert.Single(Factory.Emails.To(user.Email));
        var token = TokenFrom(mail);
        Assert.NotEmpty(token);

        var first = await client.GetAsync($"/api/auth/confirm-email?token={token}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("text/html", first.Content.Headers.ContentType?.MediaType);

        var second = await client.GetAsync($"/api/auth/confirm-email?token={token}");
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [SqlServerFact]
    public async Task PasswordReset_ChangesPassword_RevokesSessions_AndTokenIsSingleUse()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Accepted, (await Forgot(client, user.Email)).StatusCode);
        var token = TokenFrom(Assert.Single(ResetMails(Factory.Emails.To(user.Email)).ToList()));

        Assert.Equal(HttpStatusCode.NoContent, (await Reset(client, token, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(client, user.Email, NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(client, user.Email, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token, "An0ther!Password")).StatusCode);

        var oldSession = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(user.Auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
    }

    [SqlServerFact]
    public async Task PasswordReset_UnlocksLockedAccount()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await Login(client, user.Email, "wrong-password");
        }

        Assert.Equal(HttpStatusCode.Locked, (await Login(client, user.Email, user.Password)).StatusCode);

        await Forgot(client, user.Email);
        var token = TokenFrom(ResetMails(Factory.Emails.To(user.Email)).Single());
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(client, token, NewPassword)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Login(client, user.Email, NewPassword)).StatusCode);
    }

    [SqlServerFact]
    public async Task ForgotPassword_ForUnknownEmail_LooksTheSame_AndSendsNothing()
    {
        var client = Factory.CreateClient();
        var email = $"nobody-{Guid.NewGuid():N}@example.com";

        var response = await Forgot(client, email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(Factory.Emails.To(email));
    }

    [SqlServerFact]
    public async Task ForgotPassword_IsThrottledPerUser()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();

        await Forgot(client, user.Email);
        await Forgot(client, user.Email);

        Assert.Single(ResetMails(Factory.Emails.To(user.Email)).ToList());
    }

    [SqlServerFact]
    public async Task ExpiredToken_IsRejected()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();
        await Forgot(client, user.Email);
        var token = TokenFrom(ResetMails(Factory.Emails.To(user.Email)).Single());

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LighthouseDbContext>();
            foreach (var row in await db.EmailTokens.Where(t => t.UserId == user.Auth.User.Id).ToListAsync())
            {
                row.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            }

            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token, NewPassword)).StatusCode);
    }

    [SqlServerFact]
    public async Task InvalidRequests_AreRejected_AndShortPasswordDoesNotConsumeToken()
    {
        var user = await Factory.RegisterAsync();
        var client = Factory.CreateClient();
        await Forgot(client, user.Email);
        var token = TokenFrom(ResetMails(Factory.Emails.To(user.Email)).Single());

        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, "not-a-real-token", NewPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reset(client, token, "short")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Forgot(client, "not-an-email")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Reset(client, token, NewPassword)).StatusCode);
    }

    [SqlServerFact]
    public async Task ResetPage_EscapesTheToken()
    {
        var client = Factory.CreateClient();
        var payload = "\"><script>alert(1)</script>";

        var response = await client.GetAsync("/reset-password?token=" + Uri.EscapeDataString(payload));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<form", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [SqlServerFact]
    public async Task EmailTokens_DoNotAppearInLogs()
    {
        using var factory = new ApiFactory(); // власний приймач журналу
        var user = await factory.RegisterAsync();
        var client = factory.CreateClient();
        await Forgot(client, user.Email);

        var confirmToken = TokenFrom(factory.Emails.To(user.Email).First(m => m.Subject.Contains("Confirm")));
        var resetToken = TokenFrom(ResetMails(factory.Emails.To(user.Email)).Single());
        await client.GetAsync($"/api/auth/confirm-email?token={confirmToken}");
        await Reset(client, resetToken, NewPassword);

        var lines = factory.Logs.Snapshot();
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, l => l.Contains(confirmToken) || l.Contains(resetToken) || l.Contains(NewPassword));
    }
}