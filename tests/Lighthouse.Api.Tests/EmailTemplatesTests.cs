using Lighthouse.Api.Services.Email;

namespace Lighthouse.Api.Tests;

public class EmailTemplatesTests
{
    [Fact]
    public void PasswordReset_UsesRequestedLanguage_AndContainsLinkAndCode()
    {
        var uk = EmailTemplates.PasswordReset("a@b.c", "Анна", "TOKEN123", "http://x/reset?token=TOKEN123", "uk");
        var en = EmailTemplates.PasswordReset("a@b.c", "Anna", "TOKEN123", "http://x/reset?token=TOKEN123", "en");

        Assert.Contains("Відновлення", uk.Subject);
        Assert.Contains("Reset", en.Subject);
        Assert.Contains("TOKEN123", uk.TextBody);
        Assert.Contains("http://x/reset?token=TOKEN123", en.TextBody);
        Assert.Contains("24", uk.TextBody);
    }

    [Fact]
    public void Html_EncodesUserSuppliedValues()
    {
        var mail = EmailTemplates.Confirmation("a@b.c", "<b>Evil</b>", "http://x/?a=1&b=2", "en");

        Assert.DoesNotContain("<b>Evil</b>", mail.HtmlBody);
        Assert.Contains("&lt;b&gt;Evil&lt;/b&gt;", mail.HtmlBody);
        Assert.Contains("&amp;", mail.HtmlBody);
    }
}