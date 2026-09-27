using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Thaddeus.Host;

public record StoryScene(string Text, string Sub, double Seconds, string Narration, string Visual, string? Audio, string Look, string? Shot = null);
public record Storyboard(string Title, string Format, string Caption, string Channel, StoryScene[] Scenes)
{
    public double Seconds => Scenes.Sum(scene => scene.Seconds);
}

/// <summary>Short marketing videos from a storyboard: each scene is a branded card with its words, timed to the scene
/// (or to the owner's recorded narration for it), rendered on this machine with ffmpeg. No model sees or makes the pixels.</summary>
public sealed partial class VideoRenderer(IConfiguration configuration, ILogger<VideoRenderer> logger)
{
    public const int MaxScenes = 10;
    public const double MaxSeconds = 90;
    static readonly Dictionary<string, (int Width, int Height)> Sizes = new() { ["vertical"] = (1080, 1920), ["landscape"] = (1920, 1080), ["square"] = (1080, 1080) };
    // The HireZero palette: night ink, paper, lime and mint.
    static readonly Dictionary<string, (string Back, string Text, string Sub, string Accent)> Looks = new()
    {
        ["dark"] = ("0x17231c", "0xfffdf7", "0x8fd6b0", "0xd4f06b"),
        ["light"] = ("0xf6f4ec", "0x17231c", "0x3f5a14", "0x17231c"),
        ["accent"] = ("0xd4f06b", "0x17231c", "0x17231c", "0x17231c"),
    };
    bool? available;

    string Ffmpeg => configuration["Marketing:Ffmpeg"] is { Length: > 0 } path ? path : "ffmpeg";

    /// <summary>Whether ffmpeg runs here (checked once). Without it a storyboard is still saved, just not rendered.</summary>
    public bool Available()
    {
        if (available is { } known) return known;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Ffmpeg, "-hide_banner -version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
            if (process == null) return (available = false).Value;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit(10000);
            available = process.HasExited && process.ExitCode == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { available = false; }
        if (available == false) logger.LogInformation("ffmpeg isn't available; storyboards are saved without a rendered video.");
        return available.Value;
    }

    /// <summary>A storyboard from a model's answer: one JSON object, or the fenced JSON block in a storyboard document.
    /// Too-long words are refused rather than cut, so what the owner approves is what was written.</summary>
    public static Storyboard Parse(string text, string fallbackTitle)
    {
        var json = text.Trim();
        if (!json.StartsWith('{') && FencedJson().Match(json) is { Success: true } fenced) json = fenced.Groups[1].Value;
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { throw new InvalidOperationException("The video storyboard wasn't valid JSON."); }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("scenes", out var scenes) || scenes.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("A video storyboard needs its scenes.");
        var format = Text(root, "format", 12, "format").ToLowerInvariant() is var given && Sizes.ContainsKey(given) ? given : "vertical";
        var list = new List<StoryScene>();
        foreach (var scene in scenes.EnumerateArray())
        {
            if (scene.ValueKind != JsonValueKind.Object) continue;
            var words = Text(scene, "text", 120, "A scene's on-screen text");
            if (words.Length == 0) continue;
            if (words.Length > 90) throw new InvalidOperationException("Keep each scene's on-screen text to 90 characters so it can be read at a glance.");
            var sub = Text(scene, "sub", 160, "A scene's second line");
            if (sub.Length > 140) throw new InvalidOperationException("Keep each scene's second line to 140 characters.");
            var seconds = scene.TryGetProperty("seconds", out var value) && value.TryGetDouble(out var number) && double.IsFinite(number) ? Math.Clamp(number, 2, 15) : 3.5;
            var look = Text(scene, "look", 12, "look").ToLowerInvariant() is var tone && Looks.ContainsKey(tone) ? tone : "dark";
            var audio = Text(scene, "audio", 40, "audio") is { Length: 32 } id && id.All(Uri.IsHexDigit) ? id : null;
            var shot = Text(scene, "shot", 500, "A scene's page") is { Length: > 0 } address && Uri.TryCreate(address, UriKind.Absolute, out var target) && target.Scheme == Uri.UriSchemeHttps ? target.AbsoluteUri : null;
            list.Add(new StoryScene(words, sub, Math.Round(seconds, 1), Text(scene, "narration", 400, "A scene's narration"), Text(scene, "visual", 300, "A scene's visual note"), audio, look, shot));
        }
        if (list.Count < 3) throw new InvalidOperationException("A video needs at least three scenes.");
        if (list.Count > MaxScenes) throw new InvalidOperationException($"A video can have up to {MaxScenes} scenes.");
        // All one look reads as one long card: the call to action at the end gets the accent.
        if (list.All(scene => scene.Look == "dark")) list[^1] = list[^1] with { Look = "accent" };
        // A channel is one place: "YouTube or the site" means YouTube.
        var channel = System.Text.RegularExpressions.Regex.Split(Text(root, "channel", 80, "The channel"), @"\s+(?:or|and|/)\s+|,\s*")[0].Trim();
        var board = new Storyboard(Text(root, "title", 200, "title") is { Length: > 0 and <= 160 } title ? title : fallbackTitle,
            format, Text(root, "caption", 3000, "The caption"), channel.Length > 40 ? channel[..40] : channel, [.. list]);
        if (board.Seconds > MaxSeconds) throw new InvalidOperationException($"Keep the video to {MaxSeconds:0} seconds or less.");
        return board;
    }

