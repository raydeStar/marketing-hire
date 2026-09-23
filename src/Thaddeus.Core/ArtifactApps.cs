using System.Text.Json;

namespace Thaddeus.Core;

public record AppField(string Key, string Label, string Kind, string? Unit = null, string[]? Options = null);
public record AppPage(string Html, string Css, string JavaScript);
public record AppDefinition(string Title, string Description, AppField[] Fields, string[] Summaries, string? DateField = null, AppPage? Page = null);
public record AppEntry(string Id, Dictionary<string, JsonElement> Values);
public record ArtifactApp(string Id, AppDefinition Definition, AppEntry[] Entries, string Version,
    DateTimeOffset Created, DateTimeOffset Updated, bool Archived = false);
public record AppSummary(string Id, string Title, string Description, string Version, int EntryCount, bool Archived);
public record AppEdit(string OperationId, string Version, AppDefinition? Definition = null,
    AppEntry[]? Upserts = null, string[]? DeleteIds = null, bool? Archived = null);
public record AppRevision(string Id, string ArtifactId, string RequestDigest, string Description, string Source,
    DateTimeOffset At, ArtifactApp Snapshot);
public record AppRestore(string OperationId, string Version, string TargetVersion);
public record ArtifactCreationPlan(string Summary, string[] Features, string Interaction);
public record ArtifactChatContext(AppSummary[] Apps, ArtifactApp? Selected, int TotalEntries, string LocalDate,
    bool Continuing = false, ArtifactCreationPlan? CreationPlan = null, MyPageSetting? MyPage = null);
public record ArtifactResult(string Id, string Version, string Description, bool Changed, bool Deleted = false);
