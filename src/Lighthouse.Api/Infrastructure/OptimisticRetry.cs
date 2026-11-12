using Microsoft.EntityFrameworkCore;

namespace Lighthouse.Api.Infrastructure;

/// <summary>
/// Повтор операції при конфлікті запису (зміна версії BanditState іншим запитом, порушення унікального індексу).
/// Перед повтором трекер очищається, тому <c>action</c> має сам перечитувати все потрібне з БД.
/// </summary>
public static class OptimisticRetry
{
    public static async Task<T> RunAsync<T>(DbContext db, Func<Task<T>> action, int maxAttempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (DbUpdateException) when (attempt < maxAttempts)
            {
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                throw new AppException(StatusCodes.Status409Conflict, "Concurrent update, please retry.");
            }
        }
    }
}