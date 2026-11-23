using System.Net;

namespace Lighthouse.Api.Infrastructure;

/// <summary>Мінімальні двомовні сторінки, що відкриваються за посиланнями з листів.</summary>
public static class AccountPages
{
    public static string ConfirmResult(bool ok) => Page(
        ok ? "E-mail підтверджено / Email confirmed" : "Посилання недійсне / The link is invalid",
        ok ? "Дякуємо! Можете повернутися до застосунку. / Thank you! You can return to the app."
           : "Посилання недійсне або прострочене. / The link is invalid or has expired.",
        string.Empty);

    /// <summary>Форма нового пароля. Токен передається в атрибуті data-token (екранується) і не вставляється в скрипт.</summary>
    public static string ResetForm(string token) => Page(
        "Новий пароль / New password",
        "Введіть новий пароль (мінімум 8 символів). / Enter a new password (at least 8 characters).",
        $$"""
        <form id="f" data-token="{{WebUtility.HtmlEncode(token)}}">
          <input id="p" type="password" minlength="8" maxlength="128" required placeholder="Новий пароль / New password">
          <button type="submit">Змінити / Change</button>
        </form>
        <p id="m"></p>
        <script>
        const form = document.getElementById('f'), msg = document.getElementById('m');
        form.addEventListener('submit', async (e) => {
          e.preventDefault();
          msg.textContent = '';
          try {
            const r = await fetch('/api/auth/reset-password', {
              method: 'POST', headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ token: form.dataset.token, newPassword: document.getElementById('p').value })
            });
            msg.textContent = r.ok
              ? 'Пароль змінено. Увійдіть у застосунку. / Password changed. Sign in in the app.'
              : 'Помилка: код недійсний або пароль закороткий. / Error: invalid code or the password is too short.';
          } catch { msg.textContent = 'Немає з’єднання. / No connection.'; }
        });
        </script>
        """);

    private static string Page(string title, string message, string extra) => $$"""
        <!doctype html>
        <html lang="uk"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Lighthouse</title>
        <style>
          body{margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#0A0E1A;color:#F5EDD8;font-family:Arial,sans-serif}
          main{max-width:420px;padding:32px;text-align:center}
          h1{font-size:12px;letter-spacing:4px;color:#E8B84B;margin:0 0 24px}
          h2{font-size:22px;margin:0 0 12px}
          p{color:#cfc8b8;line-height:1.5}
          input,button{font-size:16px;padding:12px 16px;border-radius:12px;border:1px solid #33415f;margin:6px 0;width:100%;box-sizing:border-box}
          input{background:#1A2540;color:#F5EDD8}
          button{background:#E8B84B;color:#0A0E1A;border:0;font-weight:bold;cursor:pointer}
        </style></head>
        <body><main><h1>LIGHTHOUSE</h1><h2>{{WebUtility.HtmlEncode(title)}}</h2><p>{{WebUtility.HtmlEncode(message)}}</p>{{extra}}</main></body></html>
        """;
    
    public static string CalendarResult(bool ok) => Page(
        ok ? "Календар підключено / Calendar connected" : "Не вдалося підключити календар / Could not connect the calendar",
        ok ? "Поверніться до застосунку Lighthouse. / Return to the Lighthouse app."
            : "Спробуйте ще раз у застосунку. / Please try again in the app.",
        string.Empty);
}