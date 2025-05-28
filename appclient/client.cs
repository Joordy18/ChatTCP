using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace clientMachin
{
    class AppClient
    
    {
        public static void setClient()
        {
            IPAddress ip = IPAddress.Parse("192.168.2.6");
            int port = 5000;

            TcpClient client = new TcpClient();
            try
            {
                client.Connect(ip, port);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unable to connect to server: " + ex.Message);
                return;
            }

            NetworkStream ns = client.GetStream();

            // Lire le message d'accueil du serveur (login/register)
            string serverMsg = ReadLineFromStream(ns);
            Console.Write(serverMsg);

            // Saisie du choix utilisateur
            string mode = "";
            while (mode != "login" && mode != "register")
            {
                mode = Console.ReadLine()?.Trim().ToLower() ?? "";
                byte[] modeBuffer = Encoding.UTF8.GetBytes(mode);
                ns.Write(modeBuffer, 0, modeBuffer.Length);
                if (mode != "login" && mode != "register")
                    Console.WriteLine("Veuillez taper 'login' ou 'register'.");
            }

            serverMsg = ReadLineFromStream(ns);
            Console.Write(serverMsg);

            Console.Write("Pseudo: ");
            string pseudo = Console.ReadLine();
            Console.Write("Mot de passe: ");
            string password = Console.ReadLine();
            string authData = pseudo + "::" + password;
            byte[] authBuffer = Encoding.UTF8.GetBytes(authData);
            ns.Write(authBuffer, 0, authBuffer.Length);

            serverMsg = ReadLineFromStream(ns);
            Console.Write(serverMsg);

            if (serverMsg.StartsWith("[ERROR]") || serverMsg.StartsWith("[OK]"))
            {
                ns.Close();
                client.Close();
                Console.ReadKey();
                return;
            }

            Thread thread = new Thread(() => ReceiveData(client));
            thread.Start();

            while (true)
            {
                string message = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(message)) continue;

                byte[] buffer = Encoding.UTF8.GetBytes(message);
                ns.Write(buffer, 0, buffer.Length);

                if (message.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;
            }

            client.Client.Shutdown(SocketShutdown.Send);
            thread.Join();
            ns.Close();
            client.Close();

            Console.WriteLine("You are now disconnected.");
            Console.ReadKey();
        }

        public static void ReceiveData(TcpClient client)
        {
            NetworkStream ns = client.GetStream();
            byte[] receivedBytes = new byte[1024];
            int byteCount;

            try
            {
                while ((byteCount = ns.Read(receivedBytes, 0, receivedBytes.Length)) > 0)
                {
                    Console.Write(Encoding.UTF8.GetString(receivedBytes, 0, byteCount));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nConnection lost: " + ex.Message);
            }
        }

        private static string ReadLineFromStream(NetworkStream ns)
        {
            var buffer = new byte[1024];
            int bytes = ns.Read(buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer, 0, bytes);
        }
    }
}