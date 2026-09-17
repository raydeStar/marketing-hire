using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    public const string UserFileName = "USER.md";
    public const int MaxUserCharacters = 20_000;
    public const string DefaultUserContent = """
        # User

        ## About me
        - Nothing saved yet.

        ## Preferences
        - Nothing saved yet.

        ## Current priorities
        - Nothing saved yet.
        """;
    public string UserPath => Path.Combine(Root, UserFileName);

    public UserDocument User()
    {
        lock (gate)
        {
            EnsureUser();
            var content = File.ReadAllText(UserPath);
            ValidateUser(content);
            return new(UserFileName, content, Wire.Hash(content), File.GetLastWriteTimeUtc(UserPath));
        }
    }

    public IReadOnlyList<Page> UserHistory()
    {
        lock (gate)
            return Query("SELECT body FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20", ("$p", UserFileName))
                .Select(Wire.Unpack<Page>).ToArray();
    }

    public UserDocument UpdateUser(string content, string expectedVersion, string source, string operationId)
    {
        lock (gate)
        {
            ValidateUser(content);
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 200) throw new ArgumentException("User update identity is invalid.");
            if (source is not ("settings" or "chat")) throw new ArgumentException("User update source is invalid.");
            var operationKey = "user-operation:" + operationId;
            if (Setting(operationKey) is { } previous)
            {
                var saved = Wire.Unpack<Page>(previous);
                var current = User();
                if (saved.Content != content) throw new InvalidOperationException("This User update identity was already used for different content.");
                if (current.Version != saved.Version) throw new InvalidOperationException("The User profile was updated again after this operation completed.");
                return current;
            }
            var before = User();
            if (before.Version != expectedVersion) throw new InvalidOperationException("The User profile changed after it was opened. Refresh it before saving.");
            if (before.Content == content) return before;

            var temporary = UserPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            AssertNoLinks(UserPath);
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                {
                    writer.Write(content); writer.Flush(); output.Flush(flushToDisk: true);
                }
                testFault?.Invoke("before-user-replace");
                File.Move(temporary, UserPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }

            var page = new Page(UserFileName, content, Wire.Hash(content), DateTimeOffset.UtcNow);
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO revisions(path,body) VALUES($p,$b)", ("$p", UserFileName), ("$b", Wire.Pack(page)));
            Setting(operationKey, Wire.Pack(page));
            Exec("DELETE FROM revisions WHERE path=$p AND id NOT IN (SELECT id FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20)", ("$p", UserFileName));
            transaction.Commit();
            return User();
        }
    }

    internal void EnsureUser()
    {
        AssertNoLinks(UserPath);
        if (File.Exists(UserPath)) return;
        using var output = new FileStream(UserPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        writer.Write(DefaultUserContent);
    }

    private static void ValidateUser(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("The User profile cannot be empty.");
        if (content.Length > MaxUserCharacters) throw new ArgumentException($"The User profile must be {MaxUserCharacters:N0} characters or fewer.");
        if (content.IndexOf('\0') >= 0) throw new ArgumentException("The User profile contains an unsupported character.");
    }
}
