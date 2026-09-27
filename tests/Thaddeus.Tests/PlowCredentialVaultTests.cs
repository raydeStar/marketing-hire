using Thaddeus.Host;

namespace Thaddeus.Tests;

public sealed class PlowCredentialVaultTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "claw-plow-vault-" + Guid.NewGuid().ToString("N"));
    private readonly string scope = new('a', 32), id = new('b', 32), other = new('c', 32);

    [Fact]
    public async Task SavedSecret_IsEncrypted_Reloads_AndCanBeForgotten()
    {
        var vault = new PlowCredentialVault(root);
        const string secret = "fictional-secret-Owner’s-café";
        await vault.Execute("write", scope, id, secret, default);
        var stored = await File.ReadAllBytesAsync(Path.Combine(root, scope + "-" + id));
        Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(stored));
        Assert.Equal(secret, await new PlowCredentialVault(root).Execute("read", scope, id, null, default));
        await vault.Execute("forget", scope, id, null, default);
        Assert.Null(await vault.Execute("read", scope, id, null, default));
    }

    [Fact]
    public async Task TamperingOrCopyingBetweenCredentialIds_FailsWithoutReplacingTheVolumeKey()
    {
        var vault = new PlowCredentialVault(root);
        await vault.Execute("write", scope, id, "fictional-key", default);
        var original = await File.ReadAllBytesAsync(Path.Combine(root, "key"));
        File.Copy(Path.Combine(root, scope + "-" + id), Path.Combine(root, scope + "-" + other));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Execute("read", scope, other, null, default));
        var data = await File.ReadAllBytesAsync(Path.Combine(root, scope + "-" + id)); data[^1] ^= 1;
        await File.WriteAllBytesAsync(Path.Combine(root, scope + "-" + id), data);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.Execute("read", scope, id, null, default));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(root, "key")));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
