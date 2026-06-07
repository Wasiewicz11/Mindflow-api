using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Mindflow.Api.Data;
using Mindflow.Api.Exceptions;
using Mindflow.Api.Models.Dtos;

namespace Mindflow.Api.Services;

public class UserService(
    MindflowDbContext db, 
    IStorageService storageService) : IUserService
{
    private const long MaxAvatarBytes = 25 * 1024 * 1024;
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/png",
        "image/jpeg"
    ];
    private static readonly HashSet<string> AllowedExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg"
    ];

    public async Task<UserDto> GetByIdAsync(Guid userId)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new NotFoundException("User not found.");

        return await MapToDtoAsync(user);
    }

    public async Task<UserDto> UploadAvatarAsync(Guid userId, IFormFile? file)
    {
        var user = await db.Users.FindAsync(userId)
            ?? throw new NotFoundException("User not found.");

        ValidateAvatarFile(file);
        var avatarFile = file!;

        await using var memoryStream = new MemoryStream();
        await avatarFile.CopyToAsync(memoryStream);

        var contentType = DetectContentType(memoryStream)
            ?? throw new BadRequestException("Avatar must be a valid PNG or JPEG image.");

        var key = $"avatars/{userId}";
        user.AvatarUrl = await storageService.UploadAsync(memoryStream, key, contentType);
        await db.SaveChangesAsync();

        return await MapToDtoAsync(user);
    }

    private async Task<UserDto> MapToDtoAsync(Models.User user)
    {
        string? avatarUrl = null;
        if (user.AvatarUrl is not null)
        {
            avatarUrl = await storageService.GetPresignedUrlAsync(user.AvatarUrl);
        }

        return new UserDto(user.Id, user.Email, user.FirstName, user.LastName, avatarUrl, user.TimeZone);
    }

    private static void ValidateAvatarFile(IFormFile? file)
    {
        if (file is null)
        {
            throw new BadRequestException("Avatar file is required.");
        }

        if (file.Length == 0)
        {
            throw new BadRequestException("Avatar file is required.");
        }

        if (file.Length > MaxAvatarBytes)
        {
            throw new BadRequestException("Avatar cannot be larger than 25 MB.");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            throw new BadRequestException("Avatar must be a PNG or JPEG image.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension.ToLowerInvariant()))
        {
            throw new BadRequestException("Avatar file must use the .png, .jpg or .jpeg extension.");
        }
    }

    private static string? DetectContentType(MemoryStream stream)
    {
        var bytes = stream.ToArray();

        var isPng = bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == 0x50
            && bytes[2] == 0x4E
            && bytes[3] == 0x47
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A;

        if (isPng)
        {
            return "image/png";
        }

        var isJpeg = bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF;

        return isJpeg ? "image/jpeg" : null;
    }
}
