using System.Text.Json;
using Microsoft.Data.Sqlite;
using Thaddeus.Core;
using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class StudyMaintenance
{
    public static async Task<int> Run(string[] args)
    {
        try
        {
            if (args.Length != 3 || args[0] is not ("--study-backup" or "--study-restore")) throw new ArgumentException("Use --study-backup DATA_DIRECTORY NEW_BACKUP_DIRECTORY or --study-restore BACKUP_DIRECTORY NEW_DATA_DIRECTORY. Paths must be absolute; stop the source host first.");
            var receipt = args[0] == "--study-backup" ? await StudyBackup.Create(args[1], args[2]) : await StudyBackup.Restore(args[1], args[2]);
            Console.WriteLine(Wire.Pack(receipt));
            Console.Error.WriteLine("Study copy verified. The original remains exactly where you left it."); return 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or SqliteException)
        {
            Console.Error.WriteLine(error is SqliteException ? "The study database could not be verified. No completed copy was created." : error.Message);
            return 1;
        }
    }
}
