using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;

namespace ServerMachin
{
    public class ClientSession
    {
        public string Pseudo { get; }
        public TcpClient TcpClient { get; }
        public NetworkStream Stream => TcpClient.GetStream();
        public DateTime LastMessageTime { get; set; } = DateTime.MinValue;

        public ClientSession(string pseudo, TcpClient client)
        {
            Pseudo = pseudo;
            TcpClient = client;
        }
    } 
    

    public class Server
    {
        private readonly ConcurrentDictionary<string, ClientSession> Clients = new();
        private CancellationTokenSource? _cts;

        private void BroadcastRaw(string data)
        {
            var buffer = Encoding.UTF8.GetBytes(data + "\n");
            foreach (var client in Clients.Values)
            {
                try { client.Stream.Write(buffer, 0, buffer.Length); } catch { }
            }
        }

        private readonly BadApplePlayer badApplePlayer;

        public Server()
        {
            badApplePlayer = new BadApplePlayer(BroadcastRaw);
        }

        public async Task StartAsync(int port = 5000)
        {
            _cts = new CancellationTokenSource();
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"[{DateTime.Now:HH:mm}] Server started on port {port}");

            _ = Task.Run(ListenConsoleAsync);

            while (!_cts.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync(_cts.Token);
                _ = HandleNewClientAsync(tcpClient);
            }
        }

