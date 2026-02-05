using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Common;

class Program
{
    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        string username = LoadOrAskUsername("employee_user.txt", "Unesi username zaposlenog: ");

        int tcpPort = UdpLoginAsEmployee(username);

        using Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        tcp.Connect(new IPEndPoint(IPAddress.Loopback, tcpPort));

        while (true)
        {
            Console.WriteLine("\n--- ZAPOSLENI MENI ---");
            Console.WriteLine("1) Prikazi dodeljene zadatke (TCP)");
            Console.WriteLine("2) Preuzmi zadatak -> status 'U toku' (TCP)");
            Console.WriteLine("3) Zavrsi zadatak (TCP) + opcioni komentar");
            Console.WriteLine("0) Izlaz");
            Console.Write(">> ");

            string? op = Console.ReadLine();
            if (op == "0") break;

            if (op == "1")
            {
                LineProto.SendLine(tcp, "GET_ASSIGNED|" + username);

                string resp = ReceiveOneLine(tcp);
                if (resp.StartsWith("DATA|"))
                {
                    string json = resp.Substring("DATA|".Length);
                    var list = JsonSerializer.Deserialize<List<ZadatakProjekta>>(json) ?? new();

                    Console.WriteLine("\n--- DODELJENI (sort po prioritetu) ---");
                    foreach (var t in list)
                        Console.WriteLine($"- {t.Naziv} | status={t.Status} | prioritet={t.Prioritet} | rok={t.Rok:yyyy-MM-dd} | menadzer={t.Menadzer}");
                }
                else
                {
                    Console.WriteLine("Server: " + resp);
                }
            }
            else if (op == "2")
            {
                Console.Write("Naziv zadatka: ");
                string naziv = Console.ReadLine() ?? "";

                LineProto.SendLine(tcp, $"START_TASK|{username}|{naziv}");
                Console.WriteLine("Server: " + ReceiveOneLine(tcp));
            }
            else if (op == "3")
            {
                Console.Write("Naziv zadatka: ");
                string naziv = Console.ReadLine() ?? "";

                Console.Write("Komentar (enter za prazno): ");
                string komentar = Console.ReadLine() ?? "";

                if (string.IsNullOrWhiteSpace(komentar))
                    LineProto.SendLine(tcp, $"DONE_TASK|{username}|{naziv}");
                else
                    LineProto.SendLine(tcp, $"DONE_TASK|{username}|{naziv}|{komentar}");

                Console.WriteLine("Server: " + ReceiveOneLine(tcp));
            }
        }
    }

    static string LoadOrAskUsername(string file, string prompt)
    {
        if (File.Exists(file))
            return File.ReadAllText(file).Trim();

        Console.Write(prompt);
        string u = Console.ReadLine() ?? "";
        File.WriteAllText(file, u);
        return u;
    }

    static int UdpLoginAsEmployee(string username)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);
        string msg = "ZAPOSLENI:" + username;

        udp.SendTo(Encoding.UTF8.GetBytes(msg), server);

        byte[] buf = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = udp.ReceiveFrom(buf, ref remote);
        string resp = Encoding.UTF8.GetString(buf, 0, bytes);

        if (resp.StartsWith("TCP:") && int.TryParse(resp.Substring(4), out int tcpPort))
        {
            Console.WriteLine("Prijavljen. TCP port: " + tcpPort);
            return tcpPort;
        }

        throw new Exception("Neispravan UDP odgovor: " + resp);
    }

    static string ReceiveOneLine(Socket s)
    {
        var sb = new StringBuilder();
        byte[] buf = new byte[1024];
        while (true)
        {
            int bytes = s.Receive(buf);
            if (bytes <= 0) return "";
            sb.Append(Encoding.UTF8.GetString(buf, 0, bytes));
            var str = sb.ToString();
            int idx = str.IndexOf('\n');
            if (idx >= 0) return str.Substring(0, idx).TrimEnd('\r');
        }
    }
}
