using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.Interfaces.IServices;

/// <summary>Kết quả upload video bảo vệ. DeliveryType: "authenticated" | "upload".</summary>
public record ProtectedVideoUploadResult(string PublicId, string SecureUrl, string DeliveryType);

public interface ICloudinaryService
{
    /// <summary>
    /// Upload video CÔNG KHAI (type=upload) — dùng cho trailer. Trả về secure URL.
    /// Video phim/tập chính phải dùng <see cref="UploadProtectedVideoAsync"/>.
    /// </summary>
    Task<string> UploadVideoAsync(IFormFile file, string folderName);

    /// <summary>
    /// Upload video phim/tập CHÍNH với type=authenticated: URL không có chữ ký sẽ bị Cloudinary từ chối.
    /// Pre-generate HLS (streaming profile) ở nền. PublicId trả về đã gồm folder.
    /// </summary>
    Task<ProtectedVideoUploadResult> UploadProtectedVideoAsync(IFormFile file, string folderName);

    /// <summary>Upload ảnh (vd: brand image, poster, avatar) lên Cloudinary.</summary>
    Task<string> UploadImageAsync(IFormFile file, string folderName);

    /// <summary>
    /// Xóa file trên Cloudinary. resourceType: "video" (default) hoặc "image".
    /// deliveryType: "upload" (default) hoặc "authenticated" — phải khớp lúc upload, nếu không
    /// Cloudinary sẽ không tìm thấy file để xóa. Dùng <see cref="GetDeliveryType"/> để suy ra từ URL đã lưu.
    /// </summary>
    Task<bool> DeleteFileAsync(string publicId, string resourceType = "video", string deliveryType = "upload");

    Task<string> GenerateUrl(string publicId);

    /// <summary>
    /// Dựng link HLS master playlist: https://res.cloudinary.com/{cloud}/video/upload/sp_{profile}/{publicId}.m3u8
    /// streamingProfile: "auto", "hd", "full_hd", ... (có thể truyền kèm tiền tố "sp_"). NULL = dùng profile mặc định
    /// trong cấu hình (Cloudinary:StreamingProfile), nếu không có thì "auto".
    /// Throw ArgumentException nếu publicId rỗng hoặc profile chứa ký tự không hợp lệ.
    /// </summary>
    string GenerateHlsPlaybackUrl(string publicId, string? streamingProfile = null, bool authenticated = false);

    /// <summary>
    /// Dựng link HLS ĐÃ KÝ cho video type=authenticated:
    /// .../video/authenticated/s--&lt;sig&gt;--/sp_{profile}/{publicId}.m3u8. Chữ ký tạo bằng ApiSecret (không lộ trong URL).
    /// Chỉ gọi sau khi đã kiểm tra quyền xem.
    /// </summary>
    string GenerateSignedHlsPlaybackUrl(string publicId, string? streamingProfile = null);

    /// <summary>"authenticated" nếu URL Cloudinary dạng /authenticated/, ngược lại "upload".</summary>
    string GetDeliveryType(string? cloudinaryUrl);

    /// <summary>
    /// Trích public ID (gồm folder, không đuôi file) từ URL Cloudinary đã lưu (/upload/ hoặc /authenticated/,
    /// bỏ qua chữ ký s--...-- và transformation). NULL nếu không phải URL Cloudinary (vd: Youtube, URL ngoài).
    /// </summary>
    string? ExtractPublicId(string? cloudinaryUrl);
}