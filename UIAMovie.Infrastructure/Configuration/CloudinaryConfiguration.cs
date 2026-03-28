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

        var uploadParams = new VideoUploadParams
        {
            File       = new FileDescription(file.FileName, file.OpenReadStream()),
            Folder     = folderName,
            PublicId   = Guid.NewGuid().ToString(),
            EagerTransforms = new List<Transformation>
            {
                new Transformation().Quality("auto:eco").FetchFormat("mp4")
            },
            EagerAsync = true,
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
            throw new Exception($"Upload lỗi: {uploadResult.Error.Message}");

        return uploadResult.SecureUrl.ToString();
    }

    public async Task<bool> DeleteFileAsync(string publicId)
    {
        var deleteParams = new DeletionParams(publicId)
        {
            ResourceType = CloudinaryDotNet.Actions.ResourceType.Video
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