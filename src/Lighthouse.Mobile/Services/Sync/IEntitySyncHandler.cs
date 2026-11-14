using Lighthouse.Mobile.Data;

namespace Lighthouse.Mobile.Services.Sync;

/// <summary>Синхронізація одного типу об'єктів (завдання, категорії, ...).</summary>
public interface IEntitySyncHandler
{
    string EntityType { get; }

    /// <summary>Порядок завантаження дельти: залежності (категорії) йдуть раніше за залежні об'єкти (завдання).</summary>
    int PullOrder { get; }

    /// <summary>Надсилає операцію на сервер і оновлює локальний запис; кидає виняток при помилці.</summary>
    Task PushAsync(OutboxItem item, CancellationToken ct);

    /// <summary>Завантажує зміни за курсором і застосовує їх; повертає кількість змінених локальних записів.</summary>
    Task<int> PullAsync(CancellationToken ct);

    /// <summary>Позначає запис як відхилений сервером (повторне надсилання безглузде).</summary>
    Task MarkFailedAsync(OutboxItem item);
}