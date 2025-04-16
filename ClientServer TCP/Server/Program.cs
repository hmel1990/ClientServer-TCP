using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
namespace ClientServerTCP
{
    class Server
    {
        private const int DEFAULT_BUFLEN = 512;
        private const int DEFAULT_PORT = 27015;
        private static ConcurrentQueue<(TcpClient, byte[])> messageQueue = new ConcurrentQueue<(TcpClient, byte[])>();
        private static TcpListener? listener;
        private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private static ConcurrentDictionary<int, (TcpClient client, string ip, int port)> clients = new ConcurrentDictionary<int, (TcpClient, string, int)>();
        private static int clientCounter = 0;
        //===================================================================
        private string response;

        private static readonly string logFilePath = "server_log.txt";

        private static readonly object fileLock = new object();

        private static readonly ConcurrentQueue<string> logQueue = new();
        private static readonly CancellationTokenSource logCts = new();
        private static readonly Task logTask = Task.Run(() => ProcessLogQueue());
        //===================================================================

        private static void AddToQueue(string message)
        {
            string time = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            logQueue.Enqueue($"{time} {message}");
        }

        public static async Task LogUserConnected(int clientId, string clientIP, int clientPort)
        {
            AddToQueue($"Клиент №{clientId} IP {clientIP} Port {clientPort} подключился.");
        }

        public static async Task LogUserMessage(int clientId, string message)
        {
            AddToQueue($"Клиент №{clientId} запросил {message}");
        }

        public static async Task LogUserDisconnected(int clientId)
        {
            AddToQueue($"Клиент №{clientId} отключился.");
        }


        private static async Task ProcessLogQueue()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (logQueue.TryDequeue(out string logEntry))
                    {
                        lock (fileLock)
                        {
                            File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
                        }
                    }
                    else
                    {
                        await Task.Delay(50, logCts.Token); // ждём немного, если очередь пуста
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при записи лога: {ex.Message}");
                }
            }
        }
        //===================================================================


        static async Task Main()
        {
            string processName = Process.GetCurrentProcess().ProcessName;
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length > 1)
            {
                Console.WriteLine("Сервер уже запущен.");
                return;
            }

            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "SERVER SIDE";
            Console.WriteLine("Процесс сервера запущен!");

            _ = Task.Run(async () =>
            {
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    string? input = Console.ReadLine();
                    if (input?.ToLower() == "exit") // можно завершить работу сервера, если набрать exit
                    {
                        Console.WriteLine("Процесс сервера завершает работу...");
                        await StopServerAsync();
                        cancellationTokenSource.Cancel();
                        break;
                    }
                }
            }, cancellationTokenSource.Token);

            // закрытие окна консоли через Ctrl+C
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("Сервер завершает работу...");
                await StopServerAsync();
                cancellationTokenSource.Cancel();
            };

            try
            {
                listener = new TcpListener(IPAddress.Any, DEFAULT_PORT);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();
                Console.WriteLine("Пожалуйста, запустите одно или несколько клиентских приложений.");

                _ = ProcessMessages();

                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                        int clientId = Interlocked.Increment(ref clientCounter);
                        var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                        clients.TryAdd(clientId, (client, clientEndPoint.Address.ToString(), clientEndPoint.Port));
                        Console.WriteLine($"Клиент #{clientId} подключился: IP {clientEndPoint.Address}, Порт {clientEndPoint.Port}");
                        await LogUserConnected(clientId, clientEndPoint.Address.ToString(), clientEndPoint.Port); // Запись в лог-файл
                        _ = HandleClientAsync(client, clientId);
                    }
                    catch (OperationCanceledException)
                    {

                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted || ex.SocketErrorCode == SocketError.OperationAborted)
                    {

                    }
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                Console.WriteLine("Порт 27015 уже используется.");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                    Console.ReadKey();
                }
            }
            finally
            {
                await StopServerAsync();
                cancellationTokenSource.Dispose();
            }
        }

        private static async Task HandleClientAsync(TcpClient client, int clientId)
        {
            NetworkStream? stream = null;
            try
            {
                stream = client.GetStream();
                while (!cancellationTokenSource.Token.IsCancellationRequested && client.Connected)
                {
                    var buffer = new byte[DEFAULT_BUFLEN];
                    int bytesReceived = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationTokenSource.Token).ConfigureAwait(false);

                    if (bytesReceived > 0)
                    {
                        messageQueue.Enqueue((client, buffer[..bytesReceived]));
                        Console.WriteLine($"Клиент #{clientId}: Добавлено сообщение в очередь.");
                    }
                    else
                    {
                        await LogUserDisconnected(clientId); // Запись в лог-файл
                        break; // клиент отключился
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка с клиентом #{clientId}: {ex.Message}");
            }
            finally
            {
                stream?.Dispose();
                client.Close();
                clients.TryRemove(clientId, out _);
                Console.WriteLine($"Клиент #{clientId} отключился.");
            }
        }

        private static async Task StopServerAsync()
        {
            try
            {
                // отмена тасков
                cancellationTokenSource.Cancel();

                // закрываем соединение с клиентами
                foreach (var clientInfo in clients.Values)
                {
                    try
                    {
                        clientInfo.client.Close();
                        clientInfo.client.Dispose();
                        Console.WriteLine($"Клиент с IP {clientInfo.ip}:{clientInfo.port} закрыт.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при закрытии клиента {clientInfo.ip}:{clientInfo.port}: {ex.Message}");
                    }
                }
                clients.Clear();

                // останавливаем прослушку на сервере
                listener?.Stop();
                listener = null;

                Console.WriteLine("Сервер полностью остановлен.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при остановке сервера: {ex.Message}");
            }
            finally
            {
                // даём время на завершение задач
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        private static async Task ProcessMessages()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {

                    if (messageQueue.TryDequeue(out var item))
                    {
                        string response;
                        var (client, buffer) = item;
                        if (!client.Connected) continue;

                        string message = Encoding.UTF8.GetString(buffer);
                        var messageTrimmed = message.TrimEnd('\0'); // удаляем нулевые байты в конце строки
                        //!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
                        if (messageTrimmed == "USD")
                        {
                            var moneyRate = new MoneyRate();
                            await moneyRate.SetRateUSD();

                            response = $"курс Доллара на сегодня: {moneyRate.RateDollar} грн";
                            Console.WriteLine(response);
                        }
                        else if (message.Contains("EUR"))
                        {
                            var moneyRate = new MoneyRate();
                            await moneyRate.SetRateEUR();

                            response = $"курс Евро на сегодня: {moneyRate.RateEuro} грн";
                            Console.WriteLine(response);
                        }
                        else
                        {
                            response = new string(message.Reverse().ToArray());

                        }
                        var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;

                        int clientId = clients.FirstOrDefault(x => x.Value.ip == clientEndPoint.Address.ToString() && x.Value.port == clientEndPoint.Port).Key;

                        Console.WriteLine($"Клиент #{clientId} отправил сообщение: {message}"); 

                        await LogUserMessage(clientId, message); // Запись в лог-файл

                        await Task.Delay(100, cancellationTokenSource.Token).ConfigureAwait(false);

                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);

                        try
                        {
                            var stream = client.GetStream();
                            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, cancellationTokenSource.Token).ConfigureAwait(false);
                            Console.WriteLine($"Ответ клиенту #{clientId}: {response}");
                        }
                        catch
                        {
                            Console.WriteLine($"Не удалось отправить сообщение клиенту #{clientId}.");
                        }
                    }
                    await Task.Delay(15, cancellationTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка в ProcessMessages: {ex.Message}");
                }
            }
        }
    }
}