using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Lighthouse.Api.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Lighthouse.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class TokenValidationTests(ApiFixture fixture)
{
    private static readonly string[] ProtectedPaths =
        ["/api/tasks", "/api/categories", "/api/habits", "/api/recommendations/ranking"];

    [SqlServerFact]
    public async Task ProtectedEndpoints_RejectMissingMalformedForgedExpiredAndForeignAudienceTokens()
    {
        var jwt = fixture.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var client = fixture.Factory.CreateClient();
        var now = DateTime.UtcNow;

        var rejected = new Dictionary<string, string?>
        {
            ["no token"] = null,
            ["not a jwt"] = "not-a-jwt",
            ["forged signature"] = CreateToken(jwt, "a-completely-different-secret-key-0123456789", now, now.AddMinutes(10)),
            ["expired"] = CreateToken(jwt, jwt.Secret, now.AddHours(-2), now.AddHours(-1)),
            ["wrong audience"] = CreateToken(jwt, jwt.Secret, now, now.AddMinutes(10), audience: "Someone.Else")
        };

        foreach (var (name, token) in rejected)
        {
            foreach (var path in ProtectedPaths)
            {
                var status = await GetStatusAsync(client, path, token);
                Assert.True(status == HttpStatusCode.Unauthorized, $"{name} → {path}: очікувалось 401, отримано {(int)status}");
            }
        }
    }

    [SqlServerFact]
    public async Task ProtectedEndpoints_AcceptCorrectlySignedToken()
    {
        // Контрольний випадок: доводить, що тестові токени побудовано правильно, а відмови вище не випадкові.
        var jwt = fixture.Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var client = fixture.Factory.CreateClient();
        var now = DateTime.UtcNow;
        var token = CreateToken(jwt, jwt.Secret, now, now.AddMinutes(10));

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(client, "/api/tasks", token));
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(client, "/api/categories", token));
    }

    private static async Task<HttpStatusCode> GetStatusAsync(HttpClient client, string path, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static string CreateToken(
        JwtOptions jwt, string secret, DateTime notBefore, DateTime expires, string? audience = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            Issuer = jwt.Issuer,
            Audience = audience ?? jwt.Audience,
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), SecurityAlgorithms.HmacSha256)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}