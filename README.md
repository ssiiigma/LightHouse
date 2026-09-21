# Lighthouse — Інтелектуальний планувальник завдань і звичок

Стек: C# / .NET 10, ASP.NET Core Web API, EF Core + SQL Server, .NET MAUI (Android), LinUCB (власна реалізація на C#).

## Структура рішення

| Проєкт | Призначення |
|---|---|
| `src/Lighthouse.Shared` | DTO, enum-и — спільні для API і мобільного клієнта |
| `src/Lighthouse.Core` | Доменні сутності та AI-алгоритм LinUCB (без залежностей від EF/ASP.NET) |
| `src/Lighthouse.Api` | REST API, EF Core (DbContext), сервіси, автентифікація JWT |
| `src/Lighthouse.Mobile` | MAUI-застосунок (створюється з шаблону Rider, див. нижче) |
| `tests/Lighthouse.Core.Tests` | Модульні тести |

## Перший запуск

1. Потрібно: .NET 10 SDK, Docker Desktop, Rider, (за бажанням) SSMS.
2. `./scripts/init.ps1` — створить `Lighthouse.sln`, підключить проєкти та встановить NuGet-пакети.
3. Скопіюйте `.env.example` у `.env` і змініть паролі.
4. `docker compose up -d db` — піднімає SQL Server (підключення з SSMS: `localhost,1433`, логін `sa`).
5. Створіть початкову міграцію:
   `dotnet ef migrations add InitialCreate --project src/Lighthouse.Api --output-dir Data/Migrations`
6. У `src/Lighthouse.Api/appsettings.Development.json` перевірте пароль `sa` у рядку підключення, а `Database:AutoMigrate` = `true` застосує міграцію при старті. Або вручну:
   `dotnet ef database update --project src/Lighthouse.Api`
7. Запустіть `Lighthouse.Api` (профіль `http`, порт 5080). Документація API: http://localhost:5080/scalar/v1
8. Тести: `dotnet test`

Емулятор Android звертається до API на комп'ютері за адресою `http://10.0.2.2:5080`.

## Мобільний проєкт

У Rider: New Solution → .NET MAUI App, назва `Lighthouse.Mobile`, розташування `src/`. Далі
`dotnet sln add src/Lighthouse.Mobile` і посилання на `Lighthouse.Shared`. Структуру папок і код додамо наступним кроком.

## Docker (повне розгортання)

`docker compose up -d --build` — SQL Server + API на http://localhost:8080 (міграції застосовуються автоматично).
