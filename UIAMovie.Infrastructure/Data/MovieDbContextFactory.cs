// UIAMovie.Infrastructure/Data/MovieDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace UIAMovie.Infrastructure.Data;

public class MovieDbContextFactory : IDesignTimeDbContextFactory<MovieDbContext>
{
    public MovieDbContext CreateDbContext(string[] args)
    {
        var basePath = FindAppSettingsDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Không tìm thấy 'DefaultConnection' trong appsettings.json");

        var optionsBuilder = new DbContextOptionsBuilder<MovieDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new MovieDbContext(optionsBuilder.Options);
    }

    private static string FindAppSettingsDirectory()
    {
        // Tìm appsettings.json bằng cách đi từ thư mục hiện tại lên trên
        // và kiểm tra từng thư mục con tên "UIAMovie.API"
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current != null)
        {
            // Tìm thư mục UIAMovie.API trong thư mục hiện tại
            var apiDir = Path.Combine(current.FullName, "UIAMovie.API");
            if (File.Exists(Path.Combine(apiDir, "appsettings.json")))
                return apiDir;

            // Kiểm tra chính thư mục hiện tại
            if (File.Exists(Path.Combine(current.FullName, "appsettings.json")))
                return current.FullName;

            current = current.Parent;
        }

        throw new FileNotFoundException(
            $"Không tìm thấy appsettings.json. " +
            $"Đã tìm từ: {Directory.GetCurrentDirectory()} trở lên.");
    }
}