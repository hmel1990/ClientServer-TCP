using System.Net.Sockets;
using System.Net;
using System.Text;

class Client
{
    private const int DEFAULT_BUFLEN = 512;
    private const int DEFAULT_PORT = 27015;

    static async Task Main()
    {
        Console.Title = "CLIENT SIDE";
        string? pendingMessage = null;
        var inputCancellation = new CancellationTokenSource();

        while (true)
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, DEFAULT_PORT);
                Console.WriteLine("Подключение к серверу установлено.");
                inputCancellation = new CancellationTokenSource();

                using var stream = client.GetStream();

                var receivingTask = Task.Run(async () =>
                {
                    while (client.Connected)
                    {
                        var buffer = new byte[DEFAULT_BUFLEN];
                        int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length);

                        if (bytesReceived > 0)
                        {
                            string response = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
                            Console.WriteLine($"\nОтвет от сервера: {response}");
                        }
                        else
                        {
                            Console.WriteLine("Соединение с сервером прервано.");
                            inputCancellation.Cancel();
                            break;
                        }
                    }
                });

                if (pendingMessage != null)
                {
                    byte[] pendingBytes = Encoding.UTF8.GetBytes(pendingMessage);
                    await stream.WriteAsync(pendingBytes, 0, pendingBytes.Length);
                    Console.WriteLine($"Сохраненное сообщение отправлено: {pendingMessage}");
                    pendingMessage = null;
                }

                while (client.Connected)
                {
                    Console.Write("Введите сообщение для отправки серверу: ");
                    Console.Write("Для получения курса Доллара к гривне введите 'USD' ");
                    Console.Write("Для получения курса Евро к гривне введите 'EUR' ");


                    var readTask = Task.Run(() => Console.ReadLine(), inputCancellation.Token);
                    var completedTask = await Task.WhenAny(readTask, receivingTask);

                    if (completedTask == receivingTask)
                    {
                        // Console.WriteLine("Ожидание ввода отменено из-за отключения сервера.");
                        if (!readTask.IsCompleted)
                        {
                            inputCancellation.Cancel();
                        }
                        pendingMessage = await readTask;
                        break;
                    }

                    var message = readTask.Result;

                    if (string.IsNullOrEmpty(message))
                    {
                        break;
                    }

                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    Console.WriteLine($"Сообщение отправлено: {message}");
                }

                await receivingTask;
            }
            catch
            {
                Console.WriteLine("Сервер недоступен. Попытка подключения снова через 3 секунды...");
                await Task.Delay(3000);
            }
        }
    }
}