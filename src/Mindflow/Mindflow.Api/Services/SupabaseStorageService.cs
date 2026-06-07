using Amazon.S3;
using Amazon.S3.Model;

namespace Mindflow.Api.Services;

public class SupabaseStorageService(
    IAmazonS3 s3,
    IHttpClientFactory httpClientFactory,
    IConfiguration config) : IStorageService
{
    private readonly string _bucket = config["SupabaseStorage:BucketName"]
        ?? throw new InvalidOperationException("SupabaseStorage:BucketName is not configured.");

    public async Task<string> UploadFromUrlAsync(string sourceUrl, string key)
    {
        var http = httpClientFactory.CreateClient();
        var imageBytes = await http.GetByteArrayAsync(sourceUrl);

        using var stream = new MemoryStream(imageBytes);
        return await UploadAsync(stream, key, "image/jpeg");
    }

    public async Task<string> UploadAsync(Stream stream, string key, string contentType)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        });

        return key;
    }

    public async Task<string> GetPresignedUrlAsync(string key, int expiresInSeconds = 3600)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = key,
            Expires = DateTime.UtcNow.AddSeconds(expiresInSeconds)
        };

        return await s3.GetPreSignedURLAsync(request);
    }
}
