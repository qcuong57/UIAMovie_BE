using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.Interfaces.IServices;

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

    /// <summary>
    /// Dựng link HLS master playlist: https://res.cloudinary.com/{cloud}/video/upload/sp_{profile}/{publicId}.m3u8
    /// streamingProfile: "auto", "hd", "full_hd", ... (có thể truyền kèm tiền tố "sp_"). NULL = dùng profile mặc định
    /// trong cấu hình (Cloudinary:StreamingProfile), nếu không có thì "auto".
    /// Throw ArgumentException nếu publicId rỗng hoặc profile chứa ký tự không hợp lệ.
    /// </summary>
    string GenerateHlsPlaybackUrl(string publicId, string? streamingProfile = null);

    /// <summary>
    /// Trích public ID (gồm folder, không đuôi file) từ URL Cloudinary đã lưu.
    /// Trả về NULL nếu không phải URL Cloudinary dạng /upload/ (vd: Youtube, URL ngoài).
    /// </summary>
    string? ExtractPublicId(string? cloudinaryUrl);
}