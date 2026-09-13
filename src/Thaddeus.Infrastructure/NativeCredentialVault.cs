using System.Runtime.InteropServices;
using System.Text;

namespace Thaddeus.Infrastructure;

/// <summary>Native calls run only in the short-lived credential helper, never in the agent VM.</summary>
public static class NativeCredentialVault
{
    public static string Name => OperatingSystem.IsWindows() ? "Windows Credential Manager" : OperatingSystem.IsMacOS() ? "macOS Keychain" : "Linux Secret Service";
    public static string? Execute(string operation, string scope, string id, string? value)
    {
        if (!Identifier(scope) || !Identifier(id) || operation is not ("read" or "write" or "forget")) throw new ArgumentException("Invalid credential operation.");
        if (operation == "write" && (value == null || Encoding.UTF8.GetByteCount(value) > 2500 || value.Contains('\0'))) throw new ArgumentException("Invalid credential value.");
        var service = "dev.thaddeus.2." + scope;
        if (OperatingSystem.IsWindows()) return Windows(operation, service + "/" + id, value);
        if (OperatingSystem.IsMacOS()) return Mac(operation, service, id, value);
        if (OperatingSystem.IsLinux()) return Linux(operation, service, id, value);
        throw new PlatformNotSupportedException();
    }
    public static bool Identifier(string? value) => value is { Length: 32 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? Windows(string operation, string target, string? value)
    {
        if (operation == "write")
        {
            var bytes = Encoding.UTF8.GetBytes(value!); var pointer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                var credential = new WindowsCredential { Type = 1, TargetName = target, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pointer, Persist = 2, UserName = "Thaddeus" };
                if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Credential store refused the write.");
            }
            finally { Array.Clear(bytes); Marshal.Copy(bytes, 0, pointer, bytes.Length); Marshal.FreeHGlobal(pointer); }
            return null;
        }
        if (operation == "forget")
        {
            if (!CredDelete(target, 1, 0) && Marshal.GetLastPInvokeError() != 1168) throw new InvalidOperationException("Credential store refused removal.");
            return null;
        }
        if (!CredRead(target, 1, 0, out var result))
        {
            if (Marshal.GetLastPInvokeError() == 1168) return null;
            throw new InvalidOperationException("Credential store refused the read.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<WindowsCredential>(result);
            if (credential.CredentialBlobSize > 2500) throw new InvalidOperationException("Unexpected stored credential.");
            var bytes = new byte[credential.CredentialBlobSize];
            try { Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }
        finally { CredFree(result); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowsCredential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref WindowsCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint credential);

    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static string? Mac(string operation, string service, string id, string? value)
    {
        var security = NativeLibrary.Load(Security); var foundation = NativeLibrary.Load(Foundation);
        nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(name.StartsWith("kSec", StringComparison.Ordinal) ? security : foundation, name));
        nint Dictionary() => CFDictionaryCreateMutable(0, 0, NativeLibrary.GetExport(foundation, "kCFTypeDictionaryKeyCallBacks"), NativeLibrary.GetExport(foundation, "kCFTypeDictionaryValueCallBacks"));
        void SetText(nint dictionary, string key, string text)
        {
            var item = CFStringCreateWithCString(0, text, 0x08000100);
            try { CFDictionarySetValue(dictionary, Constant(key), item); } finally { CFRelease(item); }
        }
        var query = Dictionary();
        try
        {
            CFDictionarySetValue(query, Constant("kSecClass"), Constant("kSecClassGenericPassword"));
            SetText(query, "kSecAttrService", service); SetText(query, "kSecAttrAccount", id);
            CFDictionarySetValue(query, Constant("kSecAttrSynchronizable"), Constant("kCFBooleanFalse"));
            int status;
            if (operation == "forget") status = SecItemDelete(query);
            else if (operation == "read")
            {
                CFDictionarySetValue(query, Constant("kSecReturnData"), Constant("kCFBooleanTrue"));
                status = SecItemCopyMatching(query, out var result);
                if (status == -25300) return null;
                if (status != 0) throw new InvalidOperationException("Keychain refused the read.");
                try
                {
                    var length = CFDataGetLength(result);
                    if (length is < 0 or > 2500) throw new InvalidOperationException("Unexpected stored credential.");
                    var bytes = new byte[(int)length];
                    try { Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes); }
                    finally { Array.Clear(bytes); }
                }
                finally { CFRelease(result); }
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(value!); var blob = CFDataCreate(0, bytes, bytes.Length);
                var updates = Dictionary();
                try
                {
                    CFDictionarySetValue(updates, Constant("kSecValueData"), blob);
                    status = SecItemUpdate(query, updates);
                    if (status == -25300)
                    {
                        CFDictionarySetValue(query, Constant("kSecValueData"), blob);
                        status = SecItemAdd(query, 0);
                    }
                }
                finally { Array.Clear(bytes); CFRelease(blob); CFRelease(updates); }
            }
            if (status != 0 && !(operation == "forget" && status == -25300)) throw new InvalidOperationException("Keychain refused the operation.");
            return null;
        }
        finally { CFRelease(query); NativeLibrary.Free(security); NativeLibrary.Free(foundation); }
    }
    [DllImport(Foundation)] private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);
    [DllImport(Foundation)] private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);
    [DllImport(Foundation)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(Foundation)] private static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(Foundation)] private static extern nint CFDataGetLength(nint data);
    [DllImport(Foundation)] private static extern nint CFDataGetBytePtr(nint data);
    [DllImport(Foundation)] private static extern void CFRelease(nint value);
    [DllImport(Security)] private static extern int SecItemCopyMatching(nint query, out nint result);
    [DllImport(Security)] private static extern int SecItemUpdate(nint query, nint attributes);
    [DllImport(Security)] private static extern int SecItemAdd(nint attributes, nint result);
    [DllImport(Security)] private static extern int SecItemDelete(nint query);

    private static string? Linux(string operation, string service, string id, string? value)
    {
        var schema = secret_schema_new("dev.thaddeus.2.credentials", 0, "service", 0, "account", 0, 0);
        if (schema == 0) throw new InvalidOperationException("Secret Service schema is unavailable.");
        try
        {
            nint error; nint result = 0; var success = true;
            if (operation == "write") success = secret_password_store_sync(schema, 0, "Thaddeus provider", value!, 0, out error, "service", service, "account", id, 0) != 0;
            else if (operation == "forget") _ = secret_password_clear_sync(schema, 0, out error, "service", service, "account", id, 0);
            else result = secret_password_lookup_sync(schema, 0, out error, "service", service, "account", id, 0);
            try
            {
                if (error != 0) { g_error_free(error); throw new InvalidOperationException("Secret Service is unavailable or locked."); }
                if (!success) throw new InvalidOperationException("Secret Service refused the write.");
                if (result == 0) return null;
                // Bound the native string before copying it into managed memory.
                var length = 0; while (length <= 2500 && Marshal.ReadByte(result, length) != 0) length++;
                if (length > 2500) throw new InvalidOperationException("Unexpected stored credential.");
                return Marshal.PtrToStringUTF8(result, length);
            }
            finally { if (result != 0) secret_password_free(result); }
        }
        finally { secret_schema_unref(schema); }
    }
    private const string Secret = "libsecret-1.so.0";
    [DllImport(Secret, CharSet = CharSet.Ansi)] private static extern nint secret_schema_new(string name, int flags, string a, int at, string b, int bt, nint end);
    [DllImport(Secret)] private static extern void secret_schema_unref(nint schema);
    [DllImport(Secret, CharSet = CharSet.Ansi)] private static extern int secret_password_store_sync(nint schema, nint collection, string label, string password, nint cancel, out nint error, string a, string av, string b, string bv, nint end);
    [DllImport(Secret, CharSet = CharSet.Ansi)] private static extern int secret_password_clear_sync(nint schema, nint cancel, out nint error, string a, string av, string b, string bv, nint end);
    [DllImport(Secret, CharSet = CharSet.Ansi)] private static extern nint secret_password_lookup_sync(nint schema, nint cancel, out nint error, string a, string av, string b, string bv, nint end);
    [DllImport(Secret)] private static extern void secret_password_free(nint value);
    [DllImport("libglib-2.0.so.0")] private static extern void g_error_free(nint error);
}
