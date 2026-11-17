using System.Net;

namespace Lighthouse.Api.Services.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

/// <summary>Тексти листів двома мовами (uk за замовчуванням, en). Користувацькі значення в HTML екрануються.</summary>
public static class EmailTemplates
{
    public static EmailMessage Confirmation(string to, string name, string link, string? language)
    {
        var en = IsEnglish(language);
        var subject = en ? "Confirm your email for Lighthouse" : "Підтвердіть e-mail у Lighthouse";
        var intro = en
            ? $"Hi, {name}! Confirm your email address using the link below (valid for 24 hours):"
            : $"Вітаємо, {name}! Підтвердіть адресу e-mail за посиланням (дійсне 24 години):";
        var ignore = en
            ? "If you did not create an account, just ignore this message."
            : "Якщо ви не створювали обліковий запис, просто проігноруйте цей лист.";

        return new EmailMessage(
            to, subject,
            $"{intro}\n{link}\n\n{ignore}",
            Wrap($"<p>{Enc(intro)}</p><p><a href=\"{Enc(link)}\">{Enc(link)}</a></p><p style=\"color:#888\">{Enc(ignore)}</p>"));
    }

    public static EmailMessage PasswordReset(string to, string name, string token, string link, string? language)
    {
        var en = IsEnglish(language);
        var subject = en ? "Reset your Lighthouse password" : "Відновлення пароля Lighthouse";
        var intro = en
            ? $"Hi, {name}! To reset your password, open the link below (valid for 24 hours):"
            : $"Вітаємо, {name}! Щоб скинути пароль, відкрийте посилання (дійсне 24 години):";
        var code = en ? "Or enter this code in the app:" : "Або введіть цей код у застосунку:";
        var ignore = en
            ? "If you did not request a password reset, just ignore this message."
            : "Якщо ви не запитували скидання пароля, просто проігноруйте цей лист.";

        return new EmailMessage(
            to, subject,
            $"{intro}\n{link}\n\n{code} {token}\n\n{ignore}",
            Wrap($"<p>{Enc(intro)}</p><p><a href=\"{Enc(link)}\">{Enc(link)}</a></p>" +
                 $"<p>{Enc(code)} <code style=\"font-size:16px\">{Enc(token)}</code></p>" +
                 $"<p style=\"color:#888\">{Enc(ignore)}</p>"));
    }

    private static bool IsEnglish(string? language) =>
        language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true;

    private static string Enc(string value) => WebUtility.HtmlEncode(value);

    private static string Wrap(string body) =>
        "<div style=\"font-family:Arial,sans-serif;font-size:14px;color:#1A2540;line-height:1.5\">" + body + "</div>";
}