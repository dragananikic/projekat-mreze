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

        string username = LoadOrAskUsername("manager_user.txt", "Unesi username menadžera: ");

        int tcpPort = UdpLoginAsManager(username);

        using Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        tcp.Connect(new IPEndPoint(IPAddress.Loopback, tcpPort));

        while (true)
        {
            Console.WriteLine("\n--- MENADZER MENI ---");
            Console.WriteLine("1) Dodaj zadatak (TCP)");
            Console.WriteLine("2) Prikazi moje zadatke 'U toku' (UDP)");
            Console.WriteLine("3) Povecaj prioritet zadatku (UDP)");
            Console.WriteLine("0) Izlaz");
            Console.Write(">> ");

            string? op = Console.ReadLine();
            if (op == "0") break;

            if (op == "1")
            {
                var t = ReadTaskFromConsole(username);
                string json = JsonSerializer.Serialize(t);
                LineProto.SendLine(tcp, "ADD_TASK|" + json);
                string resp = ReceiveOneLine(tcp);
                Console.WriteLine("Server: " + resp);
            }
            else if (op == "2")
            {
                var svi = UdpGetSvi(username);
                PrintMenadzerOverview(svi);
            }
            else if (op == "3")
            {
                Console.Write("Naziv zadatka: ");
                string naziv = Console.ReadLine() ?? "";
                Console.Write("Novi prioritet (int): ");
                int novi = int.Parse(Console.ReadLine() ?? "0");


                string resp = UdpSetPrioritetZadatakFormat(naziv, novi);
                Console.WriteLine("Server: " + resp);
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

    static int UdpLoginAsManager(string username)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);
        string msg = "MENADZER:" + username;

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

    static List<ZadatakProjekta> UdpGetUToku(string menadzer)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);

        udp.SendTo(Encoding.UTF8.GetBytes("GET_UTOKU:" + menadzer), server);

        byte[] buf = new byte[8192];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = udp.ReceiveFrom(buf, ref remote);
        string json = Encoding.UTF8.GetString(buf, 0, bytes);

        var list = JsonSerializer.Deserialize<List<ZadatakProjekta>>(json);
        return list ?? new List<ZadatakProjekta>();
    }

    static string UdpSetPrioritet(string menadzer, string naziv, int novi)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);

        string msg = $"PRIORITET:{menadzer}:{naziv}:{novi}";
        udp.SendTo(Encoding.UTF8.GetBytes(msg), server);

        byte[] buf = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = udp.ReceiveFrom(buf, ref remote);
        return Encoding.UTF8.GetString(buf, 0, bytes);
    }

    static ZadatakProjekta ReadTaskFromConsole(string menadzer)
    {
        ZadatakProjekta t = new ZadatakProjekta();
        t.Menadzer = menadzer;

        Console.Write("Naziv: ");
        t.Naziv = Console.ReadLine() ?? "";

        Console.Write("Zaposleni (username): ");
        t.Zaposleni = Console.ReadLine() ?? "";

        Console.Write("Rok (yyyy-MM-dd): ");
        t.Rok = DateTime.Parse(Console.ReadLine() ?? "");

        Console.Write("Prioritet (int, manji=veci): ");
        t.Prioritet = int.Parse(Console.ReadLine() ?? "0");

        
        return t;
    }

    static void PrintUTokuWithHighlightsAndComments(List<ZadatakProjekta> utoku)
    {
        Console.WriteLine("\n--- ZADACI U TOKU ---");

        
        var now = DateTime.Now.Date;

        foreach (var t in utoku)
        {
            int days = (t.Rok.Date - now).Days;
            bool highlight = (days == 1 || days == 2);

            if (highlight)
                Console.WriteLine($"!!! HITNO ({days} dana do roka) -> {t.Naziv}, zaposleni={t.Zaposleni}, prioritet={t.Prioritet}, rok={t.Rok:yyyy-MM-dd}");
            else
                Console.WriteLine($"- {t.Naziv}, zaposleni={t.Zaposleni}, prioritet={t.Prioritet}, rok={t.Rok:yyyy-MM-dd}");
        }

        
        Console.WriteLine("\nNapomena: komentar završenih ćeš videti kada proširimo GET_UTOKU da vraća i 'završene sa komentarom'.");
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

    static List<ZadatakProjekta> UdpGetSvi(string menadzer)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);

        udp.SendTo(Encoding.UTF8.GetBytes("GET_SVI:" + menadzer), server);

        byte[] buf = new byte[8192];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = udp.ReceiveFrom(buf, ref remote);
        string json = Encoding.UTF8.GetString(buf, 0, bytes);

        return JsonSerializer.Deserialize<List<ZadatakProjekta>>(json) ?? new List<ZadatakProjekta>();
    }

    static void PrintMenadzerOverview(List<ZadatakProjekta> svi)
    {
        Console.WriteLine("\n--- PREGLED MOJIH ZADATAKA ---");
        var now = DateTime.Now.Date;

        Console.WriteLine("\nU TOKU:");
        foreach (var t in svi.Where(x => x.Status == StatusZadatka.UToku))
        {
            int days = (t.Rok.Date - now).Days;
            if (days == 1 || days == 2)
                Console.WriteLine($"!!! HITNO ({days} dana) -> {t.Naziv} | zaposleni={t.Zaposleni} | prioritet={t.Prioritet} | rok={t.Rok:yyyy-MM-dd}");
            else
                Console.WriteLine($"- {t.Naziv} | zaposleni={t.Zaposleni} | prioritet={t.Prioritet} | rok={t.Rok:yyyy-MM-dd}");
        }

        Console.WriteLine("\nNA CEKANJU (pred rok):");
        foreach (var t in svi.Where(x => x.Status == StatusZadatka.NaCekanju))
        {
            int days = (t.Rok.Date - now).Days;
            if (days == 1 || days == 2)
                Console.WriteLine($"!!! MOZE DA SE POVEĆA PRIORITET ({days} dana) -> {t.Naziv} | zaposleni={t.Zaposleni} | prioritet={t.Prioritet} | rok={t.Rok:yyyy-MM-dd}");
        }
    }

    static string UdpSetPrioritetZadatakFormat(string naziv, int novi)
    {
        using Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        EndPoint server = new IPEndPoint(IPAddress.Loopback, NetConsts.UdpPort);

        string msg = $"{naziv}:{novi}";
        udp.SendTo(Encoding.UTF8.GetBytes(msg), server);

        byte[] buf = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        int bytes = udp.ReceiveFrom(buf, ref remote);
        return Encoding.UTF8.GetString(buf, 0, bytes);
    }
}
