using Thaddeus.Infrastructure;

namespace Thaddeus.Host;

public static class UploadEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/uploads", async (HttpContext context, Store store) =>
        {
            var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if(limit is {IsReadOnly:false})limit.MaxRequestBodySize=Store.MaxUploadBytes+65536;
            if(!context.Request.HasFormContentType)throw new ArgumentException("Choose a file to upload.");
            var form=await context.Request.ReadFormAsync(context.RequestAborted);
            if(form.Files.Count!=1)throw new ArgumentException("Upload one file at a time.");
            var file=form.Files[0];
            if(file.Length is 0 or > Store.MaxUploadBytes)throw new ArgumentException("Files can be up to 2 MiB.");
            using var content=new MemoryStream();await file.CopyToAsync(content,context.RequestAborted);
            return Results.Ok(store.AddUpload(file.FileName,content.ToArray()));
        });
        app.MapPut("/api/uploads/{id}",(string id, UploadEdit edit, Store store)=>store.EditUpload(id,edit));
        app.MapGet("/api/uploads/{id}/content",(string id,HttpContext context,Store store)=>
        {
            var file=store.Upload(id);if(file==null||file.Archived)return Results.NotFound();
            context.Response.Headers.CacheControl="no-store";
            context.Response.Headers["X-Content-Type-Options"]="nosniff";
            context.Response.Headers.ContentSecurityPolicy="default-src 'none'; sandbox";
            return context.Request.Query.ContainsKey("download")?Results.File(store.UploadContent(id),file.MediaType,file.Name):Results.File(store.UploadContent(id),file.MediaType);
        });
    }
}
