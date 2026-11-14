using Lighthouse.Mobile.Infrastructure;
using SQLite;

namespace Lighthouse.Mobile.Data;

/// <summary>Локальна база SQLite (офлайн-режим): дзеркала даних сервера та черга вихідних змін.</summary>
public interface ILocalDB
{
    /// <summary>Підключення; таблиці створюються при першому зверненні.</summary>
    Task<SQLiteAsyncConnection> GetConnectionAsync();

    /// <summary>Видаляє всі локальні дані (вихід з облікового запису, зміна користувача).</summary>
    Task ClearAllAsync();
}

public sealed class LocalDB : ILocalDB
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SQLiteAsyncConnection? _connection;

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _gate.WaitAsync();
        try
        {
            if (_connection is null)
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "lighthouse.db3");
                var connection = new SQLiteAsyncConnection(path,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

                await connection.EnableWriteAheadLoggingAsync();
                await connection.CreateTableAsync<LocalCategory>();
                await connection.CreateTableAsync<LocalTask>();
                await connection.CreateTableAsync<LocalSubtask>();
                await connection.CreateTableAsync<OutboxItem>();
                await connection.CreateTableAsync<RankingCache>();
                _connection = connection;
            }

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAllAsync()
    {
        var db = await GetConnectionAsync();
        await db.RunInTransactionAsync(tx =>
        {
            tx.DeleteAll<LocalSubtask>();
            tx.DeleteAll<LocalTask>();
            tx.DeleteAll<LocalCategory>();
            tx.DeleteAll<OutboxItem>();
            tx.DeleteAll<RankingCache>();
        });
    }
}

/// <summary>Операції з чергою вихідних змін (виконуються в межах транзакції зміни даних).</summary>
internal static class OutboxStore
{
    public const int MaxAttempts = 5;
    public const string Put = "put";
    public const string Delete = "delete";

    /// <summary>Замінює всі попередні незавершені операції над об'єктом новою (зберігається лише остання).</summary>
    public static void Enqueue(
        SQLiteConnection tx, string entityType, Guid entityId, string operation, string? payloadJson)
    {
        tx.Table<OutboxItem>().Delete(i => i.EntityType == entityType && i.EntityId == entityId);
        tx.Insert(new OutboxItem
        {
            EntityType = entityType,
            EntityId = entityId,
            Operation = operation,
            PayloadJson = payloadJson,
            CreatedAtUtc = Utc.Now
        });
    }

    /// <summary>Чи є для об'єкта інша (новіша) операція, ніж <paramref name="item"/>.</summary>
    public static bool HasOtherPending(SQLiteConnection tx, OutboxItem item) =>
        tx.Table<OutboxItem>().Count(i =>
            i.EntityType == item.EntityType && i.EntityId == item.EntityId && i.Id != item.Id) > 0;
}