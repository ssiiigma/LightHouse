using System.Net.Http.Json;
using Lighthouse.Shared.Contracts.Auth;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class SensitiveDataLogTests
{
    [SqlServerFact]
    public async Task Logs_DoNotContainPasswordsTokensOrAuthorizationHeader()
    {
        using var factory = new ApiFactory(); // власний приймач журналу
        const string password = "S3cret!Passphrase-ForLogTest";
        const string wrongPassword = "Wr0ng!Passphrase-ForLogTest";

        var user = await factory.RegisterAsync(password);
        var anonymous = factory.CreateClient();

        await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, wrongPassword));
        var login = await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        await user.Client.GetAsync("/api/tasks");
        await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        await anonymous.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(auth.RefreshToken));

        var lines = factory.Logs.Snapshot();
        Assert.NotEmpty(lines); // порожній журнал означав би, що приймач не підключений, а перевірка нічого не доводить

        string[] secrets =
        [
            password, wrongPassword,
            user.Auth.AccessToken, user.Auth.RefreshToken,
            auth.AccessToken, auth.RefreshToken,
            "Bearer "
        ];

        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.Ordinal));
        }
    }
}