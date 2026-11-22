using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BoxWise.Server.Tests;

/// <summary>
/// 最小 SMTP 测试服务端：只监听本机 loopback 随机端口，不接触外部网络。
/// 支持 EHLO/HELO、MAIL FROM、RCPT TO、DATA、RSET、QUIT，并可模拟收件人拒收。
/// </summary>
internal sealed class LoopbackSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(10));
    private readonly Task _serverTask;
    private readonly List<string> _dataLines = new();
    private readonly bool _rejectRecipient;

    public int Port { get; }
    public string? MailFrom { get; private set; }
    public List<string> Recipients { get; } = new();
    public string? Data => _dataLines.Count == 0
        ? null
        : string.Join("\r\n", _dataLines) + "\r\n";
    public bool QuitReceived { get; private set; }
    public Task Completion => _serverTask;

    public LoopbackSmtpServer(bool rejectRecipient = false)
    {
        _rejectRecipient = rejectRecipient;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serverTask = ServeAsync(_cts.Token);
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(ct);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            using var writer = new StreamWriter(stream, new UTF8Encoding(false))
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            await writer.WriteLineAsync("220 localhost ESMTP");

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 localhost");
                }
                else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
                {
                    MailFrom = ExtractAddress(line);
                    await writer.WriteLineAsync("250 2.1.0 Ok");
                }
                else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
                {
                    Recipients.Add(ExtractAddress(line));
                    await writer.WriteLineAsync(_rejectRecipient
                        ? "550 5.1.1 mailbox unavailable"
                        : "250 2.1.5 Ok");
                }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                    while ((line = await reader.ReadLineAsync(ct)) is not null && line != ".")
                        _dataLines.Add(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);

                    await writer.WriteLineAsync("250 2.0.0 Ok: queued");
                }
                else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    QuitReceived = true;
                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    break;
                }
                else
                {
                    // RSET / NOOP 等；失败发送路径中 MailKit 会发 RSET。
                    await writer.WriteLineAsync("250 2.0.0 Ok");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            // 客户端在失败路径中直接释放连接。
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string ExtractAddress(string line)
    {
        var value = line[(line.IndexOf(':') + 1)..].Trim();
        var start = value.IndexOf('<');
        var end = value.IndexOf('>', start + 1);
        return start >= 0 && end > start ? value[(start + 1)..end] : value;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            await _serverTask;
        }
        catch
        {
            // 测试清理阶段忽略服务端任务异常。
        }

        _cts.Dispose();
    }
}
