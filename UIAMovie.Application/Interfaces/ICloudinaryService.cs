using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.Interfaces;

public interface ICloudinaryService
{
    Task<string> UploadVideoAsync(IFormFile file, string folderName);

    /// <summary>Upload ảnh (vd: brand image, poster, avatar) lên Cloudinary.</summary>
    Task<string> UploadImageAsync(IFormFile file, string folderName);

    /// <summary>
    /// Xóa file trên Cloudinary. resourceType: "video" (default) hoặc "image".
    /// Phải truyền đúng loại resource lúc upload, nếu không Cloudinary sẽ không tìm thấy file để xóa.
    /// </summary>
    Task<bool> DeleteFileAsync(string publicId, string resourceType = "video");

    Task<string> GenerateUrl(string publicId);
}