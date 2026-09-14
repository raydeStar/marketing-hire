namespace Thaddeus.Infrastructure;

internal static class StorageSpace
{
    internal const long Reserve = 10L * 1024 * 1024 * 1024;
    internal static long Available(string path)
    {
        path = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var drive = DriveInfo.GetDrives().Where(drive => path.StartsWith(Path.EndsInDirectorySeparator(drive.RootDirectory.FullName)
                ? drive.RootDirectory.FullName : drive.RootDirectory.FullName + Path.DirectorySeparatorChar, comparison))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length).FirstOrDefault();
        return drive?.AvailableFreeSpace ?? throw new IOException("Cannot determine free space for the destination.");
    }
    internal static void Require(string destination, long additionalBytes, long? available = null)
    {
        if (additionalBytes < 0 || (available ?? Available(destination)) < checked(Reserve + additionalBytes))
            throw new IOException("This copy needs room for its complete contents plus 10 GiB free. Choose a destination with more space; the original study stays intact.");
    }
}
