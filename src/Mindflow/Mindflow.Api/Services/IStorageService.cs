namespace Mindflow.Api.Services;

public interface IStorageService
{
    Task<string> UploadFromUrlAsync(string sourceUrl, string key);
    Task<string> GetPresignedUrlAsync(string key, int expiresInSeconds = 3600);
}
