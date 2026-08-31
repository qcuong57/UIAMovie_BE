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
        var cloudName = configuration["Cloudinary:CloudName"];
        var apiKey    = configuration["Cloudinary:ApiKey"];
        var apiSecret = configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrWhiteSpace(cloudName) ||
            string.IsNullOrWhiteSpace(apiKey) ||
            string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException(
                "Cấu hình Cloudinary (CloudName/ApiKey/ApiSecret) đang thiếu hoặc để trống. " +
                "Kiểm tra lại appsettings.json hoặc biến môi trường Cloudinary__CloudName / " +
                "Cloudinary__ApiKey / Cloudinary__ApiSecret.");
        }

        var account = new Account(cloudName, apiKey, apiSecret);

        _cloudinary = new Cloudinary(account);

        // Mặc định HttpClient nội bộ của CloudinaryDotNet timeout sau 100s (mặc định .NET
        // HttpClient.Timeout). Với video lớn, upload lên Cloudinary tốn hơn 100s → request
        // bị hủy phía mình dù Cloudinary vẫn xử lý xong ở phía họ (log: TaskCanceledException
        // "HttpClient.Timeout of 100 seconds elapsing"). Nâng lên 10 phút, khớp với timeout
        // FE đang set khi gọi upload video (uploadVideo/uploadTrailerVideo/uploadEpisodeVideo).
        _cloudinary.Api.Timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds;
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
            // Ép transcode NGAY lúc upload sang H.264/AAC/mp4 để mọi trình duyệt
            // (kể cả Safari/mobile) đều phát được, bất kể codec file gốc là gì
            // (VD: HEVC từ iPhone). Đặt vào Transformation (không phải EagerTransforms)
            // để nó chạy đồng bộ ngay, và uploadResult.SecureUrl trả về sẽ là link
            // của bản ĐÃ transcode chứ không phải file gốc.
            Transformation = new Transformation()
                .Quality("auto:eco")
                .FetchFormat("mp4")
                .VideoCodec("h264")
                .AudioCodec("aac"),
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams);

        if (uploadResult.Error != null)
            throw new Exception($"Upload lỗi: {uploadResult.Error.Message}");

        return uploadResult.SecureUrl.ToString();
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File không hợp lệ");

        var uploadParams = new ImageUploadParams
        {
            File     = new FileDescription(file.FileName, file.OpenReadStream()),
            Folder   = folderName,
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