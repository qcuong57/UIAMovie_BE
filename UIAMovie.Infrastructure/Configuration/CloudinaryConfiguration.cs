using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using UIAMovie.Application.Interfaces;

namespace UIAMovie.Infrastructure.Configuration;

public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IConfiguration configuration)
    {
        var account = new Account(
            configuration["Cloudinary:CloudName"],
            configuration["Cloudinary:ApiKey"],
            configuration["Cloudinary:ApiSecret"]
        );

        _cloudinary = new Cloudinary(account);
    }

    public async Task<string> UploadVideoAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ");

        var publicId = Guid.NewGuid().ToString();

        var uploadParams = new VideoUploadParams
        {
            File = new FileDescription(file.FileName, file.OpenReadStream()),
            Folder = folderName,
            PublicId = publicId,
            // Không cần Eager nữa — build URL transform bên dưới sẽ tự
            // trigger transcode on-demand ở request đầu tiên và cache lại.
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
            throw new Exception($"Upload lỗi: {uploadResult.Error.Message}");

        // Ép codec H.264 + AAC trong container MP4, tương thích mọi trình duyệt
        // (đặc biệt Safari iOS / Chrome Android vốn rất kén codec).
        var deliveryUrl = _cloudinary.Api.UrlVideoUp
            .Secure(true)
            .ResourceType("video")
            .Transform(new Transformation()
                .Quality("auto")
                .FetchFormat("mp4")
                .VideoCodec("h264")
                .AudioCodec("aac"))
            .BuildUrl($"{folderName}/{publicId}");

        return deliveryUrl;
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ");

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, file.OpenReadStream()),
            Folder = folderName,
            PublicId = Guid.NewGuid().ToString(),
            Transformation = new Transformation().Quality("auto:good").FetchFormat("auto")
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
            throw new Exception($"Upload lỗi: {uploadResult.Error.Message}");

        return uploadResult.SecureUrl.ToString();
    }

    public async Task<bool> DeleteFileAsync(string publicId, string resourceType = "video")
    {
        var type = resourceType.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? CloudinaryDotNet.Actions.ResourceType.Image
            : CloudinaryDotNet.Actions.ResourceType.Video;

        var deleteParams = new DeletionParams(publicId)
        {
            ResourceType = type
        };

        var result = await _cloudinary.DestroyAsync(deleteParams);
        return result.Result == "ok";
    }

    public Task<string> GenerateUrl(string publicId)
    {
        var url = _cloudinary.Api.UrlVideoUp
            .Secure(true)
            .Transform(new Transformation().Quality("auto"))
            .BuildUrl(publicId);

        return Task.FromResult(url);
    }
}