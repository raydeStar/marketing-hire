namespace Thaddeus.Core;

public record MyPageSetting(string Mode = "today", string? ArtifactId = null, string Version = "absent");
public record MyPageEdit(string Mode, string? ArtifactId, string Version);
