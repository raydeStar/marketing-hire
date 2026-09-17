using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    public const string SoulFileName = "SOUL.md";
    public const int MaxSoulCharacters = 20_000;
    public string SoulPath => Path.Combine(Root, SoulFileName);

    public SoulDocument Soul()
    {
        lock (gate)
        {
            EnsureSoul();
            var content = File.ReadAllText(SoulPath);
            ValidateSoul(content);
            return new(SoulFileName, content, Wire.Hash(content), File.GetLastWriteTimeUtc(SoulPath));
        }
    }

    public IReadOnlyList<Page> SoulHistory()
    {
        lock (gate)
            return Query("SELECT body FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20", ("$p", SoulFileName))
                .Select(Wire.Unpack<Page>).ToArray();
    }

    public SoulDocument UpdateSoul(string content, string expectedVersion, string source, string operationId)
    {
        lock (gate)
        {
            ValidateSoul(content);
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 200) throw new ArgumentException("Soul update identity is invalid.");
            if (source is not ("settings" or "chat")) throw new ArgumentException("Soul update source is invalid.");
            var operationKey = "soul-operation:" + operationId;
            if (Setting(operationKey) is { } previous)
            {
                var saved = Wire.Unpack<Page>(previous);
                var current = Soul();
                if (saved.Content != content) throw new InvalidOperationException("This Soul update identity was already used for different content.");
                if (current.Version != saved.Version) throw new InvalidOperationException("The Soul was updated again after this operation completed.");
                return current;
            }
            var before = Soul();
            if (before.Version != expectedVersion) throw new InvalidOperationException("The Soul changed after it was opened. Refresh it before saving.");
            if (before.Content == content) return before;

            var temporary = SoulPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            AssertNoLinks(SoulPath);
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                {
                    writer.Write(content); writer.Flush(); output.Flush(flushToDisk: true);
                }
                testFault?.Invoke("before-soul-replace");
                File.Move(temporary, SoulPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }

            var page = new Page(SoulFileName, content, Wire.Hash(content), DateTimeOffset.UtcNow);
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO revisions(path,body) VALUES($p,$b)", ("$p", SoulFileName), ("$b", Wire.Pack(page)));
            Setting(operationKey, Wire.Pack(page));
            Exec("DELETE FROM revisions WHERE path=$p AND id NOT IN (SELECT id FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20)", ("$p", SoulFileName));
            transaction.Commit();
            return Soul();
        }
    }

    internal void EnsureSoul()
    {
        AssertNoLinks(SoulPath);
        if (File.Exists(SoulPath)) return;
        using var output = new FileStream(SoulPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        writer.Write(PersonalityProfile.Thaddeus.Instructions);
    }

    private static void ValidateSoul(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("The Soul cannot be empty.");
        if (content.Length > MaxSoulCharacters) throw new ArgumentException($"The Soul must be {MaxSoulCharacters:N0} characters or fewer.");
        if (content.IndexOf('\0') >= 0) throw new ArgumentException("The Soul contains an unsupported character.");
    }
}
