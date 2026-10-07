using System.Text.RegularExpressions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using UIAMovie.Application.Interfaces;
using UIAMovie.Application.Interfaces.IServices;

namespace UIAMovie.Infrastructure.Configuration;

public class CloudinaryService : ICloudinaryService
{
    private const string DefaultStreamingProfile = "auto";

    // Streaming profile chỉ gồm chữ thường, số, gạch dưới → chặn chèn ký tự lạ vào URL.
    private static readonly Regex ProfileRegex = new("^[a-z0-9_]+$", RegexOptions.Compiled);
    private static readonly Regex VersionSegmentRegex = new(@"^v\d+$", RegexOptions.Compiled);

    private readonly Cloudinary _cloudinary;
    private readonly string _defaultStreamingProfile;

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

        // Profile HLS mặc định (tùy chọn): Cloudinary:StreamingProfile = "auto" | "hd" | "full_hd" ...
        var configuredProfile = NormalizeProfile(configuration["Cloudinary:StreamingProfile"]);
        _defaultStreamingProfile = configuredProfile ?? DefaultStreamingProfile;
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

    // ── HLS ──────────────────────────────────────────────────────────────────

    public string GenerateHlsPlaybackUrl(string publicId, string? streamingProfile = null)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            throw new ArgumentException("publicId không hợp lệ", nameof(publicId));

        // Người gọi truyền profile nhưng sai định dạng → báo lỗi rõ ràng thay vì âm thầm dùng mặc định.
        var profile = _defaultStreamingProfile;
        if (!string.IsNullOrWhiteSpace(streamingProfile))
        {
            profile = NormalizeProfile(streamingProfile)
                ?? throw new ArgumentException(
                    $"Streaming profile '{streamingProfile}' không hợp lệ", nameof(streamingProfile));
        }

        // => https://res.cloudinary.com/<cloud>/video/upload/sp_<profile>/<publicId>.m3u8
        // Lần đầu request, Cloudinary có thể mất vài giây để sinh manifest/segment (lazy transcode).
        // ApiSecret không bao giờ nằm trong URL này.
        return _cloudinary.Api.UrlVideoUp
            .Secure(true)
            .Transform(new Transformation().StreamingProfile(profile))
            .Format("m3u8")
            .BuildUrl(publicId.Trim('/'));
    }

    public string? ExtractPublicId(string? cloudinaryUrl)
    {
        if (string.IsNullOrWhiteSpace(cloudinaryUrl) ||
            !Uri.TryCreate(cloudinaryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.EndsWith("cloudinary.com", StringComparison.OrdinalIgnoreCase))
            return null;

        // Path dạng: /<cloud>/video/upload/[transformations/][v123/]<folder>/<id>.<ext>
        const string marker = "/upload/";
        var path = uri.AbsolutePath;
        var idx  = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var segments = path[(idx + marker.Length)..]
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (segments.Count == 0) return null;

        // Bỏ mọi thứ đứng trước segment version (v1234567890) — đó là các transformation.
        var versionIdx = segments.FindIndex(s => VersionSegmentRegex.IsMatch(s));
        if (versionIdx >= 0)
        {
            segments = segments.Skip(versionIdx + 1).ToList();
        }
        else
        {
            // Không có version: chỉ bỏ các segment chắc chắn là transformation (chứa dấu phẩy,
            // vd "q_auto,f_mp4"). Không đoán theo tiền tố "xx_" để tránh nhầm với tên folder.
            segments = segments.Where(s => !s.Contains(',')).ToList();
        }

        if (segments.Count == 0) return null;

        // Bỏ đuôi file ở segment cuối.
        var last = Uri.UnescapeDataString(segments[^1]);
        var dot  = last.LastIndexOf('.');
        if (dot > 0) last = last[..dot];

        segments[^1] = last;
        var publicId = string.Join('/', segments.Take(segments.Count - 1).Select(Uri.UnescapeDataString).Append(last));

        return string.IsNullOrWhiteSpace(publicId) ? null : publicId;
    }

    /// <summary>"SP_Auto" / "sp_auto" / "auto" → "auto". Sai định dạng hoặc rỗng → null.</summary>
    private static string? NormalizeProfile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var p = raw.Trim().ToLowerInvariant();
        if (p.StartsWith("sp_")) p = p[3..];

        return ProfileRegex.IsMatch(p) ? p : null;
    }
}