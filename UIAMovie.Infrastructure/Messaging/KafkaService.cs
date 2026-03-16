using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace UIAMovie.Infrastructure.Messaging;

public class VideoUploadConsumerService : BackgroundService
{
    private readonly KafkaConsumer _consumer;
    private readonly IServiceProvider _serviceProvider;

    public VideoUploadConsumerService(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _consumer = new KafkaConsumer(configuration, "video-upload-group");
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe("video-upload-started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var message = _consumer.Consume(stoppingToken);

                if (message == null)
                    continue;

                // Xử lý message
                await ProcessVideoUploadAsync(message.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
            }
        }

        _consumer.Dispose();
    }

    private async Task ProcessVideoUploadAsync(string messageJson)
    {
        // Giả sử message chứa movieId, videoUrl, quality
        var uploadData = JsonConvert.DeserializeObject<dynamic>(messageJson);
        
        using (var scope = _serviceProvider.CreateScope())
        {
            // var movieRepository = scope.ServiceProvider.GetRequiredService<IRepository<Movie>>();
            // Xử lý nén video, update database, etc.
        }

        await Task.Delay(1000); // Simulate processing
    }
}