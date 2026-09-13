namespace Thaddeus.Core;

public record MemorySource(string Path, string Version, string Quote);
public record MemorySelection(string Id, string Version);
public record RememberedEntry(string Id, string Version, string Statement, MemorySource? Source, DateTimeOffset Updated, bool Forgotten = false);
public record MemoryView(RememberedEntry Entry, string SourceStatus);
public record MemoryChange(string Id, string MemoryId, string Kind, string Version, DateTimeOffset Recorded);
public record RememberRequest(string Statement, MemorySource Source, string Version);
