namespace UIAMovie.Application.Interfaces;

public interface IEmailQueue
{
    void Enqueue(Func<IEmailService, Task> job);
}