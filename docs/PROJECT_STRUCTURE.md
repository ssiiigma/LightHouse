# Структура проєкту Lighthouse

Умовні позначення: ✅ є в каркасі · ⏳ заплановано.

```
Lighthouse/
├── Lighthouse.sln                              ✅ (створює scripts/init.ps1)
├── docker-compose.yml                          ✅ SQL Server + API
├── .env.example                                ✅
├── README.md                                   ✅
├── scripts/init.ps1                            ✅
├── docs/
│
├── src/
│   ├── Lighthouse.Shared/                      ✅ спільні контракти
│   │   ├── Enums/                              ✅ TaskPriority, HabitFrequencyType, BehaviorEventType
│   │   └── Contracts/
│   │       ├── Auth/                           ✅ Register/Login/Refresh/Logout, AuthResponse, UserDto
│   │       ├── Tasks/                          ✅ TaskDto, SubtaskDto, UpsertTaskRequest
│   │       ├── Categories/                     ⏳
│   │       ├── Habits/                         ⏳
│   │       ├── Analytics/                      ⏳
│   │       └── Recommendations/                ⏳
│   │
│   ├── Lighthouse.Core/                        ✅ домен + AI (без EF/ASP.NET)
│   │   ├── Entities/                           ✅ User, RefreshToken, Category, TaskItem, Subtask, Habit,
│   │   │                                          HabitLog, NotificationSettings, BehaviorEvent,
│   │   │                                          RecommendationFeedback, BanditState
│   │   └── AI/
│   │       ├── LinUcbModel.cs                  ✅ алгоритм
│   │       ├── LinUcbSnapshot.cs               ✅ серіалізація стану
│   │       ├── TaskFeatureExtractor.cs         ⏳ вектор ознак завдання (дедлайн, пріоритет, година доби, історія)
│   │       ├── RewardCalculator.cs             ⏳ винагорода з подій (виконано/відкладено/відхилено)
│   │       └── ExplanationBuilder.cs           ⏳ текстове пояснення рекомендації (FR-38)
│   │
│   ├── Lighthouse.Api/                         ✅ ASP.NET Core Web API
│   │   ├── Program.cs                          ✅
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs               ✅ (Google-вхід ⏳)
│   │   │   ├── TasksController.cs              ✅
│   │   │   ├── CategoriesController.cs         ⏳
│   │   │   ├── HabitsController.cs             ⏳ (+ відмітки, streak)
│   │   │   ├── AnalyticsController.cs          ⏳ (+ звіти PDF/CSV)
│   │   │   ├── RecommendationsController.cs    ⏳ (ранжування, accept/reject, feedback)
│   │   │   ├── NotificationSettingsController.cs ⏳
│   │   │   └── DataController.cs               ⏳ (імпорт/експорт, резервна копія)
│   │   ├── Services/
│   │   │   ├── Auth/                           ✅ TokenService, AuthService
│   │   │   ├── Tasks/                          ✅ TaskService
│   │   │   ├── Categories/, Habits/            ⏳
│   │   │   ├── Analytics/                      ⏳ AnalyticsService, ReportService (QuestPDF, CsvHelper)
│   │   │   ├── Recommendations/                ⏳ PrioritizationService (LinUCB + бізнес-правила)
│   │   │   ├── Calendar/                       ⏳ GoogleCalendarService
│   │   │   └── Backup/                         ⏳
│   │   ├── BackgroundJobs/                     ⏳ ReminderWorker, BackupWorker, ModelTrainingWorker, ArchiveCleanupWorker
│   │   ├── Data/
│   │   │   ├── LighthouseDbContext.cs          ✅
│   │   │   └── Migrations/                     (створюється командою dotnet ef)
│   │   ├── Validators/                         ✅ Auth, Task
│   │   ├── Infrastructure/                     ✅ AppException, AppExceptionHandler, ClaimsPrincipalExtensions
│   │   ├── Options/                            ✅ JwtOptions
│   │   ├── Dockerfile                          ✅
│   │   └── requests.http                       ✅
│   │
│   └── Lighthouse.Mobile/                      ⏳ .NET MAUI (створюється з шаблону)
│       ├── MauiProgram.cs, App.xaml, AppShell.xaml
│       ├── Platforms/Android/
│       ├── Resources/
│       │   ├── Styles/                         Colors.xaml (темна/світла теми), Styles.xaml
│       │   └── Strings/                        AppResources.resx, AppResources.uk.resx
│       ├── Data/
│       │   ├── LocalDatabase.cs                SQLite-підключення
│       │   └── Entities/                       LocalTask, LocalSubtask, LocalHabit, LocalHabitLog, OutboxItem
│       ├── Services/
│       │   ├── Api/                            ApiClient, AuthHttpHandler (підстановка/оновлення JWT)
│       │   ├── Auth/                           AuthService, TokenStore (SecureStorage)
│       │   ├── Sync/                           SyncService, OutboxProcessor, ConnectivityMonitor
│       │   ├── Repositories/                   TaskRepository, HabitRepository
│       │   ├── Notifications/                  LocalReminderScheduler
│       │   └── Settings/                       ThemeService, LanguageService
│       ├── ViewModels/                         Welcome, Login, Register, Tasks, TaskEdit, Habits, Analytics, Profile
│       ├── Views/                              відповідні сторінки XAML
│       ├── Controls/                           HeatmapView, PriorityChip, StreakBadge
│       └── Converters/
│
└── tests/
    ├── Lighthouse.Core.Tests/                  ✅ тести LinUCB
    └── Lighthouse.Api.Tests/                   ⏳ інтеграційні (WebApplicationFactory + Testcontainers)
```
