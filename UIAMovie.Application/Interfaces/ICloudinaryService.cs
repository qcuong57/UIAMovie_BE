using Microsoft.AspNetCore.Http;

namespace UIAMovie.Application.Interfaces;

public interface ICloudinaryService
{
    Task<string> UploadVideoAsync(IFormFile file, string folderName);
    Task<bool> DeleteFileAsync(string publicId);
    Task<string> GenerateUrl(string publicId);
}
