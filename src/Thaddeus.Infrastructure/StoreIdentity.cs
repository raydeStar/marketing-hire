using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public sealed partial class Store
{
    public const string IdentityFileName = "IDENTITY.md";
    public const int MaxIdentityCharacters = 20_000;
    public const string DefaultIdentityContent = """
        # Identity

        **Name:** Thaddeus

        **Role:** A private AI butler and thoughtful collaborator.

        **Purpose:** Help the user turn conversation into useful understanding, plans, and carefully reviewed action.

        **Presentation:** A small raven with subtle wizardly airs, quiet confidence, and room for playfulness.
        """;
    public string IdentityPath => Path.Combine(Root, IdentityFileName);

    public IdentityDocument Identity()
    {
        lock (gate)
        {
            EnsureIdentity();
            var content = File.ReadAllText(IdentityPath);
            ValidateIdentity(content);
            return new(IdentityFileName, content, Wire.Hash(content), File.GetLastWriteTimeUtc(IdentityPath));
        }
    }

    public IReadOnlyList<Page> IdentityHistory()
    {
        lock (gate)
            return Query("SELECT body FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20", ("$p", IdentityFileName))
                .Select(Wire.Unpack<Page>).ToArray();
    }

    public IdentityDocument UpdateIdentity(string content, string expectedVersion, string operationId)
    {
        lock (gate)
        {
            ValidateIdentity(content);
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 200) throw new ArgumentException("Identity update identity is invalid.");
            var operationKey = "identity-operation:" + operationId;
            if (Setting(operationKey) is { } previous)
            {
                var saved = Wire.Unpack<Page>(previous);
                var current = Identity();
                if (saved.Content != content) throw new InvalidOperationException("This Identity update identity was already used for different content.");
                if (current.Version != saved.Version) throw new InvalidOperationException("Identity was updated again after this operation completed.");
                return current;
            }
            var before = Identity();
            if (before.Version != expectedVersion) throw new InvalidOperationException("Identity changed after it was opened. Refresh it before saving.");
            if (before.Content == content) return before;

            var temporary = IdentityPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            AssertNoLinks(IdentityPath);
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                {
                    writer.Write(content); writer.Flush(); output.Flush(flushToDisk: true);
                }
                testFault?.Invoke("before-identity-replace");
                File.Move(temporary, IdentityPath, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }

            var page = new Page(IdentityFileName, content, Wire.Hash(content), DateTimeOffset.UtcNow);
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO revisions(path,body) VALUES($p,$b)", ("$p", IdentityFileName), ("$b", Wire.Pack(page)));
            Setting(operationKey, Wire.Pack(page));
            Exec("DELETE FROM revisions WHERE path=$p AND id NOT IN (SELECT id FROM revisions WHERE path=$p ORDER BY id DESC LIMIT 20)", ("$p", IdentityFileName));
            transaction.Commit();
            return Identity();
        }
    }

    internal void EnsureIdentity()
    {
        AssertNoLinks(IdentityPath);
        if (File.Exists(IdentityPath)) return;
        using var output = new FileStream(IdentityPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(output, new UTF8Encoding(false));
        writer.Write(DefaultIdentityContent);
    }

    private static void ValidateIdentity(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Identity cannot be empty.");
        if (content.Length > MaxIdentityCharacters) throw new ArgumentException($"Identity must be {MaxIdentityCharacters:N0} characters or fewer.");
        if (content.IndexOf('\0') >= 0) throw new ArgumentException("Identity contains an unsupported character.");
    }
}