        private async Task HandleNewClientAsync(TcpClient tcpClient)
        {
            var stream = tcpClient.GetStream();
            var buffer = new byte[1024];

            string mode = "";
            while (mode != "login" && mode != "register")
            {
                await SendTextAsync(stream, "Use login or register :");

                int byteCount;
                try
                {
                    byteCount = await stream.ReadAsync(buffer, _cts!.Token);
                }
                catch
                {
                    tcpClient.Close();
                    return;
                }

                mode = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim().ToLower();

                if (mode != "login" && mode != "register")
                {
                    await SendTextAsync(stream, "[ERROR] Please type 'login' or 'register'.");
                }
            }

            if (mode == "register")
            {
                await SendTextAsync(stream, "Enter pseudo and password:");
                int byteCount = await stream.ReadAsync(buffer, _cts.Token);
                string regData = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim();
                string[] regParts = regData.Split(new[] { "::" }, StringSplitOptions.None);

                if (regParts.Length != 2)
                {
                    await SendTextAsync(stream, "[ERROR] Invalid Format.");
                    tcpClient.Close();
                    return;
                }

                string pseudo = regParts[0];
                string password = regParts[1];

                if (pseudo.Length > 25 || password.Length > 25 || string.IsNullOrWhiteSpace(pseudo) || string.IsNullOrWhiteSpace(password) || pseudo.Contains(' '))
                {
                    await SendTextAsync(stream, "[ERROR] Invalid pseudo or password.");
                    tcpClient.Close();
                    return;
                }

                using (var con = new MySql.Data.MySqlClient.MySqlConnection("Server=localhost;Port=3306;Database=coconitro;User ID=root;Password=admin;"))
                {
                    try
                    {
                        con.Open();
                        // Vérifier si le pseudo existe déjà
                        string checkQuery = "SELECT COUNT(*) FROM users WHERE pseudo = @pseudo;";
                        using (var checkCmd = new MySql.Data.MySqlClient.MySqlCommand(checkQuery, con))
                        {
                            checkCmd.Parameters.AddWithValue("@pseudo", pseudo);
                            long exists = (long)checkCmd.ExecuteScalar();
                            if (exists > 0)
                            {
                                await SendTextAsync(stream, "[ERROR] This pseudo already exists.");
                                tcpClient.Close();
                                return;
                            }
                        }
                        // Insérer le nouvel utilisateur
                        string insertQuery = "INSERT INTO users (pseudo, password) VALUES (@pseudo, @password);";
                        using (var insertCmd = new MySql.Data.MySqlClient.MySqlCommand(insertQuery, con))
                        {
                            insertCmd.Parameters.AddWithValue("@pseudo", pseudo);
                            insertCmd.Parameters.AddWithValue("@password", password);
                            insertCmd.ExecuteNonQuery();
                        }
                        await SendTextAsync(stream, "[OK] Inscription done. You can connect");
                        tcpClient.Close();
                        return;
                    }
                    catch (Exception ex)
                    {
                        await SendTextAsync(stream, "[ERROR] Server Error: " + ex.Message);
                        tcpClient.Close();
                        return;
                    }
                }
            }
            else if (mode == "login")
            {
                await SendTextAsync(stream, "Enter pseudo and password: ");
                int byteCount = await stream.ReadAsync(buffer, _cts.Token);
                string authData = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim();
                string[] parts = authData.Split(new[] { "::" }, StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    await SendTextAsync(stream, "[ERROR] Invalid login format.");
                    tcpClient.Close();
                    return;
                }

                string pseudo = parts[0];
                string password = parts[1];
                
                using (var con = new MySql.Data.MySqlClient.MySqlConnection("Server=localhost;Port=3306;Database=coconitro;User ID=root;Password=admin;"))
                {
                    try
                    {
                        con.Open();
                        string query = "SELECT COUNT(*) FROM users WHERE pseudo = @pseudo AND password = @password;";
                        using var cmd = new MySql.Data.MySqlClient.MySqlCommand(query, con);
                        cmd.Parameters.AddWithValue("@pseudo", pseudo);
                        cmd.Parameters.AddWithValue("@password", password);

                        long count = (long)cmd.ExecuteScalar();
                        if (count != 1)
                        {
                            await SendTextAsync(stream, "[ERROR] Invalid username or password.");
                            tcpClient.Close();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        await SendTextAsync(stream, "[ERROR] Server error: " + ex.Message);
                        tcpClient.Close();
                        return;
                    }
                }

                if (string.IsNullOrWhiteSpace(pseudo) || pseudo.Contains(' ') || Clients.ContainsKey(pseudo))
                {
                    await SendTextAsync(stream, "[ERROR] Invalid or duplicate username.");
                    tcpClient.Close();
                    return;
                }

                var session = new ClientSession(pseudo, tcpClient);
                Clients[pseudo] = session;
                Console.WriteLine($"[{DateTime.Now:HH:mm}] {pseudo} is now connected");
                await BroadcastTextAsync($"[INFO] [{DateTime.Now:HH:mm}] {pseudo} connected");

                try
                {
                    await HandleClientAsync(session);
                }
                finally
                {
                    Clients.TryRemove(pseudo, out _);
                    try { tcpClient.Client.Shutdown(SocketShutdown.Both); } catch { }
                    tcpClient.Close();
                    await BroadcastTextAsync($"[INFO] [{DateTime.Now:HH:mm}] {pseudo} disconnected");
                    Console.WriteLine($"[{DateTime.Now:HH:mm}] {pseudo} disconnected");
                }
            }
        }


        private async Task HandleClientAsync(ClientSession session)
        {
            var buffer = new byte[1024];
            while (session.TcpClient.Connected)
            {
                int byteCount;
                try
                {
                    byteCount = await session.Stream.ReadAsync(buffer, _cts!.Token);
                }
                catch { break; }

                if (byteCount == 0) break;

                string data = Encoding.UTF8.GetString(buffer, 0, byteCount).Trim();

                // Protection anti-flood
                if ((DateTime.UtcNow - session.LastMessageTime).TotalSeconds < 1)
                {
                    await SendTextAsync(session.Stream, "[ERROR] Flood detected: 1 message/sec max.");
                    continue;
                }
                session.LastMessageTime = DateTime.UtcNow;

                if (data.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (data.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    await SendTextAsync(session.Stream,
                        "[HELP] Commands: exit, whisper <pseudo> <msg>, help");
                    continue;
                }

                if (data.StartsWith("whisper "))
                {
                    var parts = data.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        string dest = parts[1];
                        string msg = parts[2];
                        if (dest == session.Pseudo)
                        {
                            await SendTextAsync(session.Stream, "[ERROR] You can't whisper to yourself.");
                        }
                        else if (Clients.TryGetValue(dest, out var destSession))
                        {
                            string whisperMsg = $"[WHISPER] [{DateTime.Now:HH:mm}] {session.Pseudo} -> {dest}: {msg}";
                            await SendTextAsync(destSession.Stream, whisperMsg);
                            await SendTextAsync(session.Stream, $"[INFO] Whisper sent to {dest}");
                            Console.WriteLine($"[WHISPER] [{DateTime.Now:HH:mm}] {session.Pseudo} -> {dest}: {msg}");
                        }
                        else
                        {
                            await SendTextAsync(session.Stream, $"[ERROR] No user '{dest}'.");
                        }
                    }
                    else
                    {
                        await SendTextAsync(session.Stream, "[ERROR] Format: whisper <pseudo> <message>");
                    }
                    continue;
                }

                // Message public (format demandé)
                string chatMsg = $"[{DateTime.Now:HH:mm}] {session.Pseudo} : {data}";
                await BroadcastTextAsync(chatMsg);
                Console.WriteLine(chatMsg);
            }
        }

        private async Task BroadcastTextAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message + "\n");
            foreach (var client in Clients.Values)
            {
                try
                {
                    await client.Stream.WriteAsync(buffer);
                }
                catch { }
            }
        }

        private async Task SendTextAsync(NetworkStream stream, string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message + "\n");
            await stream.WriteAsync(buffer);
        }

        private async Task ListenConsoleAsync()
        {
            while (true)
            {
                var cmd = Console.ReadLine();
                if (cmd == null) continue;
                if (cmd.StartsWith("kick "))
                {
                    var pseudo = cmd[5..].Trim();
                    if (Clients.TryGetValue(pseudo, out var session))
                    {
                        await SendTextAsync(session.Stream, "[INFO] You have been kicked.");
                        session.TcpClient.Close();
                        Clients.TryRemove(pseudo, out _);
                        await BroadcastTextAsync($"[INFO] [{DateTime.Now:HH:mm}] {pseudo} has been kicked");
                        Console.WriteLine($"[{DateTime.Now:HH:mm}] {pseudo} has been kicked");
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm}] No user '{pseudo}' found.");
                    }
                }
                else if (cmd.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var session in Clients.Values)
                    {
                        try { session.TcpClient.Client.Shutdown(SocketShutdown.Both); } catch { }
                        session.TcpClient.Close();
                    }
                    Clients.Clear();
                    Console.WriteLine($"[{DateTime.Now:HH:mm}] Server shutting down...");
                    Environment.Exit(0);
                }
                else if (cmd.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm}] Connected clients:");
                    foreach (var pseudo in Clients.Keys)
                        Console.WriteLine($"[{DateTime.Now:HH:mm}] - {pseudo}");
                }
                else if (cmd.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Clear();
                }
                else if (cmd.StartsWith("say "))
                {
                    var msg = cmd[4..].Trim();
                    string adminMsg = $"[ADMIN] [{DateTime.Now:HH:mm}] {msg}";
                    await BroadcastTextAsync(adminMsg);
                    Console.WriteLine($"[{DateTime.Now:HH:mm}] > Admin: {msg}");
                }
                else if (cmd.StartsWith("whisper "))
                {
                    var parts = cmd.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        var dest = parts[1];
                        var msg = parts[2];
                        if (Clients.TryGetValue(dest, out var destSession))
                        {
                            string whisperMsg = $"[WHISPER] [{DateTime.Now:HH:mm}] Admin -> {dest}: {msg}";
                            await SendTextAsync(destSession.Stream, whisperMsg);
                            Console.WriteLine($"[{DateTime.Now:HH:mm}] [Admin] whispered to {dest}: {msg}");
                        }
                        else
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm}] No user '{dest}' found.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm}] Usage: whisper <pseudo> <message>");
                    }
                }
                else if (cmd.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm}] Available commands: kick <pseudo>, exit, list, clear, say <msg>, whisper <pseudo> <msg>, badapple, resumebadapple, pausebadapple, stopbadapple");
                }
                // Commandes Bad Apple
                else if (cmd.Equals("badapple", StringComparison.OrdinalIgnoreCase))
                {
                    badApplePlayer.Start();
                }
                else if (cmd.Equals("pausebadapple", StringComparison.OrdinalIgnoreCase))
                {
                    badApplePlayer.Pause();
                }
                else if (cmd.Equals("resumebadapple", StringComparison.OrdinalIgnoreCase))
                {
                    badApplePlayer.Resume();
                }
                else if (cmd.Equals("stopbadapple", StringComparison.OrdinalIgnoreCase))
                {
                    badApplePlayer.Stop();
                }
            }
        }
    }
}
