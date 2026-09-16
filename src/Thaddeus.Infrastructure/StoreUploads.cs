using System.Text;
using Thaddeus.Core;

namespace Thaddeus.Infrastructure;

public record UploadFile(string Id, string Name, string MediaType, long Bytes, string Sha256, DateTimeOffset Created, string Version, bool Archived = false);
public record UploadEdit(string Version, bool Archived);

public sealed partial class Store
{
    public const int MaxUploadBytes = 2 * 1024 * 1024;
    public UploadFile[] Uploads() { lock (gate) return Query("SELECT body FROM uploads ORDER BY rowid DESC").Select(Wire.Unpack<UploadFile>).ToArray(); }
    public UploadFile? Upload(string id) { lock (gate) return Query("SELECT body FROM uploads WHERE id=$id", ("$id", id)).Select(Wire.Unpack<UploadFile>).SingleOrDefault(); }
    public string UploadCursor() => Setting("upload-revision") ?? "absent";
    public byte[] UploadContent(string id)
    {
        lock (gate)
        {
            using var command = db.CreateCommand(); command.CommandText = "SELECT content FROM uploads WHERE id=$id"; command.Parameters.AddWithValue("$id", id);
            return command.ExecuteScalar() as byte[] ?? throw new ArgumentException("File not found.");
        }
    }
    public UploadFile AddUpload(string name, byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxUploadBytes) throw new ArgumentException("Choose a file between 1 byte and 2 MiB.");
        name = Path.GetFileName(name.Replace('\\', '/')).Trim();
        if (name.Length is 0 or > 160 || name.Any(char.IsControl)) throw new ArgumentException("Use a file name of up to 160 characters.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var media = extension switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".txt" or ".md" or ".csv" or ".json" => "text/plain", _ => throw new ArgumentException("Supported files: TXT, Markdown, CSV, JSON, PNG, JPEG, WebP. PDF, video and audio support is deferred.") };
        if (media == "image/png" && !bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}) ||
            media == "image/jpeg" && !bytes.AsSpan().StartsWith(new byte[]{255,216,255}) ||
            media == "image/webp" && (bytes.Length < 12 || Encoding.ASCII.GetString(bytes,0,4) != "RIFF" || Encoding.ASCII.GetString(bytes,8,4) != "WEBP"))
            throw new ArgumentException("The image contents do not match its file type.");
        if (media == "text/plain")
        {
            string text; try { text = new UTF8Encoding(false,true).GetString(bytes); } catch (DecoderFallbackException) { throw new ArgumentException("Text files must use UTF-8 encoding."); }
            if (text.Length > 60000 || text.Contains('\0')) throw new ArgumentException("Text uploads support up to 60,000 characters without binary content.");
        }
        lock (gate)
        {
            var existing = Uploads();
            if (existing.Length >= 100 || existing.Sum(f => f.Bytes) + bytes.Length > 64 * 1024 * 1024) throw new InvalidOperationException("This study's upload allowance is full (100 files or 64 MiB, including Trash).");
            var file = new UploadFile(Guid.NewGuid().ToString("N"),name,media,bytes.Length,Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)),DateTimeOffset.UtcNow,Guid.NewGuid().ToString("N"));
            using var transaction = db.BeginTransaction();
            Exec("INSERT INTO uploads(id,body,content) VALUES($id,$body,$content)",("$id",file.Id),("$body",Wire.Pack(file)),("$content",bytes));
            Setting("upload-revision",file.Version); transaction.Commit(); return file;
        }
    }
    public UploadFile EditUpload(string id, UploadEdit edit)
    {
        lock (gate)
        {
            var file=Upload(id)??throw new ArgumentException("File not found.");
            if(file.Version!=edit.Version)throw new InvalidOperationException("This file changed. Refresh before editing.");
            file=file with {Archived=edit.Archived,Version=Guid.NewGuid().ToString("N")};
            using var transaction=db.BeginTransaction();
            Exec("UPDATE uploads SET body=$body WHERE id=$id",("$id",id),("$body",Wire.Pack(file)));Setting("upload-revision",file.Version);transaction.Commit();return file;
        }
    }
    public ModelAttachment[] Attachments(string[] ids, bool admitted = false)
    {
        if(ids.Length>4||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Attach up to four distinct files.");
        lock(gate)
        {
            var files=ids.Select(id=>Upload(id)??throw new ArgumentException("An attached file is missing.")).ToArray();
            if(!admitted&&files.Any(f=>f.Archived))throw new ArgumentException("Restore a file from Trash before attaching it.");
            if(files.Sum(f=>f.Bytes)>4*1024*1024)throw new ArgumentException("Keep this message's attachments within 4 MiB.");
            return files.Select(f=>new ModelAttachment(f.Id,f.Name,f.MediaType,f.MediaType=="text/plain"?Encoding.UTF8.GetString(UploadContent(f.Id)):Convert.ToBase64String(UploadContent(f.Id)))).ToArray();
        }
    }
}