    static string Text(JsonElement item, string name, int limit, string field)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = Regex.Replace(value.GetString()!.Replace("\r", ""), @"[\u0000-\u0008\u000B-\u001F]", "").Trim();
        if (text.Length > limit) throw new InvalidOperationException($"{field} is too long.");
        return text;
    }

    /// <summary>The storyboard as the document the owner reads: the script as a table, then the JSON block that the narration
    /// recorder fills in with the owner's clips and that re-rendering reads.</summary>
    public static string Document(Storyboard board, string? note = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"_A {board.Format} video, {board.Seconds:0.#} seconds, {board.Scenes.Length} scenes{(board.Channel.Length > 0 ? " for " + board.Channel : "")}. {note ?? "Record your own narration from this page if you want a voiceover, then render it again."}_");
        text.AppendLine();
        if (board.Caption.Length > 0) { text.AppendLine("## Caption to post with it"); text.AppendLine(); text.AppendLine(board.Caption); text.AppendLine(); }
        text.AppendLine("## Script");
        text.AppendLine();
        foreach (var (scene, index) in board.Scenes.Select((scene, index) => (scene, index + 1)))
        {
            text.AppendLine($"{index}. **{Cell(scene.Text)}**{(scene.Sub.Length > 0 ? " — " + Cell(scene.Sub) : "")} · {scene.Seconds:0.#} s");
            if (scene.Narration.Length > 0) text.AppendLine($"   - Voiceover: {Cell(scene.Narration)}");
            if (scene.Visual.Length > 0) text.AppendLine($"   - Visual: {Cell(scene.Visual)}");
            if (scene.Shot != null) text.AppendLine($"   - Shows: {scene.Shot}");
        }
        text.AppendLine();
        text.AppendLine("## Storyboard");
        text.AppendLine();
        text.AppendLine("```json");
        text.AppendLine(Json(board));
        text.AppendLine("```");
        return text.ToString();
    }

    static string Cell(string value) => value.Replace("\n", " ").Replace("*", "\\*");

    public static string Json(Storyboard board)
    {
        var scenes = new JsonArray();
        foreach (var scene in board.Scenes)
        {
            var node = new JsonObject { ["text"] = scene.Text, ["sub"] = scene.Sub, ["seconds"] = scene.Seconds, ["narration"] = scene.Narration, ["visual"] = scene.Visual, ["look"] = scene.Look };
            if (scene.Shot != null) node["shot"] = scene.Shot;
            if (scene.Audio != null) node["audio"] = scene.Audio;
            scenes.Add(node);
        }
        var root = new JsonObject { ["renderer"] = "cards", ["title"] = board.Title, ["format"] = board.Format, ["channel"] = board.Channel, ["caption"] = board.Caption, ["scenes"] = scenes };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Render to MP4 (H.264 and AAC, silent where there's no narration). media maps a scene's clip id to its WAV bytes, and
    /// "shot:N" to a PNG screenshot shown in scene N. mark is the small line in the top corner, such as the owner's site.</summary>
    public async Task<byte[]> Render(Storyboard board, IReadOnlyDictionary<string, byte[]> audio, string mark, CancellationToken cancellation)
    {
        if (!Available()) throw new InvalidOperationException("Rendering a video needs ffmpeg on this machine (set Marketing:Ffmpeg to its path).");
        var (width, height) = Sizes[board.Format];
        var work = Directory.CreateTempSubdirectory("hz-video-");
        try
        {
            CopyFonts(work.FullName);
            var margin = (int)(width * 0.074);
            File.WriteAllText(Path.Combine(work.FullName, "mark.txt"), mark, new UTF8Encoding(false));
            var list = new StringBuilder();
            for (var index = 0; index < board.Scenes.Length; index++)
            {
                var scene = board.Scenes[index];
                var look = Looks[scene.Look];
                var seconds = scene.Seconds;
                var voice = scene.Audio != null && audio.TryGetValue(scene.Audio, out var wav) ? wav : null;
                if (voice != null)
                {
                    File.WriteAllBytes(Path.Combine(work.FullName, $"voice{index}.wav"), voice);
                    if (WavSeconds(voice) is { } spoken) seconds = Math.Max(seconds, Math.Min(15, spoken + 0.4));
                }
                // A screenshot takes the lower part (vertical and square) or the right half (landscape); the words keep the rest.
                var shot = audio.TryGetValue($"shot:{index}", out var png) ? png : null;
                if (shot != null) File.WriteAllBytes(Path.Combine(work.FullName, $"shot{index}.png"), shot);
                var textWidth = shot != null && board.Format == "landscape" ? (int)(width * 0.42) : width - 2 * margin;
                var size = shot != null ? Math.Min(HeadlineSize(scene.Text, textWidth, board.Format), board.Format == "landscape" ? 80 : 88) : HeadlineSize(scene.Text, textWidth, board.Format);
                File.WriteAllText(Path.Combine(work.FullName, $"text{index}.txt"), Wrap(scene.Text, textWidth, size, 0.56), new UTF8Encoding(false));
                var subSize = (int)(size * 0.5);
                File.WriteAllText(Path.Combine(work.FullName, $"sub{index}.txt"), Wrap(scene.Sub, textWidth, subSize, 0.52), new UTF8Encoding(false));
                // The headline and its second line are laid out as one block, centred a little above the middle.
                var headLines = Wrap(scene.Text, textWidth, size, 0.56).Split('\n').Length;
                var subLines = scene.Sub.Length > 0 ? Wrap(scene.Sub, textWidth, subSize, 0.52).Split('\n').Length : 0;
                var headHeight = headLines * size * 1.2 + (headLines - 1) * (size / 6);
                var subHeight = subLines == 0 ? 0 : size * 0.45 + subLines * subSize * 1.25 + (subLines - 1) * (subSize / 4);
                // With a screenshot below, the words sit in the upper part of the frame.
                var textArea = shot != null && board.Format != "landscape" ? height * 0.45 : height;
                var top = (int)Math.Max(margin * 2.2, (textArea - headHeight - subHeight) / 2 - (shot != null && board.Format != "landscape" ? -margin : height * 0.03));
                var subTop = (int)(top + headHeight + size * 0.45);
                var at = seconds.ToString("0.##", CultureInfo.InvariantCulture);
                var total = board.Seconds.ToString("0.##", CultureInfo.InvariantCulture);
                var before = board.Scenes.Take(index).Sum(item => item.Seconds).ToString("0.##", CultureInfo.InvariantCulture);
                // The progress bar runs across the whole video, so each scene starts where the last one ended.
                var filter = $"[0:v]drawtext=fontfile=bold.ttf:textfile=mark.txt:expansion=none:fontsize={Math.Min(width, height) / 26}:fontcolor={look.Accent}:x={margin}:y={margin + height / 60}," +
                    $"drawtext=fontfile=bold.ttf:textfile=text{index}.txt:expansion=none:fontsize={size}:line_spacing={size / 6}:fontcolor={look.Text}:x={margin}:y={top}:alpha='min(1,t/0.35)'" +
                    (scene.Sub.Length > 0 ? $",drawtext=fontfile=regular.ttf:textfile=sub{index}.txt:expansion=none:fontsize={subSize}:line_spacing={subSize / 4}:fontcolor={look.Sub}:x={margin}:y={subTop}:alpha='min(1,max(0,(t-0.3)/0.4))'" : "") +
                    (shot != null
                        ? $"[base];[3:v]scale={(board.Format == "landscape" ? (int)(width * 0.5) : width - 2 * margin)}:-2,pad=iw+12:ih+12:6:6:color={look.Accent}[shot];" +
                          $"[base][shot]overlay=x={(board.Format == "landscape" ? "W-w-" + margin : "(W-w)/2")}:y={(board.Format == "landscape" ? "(H-h)/2" : $"H-h-{margin + height / 30}")}:format=auto[framed];[framed]"
                        : "[base];[base]") +
                    $"[2:v]overlay=x='-w+W*({before}+t)/{total}':y=H-h:eval=frame,format=yuv420p[v]";
                var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"color=c={look.Back}:s={width}x{height}:d={at}:r=30" };
                if (voice != null) arguments.AddRange(["-i", $"voice{index}.wav"]);
                else arguments.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo"]);
                arguments.AddRange(["-f", "lavfi", "-i", $"color=c={look.Accent}:s={width}x{Math.Max(8, height / 110)}:d={at}:r=30"]);
                if (shot != null) arguments.AddRange(["-loop", "1", "-t", at, "-i", $"shot{index}.png"]);
                arguments.AddRange(["-filter_complex", filter + $";[1:a]aformat=sample_rates=48000:channel_layouts=stereo,apad[a]", "-map", "[v]", "-map", "[a]", "-t", at,
                    "-c:v", "libx264", "-preset", "veryfast", "-tune", "stillimage", "-crf", "22", "-r", "30", "-c:a", "aac", "-b:a", "128k", "-ar", "48000", $"scene{index}.mp4"]);
                await Run(work.FullName, arguments, cancellation);
                list.AppendLine($"file 'scene{index}.mp4'");
            }
            File.WriteAllText(Path.Combine(work.FullName, "scenes.txt"), list.ToString());
            await Run(work.FullName, ["-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", "scenes.txt", "-c", "copy", "-movflags", "+faststart", "video.mp4"], cancellation);
            return File.ReadAllBytes(Path.Combine(work.FullName, "video.mp4"));
        }
        finally
        {
            try { work.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The size a network shows a post image at: a link-preview card for most, portrait for Instagram, wide for Bluesky and Mastodon.</summary>
    public static (int Width, int Height) ImageSize(string channel) => channel.Trim().ToLowerInvariant() switch
    {
        "instagram" => (1080, 1350),
        "bluesky" or "mastodon" => (1600, 900),
        _ => (1200, 627)
    };

    /// <summary>A branded post image (PNG): one card with the words, the owner's site in the corner, in the same looks as the videos.</summary>
    public async Task<byte[]> Card(string text, string sub, string look, int width, int height, string mark, CancellationToken cancellation)
    {
        if (!Available()) throw new InvalidOperationException("Making an image needs ffmpeg on this machine (set Marketing:Ffmpeg to its path).");
        if (text.Trim().Length is 0 or > 90) throw new InvalidOperationException("An image's words run to 90 characters.");
        var colors = Looks.TryGetValue(look, out var chosen) ? chosen : Looks["dark"];
        var work = Directory.CreateTempSubdirectory("hz-card-");
        try
        {
            CopyFonts(work.FullName);
            var margin = (int)(Math.Min(width, height) * 0.09);
            var size = Math.Min(width, height) / 9;
            while (size > 36 && Wrap(text.Trim(), width - 2 * margin, size, 0.56).Split('\n').Length > 4) size -= 6;
            var subSize = (int)(size * 0.48);
            File.WriteAllText(Path.Combine(work.FullName, "mark.txt"), mark, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work.FullName, "text.txt"), Wrap(text.Trim(), width - 2 * margin, size, 0.56), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(work.FullName, "sub.txt"), Wrap(sub.Trim(), width - 2 * margin, subSize, 0.52), new UTF8Encoding(false));
            var lines = Wrap(text.Trim(), width - 2 * margin, size, 0.56).Split('\n').Length;
            var headHeight = lines * size * 1.2 + (lines - 1) * (size / 6);
            var subHeight = sub.Trim().Length > 0 ? size * 0.45 + Wrap(sub.Trim(), width - 2 * margin, subSize, 0.52).Split('\n').Length * subSize * 1.25 : 0;
            var top = (int)Math.Max(margin * 1.8, (height - headHeight - subHeight) / 2);
            var filter = $"drawtext=fontfile=bold.ttf:textfile=mark.txt:expansion=none:fontsize={Math.Min(width, height) / 22}:fontcolor={colors.Accent}:x={margin}:y={margin}," +
                $"drawtext=fontfile=bold.ttf:textfile=text.txt:expansion=none:fontsize={size}:line_spacing={size / 6}:fontcolor={colors.Text}:x={margin}:y={top}" +
                (sub.Trim().Length > 0 ? $",drawtext=fontfile=regular.ttf:textfile=sub.txt:expansion=none:fontsize={subSize}:line_spacing={subSize / 4}:fontcolor={colors.Sub}:x={margin}:y={(int)(top + headHeight + size * 0.45)}" : "") +
                $",drawbox=x=0:y=ih-{Math.Max(6, height / 90)}:w=iw:h={Math.Max(6, height / 90)}:color={colors.Accent}:t=fill";
            await Run(work.FullName, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"color=c={colors.Back}:s={width}x{height}", "-vf", filter, "-frames:v", "1", "card.png"], cancellation);
            return File.ReadAllBytes(Path.Combine(work.FullName, "card.png"));
        }
        finally
        {
            try { work.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    async Task Run(string folder, IEnumerable<string> arguments, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(Ffmpeg) { WorkingDirectory = folder, RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg didn't start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        _ = process.StandardOutput.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw new InvalidOperationException("Rendering the video took too long."); }
        if (process.ExitCode != 0)
        {
            var message = (await errors).Trim();
            throw new InvalidOperationException("ffmpeg couldn't render the video: " + (message.Length > 300 ? message[..300] : message));
        }
    }

    /// <summary>A bold and a regular font, copied beside the job so ffmpeg's filter never needs a drive path.</summary>
    void CopyFonts(string folder)
    {
        string? Find(params string[] candidates) => candidates.FirstOrDefault(File.Exists);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var bold = configuration["Marketing:VideoFontBold"] is { Length: > 0 } b && File.Exists(b) ? b : Find(Path.Combine(windows, "segoeuib.ttf"), Path.Combine(windows, "arialbd.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf", "/System/Library/Fonts/Supplemental/Arial Bold.ttf");
        var regular = configuration["Marketing:VideoFont"] is { Length: > 0 } r && File.Exists(r) ? r : Find(Path.Combine(windows, "segoeui.ttf"), Path.Combine(windows, "arial.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf") ?? bold;
        if (bold == null || regular == null) throw new InvalidOperationException("No font was found for the video (set Marketing:VideoFontBold and Marketing:VideoFont).");
        File.Copy(bold, Path.Combine(folder, "bold.ttf"), true);
        File.Copy(regular, Path.Combine(folder, "regular.ttf"), true);
    }

    /// <summary>Big when the words are few; smaller so a long line still fits in about five lines.</summary>
    static int HeadlineSize(string text, int width, string format)
    {
        var size = format == "landscape" ? 104 : 112;
        while (size > 56 && Wrap(text, width, size, 0.56).Split('\n').Length > (format == "landscape" ? 4 : 6)) size -= 8;
        return size;
    }

    /// <summary>Word wrap by estimated glyph width; a word too long for a line gets a line of its own.</summary>
    public static string Wrap(string text, int width, int size, double glyph)
    {
        var perLine = Math.Max(6, (int)(width / (size * glyph)));
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > perLine) { lines.Add(line); line = word; }
                else line = line.Length == 0 ? word : line + " " + word;
            }
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    /// <summary>Length of a PCM WAV clip, from its header; null when it isn't one this can read.</summary>
    public static double? WavSeconds(byte[] wav)
    {
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") return null;
        var byteRate = 0; var position = 12;
        while (position + 8 <= wav.Length)
        {
            var id = Encoding.ASCII.GetString(wav, position, 4); var length = BitConverter.ToInt32(wav, position + 4);
            if (id == "fmt " && position + 16 <= wav.Length) byteRate = BitConverter.ToInt32(wav, position + 16);
            if (id == "data") return byteRate > 0 ? Math.Min(length, wav.Length - position - 8) / (double)byteRate : null;
            if (length < 0) return null;
            position += 8 + length + (length % 2);
        }
        return null;
    }

    [GeneratedRegex("```(?:json)?\\s*\\n?([\\s\\S]*?)```")]
    private static partial Regex FencedJson();
}
