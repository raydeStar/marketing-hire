using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

const int MaxInputCharacters = 8_192;
var json = Console.In.ReadToEnd();
if (json.Length is 0 or > MaxInputCharacters)
    return Fail("The notification request was empty or too large.");

NotificationRequest? request;
try { request = JsonSerializer.Deserialize<NotificationRequest>(json, Wire.Json); }
catch (JsonException) { return Fail("The notification request was invalid JSON."); }
if (request is null || string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Message) ||
    request.Title.Length > 200 || request.Message.Length > 2_000)
    return Fail("The notification request fields were invalid.");

var icon = Path.Combine(AppContext.BaseDirectory, "thaddeus-notification.png");
if (!File.Exists(icon)) return Fail("The packaged notification icon is missing.");

Marshal.ThrowExceptionForHR(Shell.SetCurrentProcessExplicitAppUserModelID("raydeStar.Thaddeus"));
var manager = AppNotificationManager.Default;
var registered = false;
try
{
    manager.Register("Thaddeus", new Uri(icon));
    registered = true;
    var setting = manager.Setting;
    if (setting != AppNotificationSetting.Enabled)
        return Fail("Windows notifications are not enabled for Thaddeus.", setting.ToString());

    var notification = new AppNotificationBuilder()
        .AddText(request.Title)
        .AddText(request.Message)
        .BuildNotification();
    manager.Show(notification);
    if (notification.Id == 0) return Fail("Windows did not assign a notification identifier.", setting.ToString());

    await Task.Delay(TimeSpan.FromMilliseconds(750));
    var active = await manager.GetAllAsync();
    var retained = active.Any(item => item.Id == notification.Id);
    if (!retained)
        return Fail("Windows accepted the notification but did not retain it in Notification Center.", setting.ToString());

    Console.Out.Write(JsonSerializer.Serialize(new NotificationResult(
        true, "AppNotificationManager", setting.ToString(), notification.Id, active.Count, retained), Wire.Json));
    Console.Out.Flush();
    await Task.Delay(TimeSpan.FromSeconds(2));
    return 0;
}
catch (Exception error)
{
    return Fail($"Windows rejected the app notification ({error.GetType().Name}, 0x{error.HResult:X8}).");
}
finally
{
    if (registered)
    {
        try { manager.Unregister(); }
        catch { /* Notification delivery has already been decided; cleanup cannot replay it. */ }
    }
}

static int Fail(string message, string? setting = null)
{
    Console.Error.Write(JsonSerializer.Serialize(new { accepted = false, setting, error = message }, Wire.Json));
    return 2;
}

internal sealed record NotificationRequest(string Title, string Message);
internal sealed record NotificationResult(bool Accepted, string Mechanism, string Setting, uint NotificationId,
    int ActiveCount, bool RetainedInNotificationCenter);
internal static class Shell
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
internal static class Wire
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
