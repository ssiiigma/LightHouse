using System.Threading.Channels;

namespace Lighthouse.Api.Services.Email;

/// <summary>Черга листів: запит не чекає SMTP, тому час відповіді не залежить від наявності адреси в системі.</summary>
public interface IEmailQueue
{
    bool TryEnqueue(EmailMessage message);
}

public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public bool TryEnqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}