using Microsoft.Data.Sqlite;

namespace Thaddeus.Host;

public sealed partial class MarketingBackend
{
    private const string NativeOwnerIdentity = "owner@cockpit.local";
    private const int NativeMemberSlots = 16;

    private static string? NativeIdentity(SqliteConnection db, DeviceSession actor)
    {
        if (actor.Owner) return NativeOwnerIdentity;
        using var command = db.CreateCommand();
        command.CommandText = "SELECT identity FROM native_device_bindings WHERE device_id=$device";
        command.Parameters.AddWithValue("$device", actor.Id);
        return command.ExecuteScalar() as string;
    }

    private static string BindNativeDevice(SqliteConnection db, SqliteTransaction transaction,
        string deviceId, string ownerId)
    {
        using (var existing = db.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT identity FROM native_device_bindings WHERE device_id=$device";
            existing.Parameters.AddWithValue("$device", deviceId);
            if (existing.ExecuteScalar() is string identity) return identity;
        }
        for (var slot = 1; slot <= NativeMemberSlots; slot++)
        {
            var identity = $"member-{slot:00}@cockpit.local";
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO native_device_bindings" +
                "(device_id,identity,assigned_by,created_at) VALUES($device,$identity,$owner,$time)";
            insert.Parameters.AddWithValue("$device", deviceId);
            insert.Parameters.AddWithValue("$identity", identity);
            insert.Parameters.AddWithValue("$owner", ownerId);
            insert.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            if (insert.ExecuteNonQuery() == 1) return identity;
        }
        throw new InvalidOperationException("All 16 native identity slots are assigned. Add slots to the private shared Gateway before granting another device.");
    }

    private static bool ConfirmNativeProfile(SqliteConnection db, string deviceId, string profileId)
    {
        if (!Guid.TryParse(profileId, out _)) return false;
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE native_device_bindings SET gateway_profile=$profile " +
            "WHERE device_id=$device AND (gateway_profile IS NULL OR gateway_profile=$profile)";
        command.Parameters.AddWithValue("$device", deviceId);
        command.Parameters.AddWithValue("$profile", profileId);
        try { return command.ExecuteNonQuery() == 1; }
        catch (SqliteException) { return false; }
    }
}
