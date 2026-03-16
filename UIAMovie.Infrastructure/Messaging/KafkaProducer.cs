using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace UIAMovie.Infrastructure.Messaging;

public interface IKafkaProducer
{
    Task<DeliveryResult<string, string>> PublishEventAsync(string topic, string key, object message);
}

public class KafkaProducer : IKafkaProducer
{
    private readonly IProducer<string, string> _producer;

    public KafkaProducer(IConfiguration configuration)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            CompressionType = CompressionType.Snappy
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => Console.WriteLine($"Kafka Error: {e.Reason}"))
            .Build();
    }

    public async Task<DeliveryResult<string, string>> PublishEventAsync(string topic, string key, object message)
    {
        var messageJson = JsonConvert.SerializeObject(message);
        
        return await _producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = messageJson
        });
    }
}

public class KafkaConsumer
{
    private readonly IConsumer<string, string> _consumer;

    public KafkaConsumer(IConfiguration configuration, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        _consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => Console.WriteLine($"Kafka Error: {e.Reason}"))
            .Build();
    }

    public void Subscribe(string topic)
    {
        _consumer.Subscribe(topic);
    }

    public ConsumeResult<string, string>? Consume(CancellationToken cancellationToken)
    {
        return _consumer.Consume(cancellationToken);
    }

    public void Dispose()
    {
        _consumer?.Dispose();
    }
}