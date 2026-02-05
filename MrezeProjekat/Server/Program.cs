using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Common;

class Program
{
    static readonly Dictionary<string, List<ZadatakProjekta>> _zadaciPoMenadzeru = new();

    
    static readonly List<Socket> _tcpClients = new();
    static readonly Dictionary<Socket, StringBuilder> _tcpBuffers = new();

    static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        udp.Bind(new IPEndPoint(IPAddress.Any, NetConsts.UdpPort));
        udp.Blocking = false;

        Socket listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listen.Bind(new IPEndPoint(IPAddress.Any, NetConsts.TcpPort));
        listen.Listen(50);
        listen.Blocking = false;

        Console.WriteLine($"SERVER START: UDP {NetConsts.UdpPort}, TCP {NetConsts.TcpPort}");

       
        while (true)
        {
            List<Socket> readSockets = new List<Socket>();
            readSockets.Add(udp);
            readSockets.Add(listen);
            readSockets.AddRange(_tcpClients);

            Socket.Select(readSockets, null, null, NetConsts.SelectTimeoutMicroseconds); // 1s :contentReference[oaicite:6]{index=6}

            foreach (var s in readSockets)
            {
                if (s == udp)
                {
                    HandleUdp(udp);
                }
                else if (s == listen)
                {
                    AcceptTcpClient(listen);
                }
                else
                {
                    HandleTcpClient(s);
                }
            }

        }
    }

    static void HandleUdp(Socket udp)
    {
        try
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[2048];
            int bytes = udp.ReceiveFrom(buffer, ref remote);
            string msg = Encoding.UTF8.GetString(buffer, 0, bytes);

            Console.WriteLine($"[UDP] {remote} -> {msg}");

            if (msg.StartsWith("MENADZER:"))
            {
                string m = msg.Substring("MENADZER:".Length).Trim();
                if (!_zadaciPoMenadzeru.ContainsKey(m))
                    _zadaciPoMenadzeru[m] = new List<ZadatakProjekta>();

                SendUdp(udp, remote, $"TCP:{NetConsts.TcpPort}");
                return;
            }

            if (msg.StartsWith("ZAPOSLENI:"))
            {
                SendUdp(udp, remote, $"TCP:{NetConsts.TcpPort}");
                return;
            }

            if (msg.StartsWith("GET_UTOKU:"))
            {
                string menadzer = msg.Substring("GET_UTOKU:".Length).Trim();
                var list = GetMenadzerTasks(menadzer)
                    .Where(t => t.Status == StatusZadatka.UToku)
                    .ToList();
                string json = JsonSerializer.Serialize(list);
                SendUdp(udp, remote, json);
                return;
            }

            if (msg.StartsWith("GET_SVI:"))
            {
                string menadzer = msg.Substring("GET_SVI:".Length).Trim();
                var list = GetMenadzerTasks(menadzer); 
                string json = JsonSerializer.Serialize(list);
                SendUdp(udp, remote, json);
                return;
            }

            if (msg.StartsWith("PRIORITET:"))
            {
                var parts = msg.Split(':', 4);
                if (parts.Length == 4)
                {
                    string menadzer = parts[1];
                    string naziv = parts[2];
                    if (int.TryParse(parts[3], out int novi))
                    {
                        bool ok = SetPrioritet(menadzer, naziv, novi);
                        SendUdp(udp, remote, ok ? "OK" : "NOT_FOUND");
                        return;
                    }
                }
                SendUdp(udp, remote, "ERR");
                return;
            }
            
            if (msg.Contains(':') &&
                !msg.StartsWith("MENADZER:") &&
                !msg.StartsWith("ZAPOSLENI:") &&
                !msg.StartsWith("GET_UTOKU:") &&
                !msg.StartsWith("GET_SVI:") &&
                !msg.StartsWith("PRIORITET:"))
            {
                var parts = msg.Split(':', 2);
                if (parts.Length == 2 && int.TryParse(parts[1], out int novi))
                {
                    string naziv = parts[0];

                   
                    bool ok = SetPrioritetAnyManager(naziv, novi);
                    SendUdp(udp, remote, ok ? "OK" : "NOT_FOUND");
                    return;
                }

                SendUdp(udp, remote, "ERR");
                return;
            }
            SendUdp(udp, remote, "UNKNOWN_UDP");
        }
        catch (SocketException)
        {
           
        }
        catch (Exception ex)
        {
            Console.WriteLine("[UDP] Error: " + ex.Message);
        }
    }

    static void SendUdp(Socket udp, EndPoint remote, string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        udp.SendTo(data, remote);
    }

    static void AcceptTcpClient(Socket listen)
    {
        try
        {
            Socket client = listen.Accept();
            client.Blocking = false; 
            _tcpClients.Add(client);
            _tcpBuffers[client] = new StringBuilder();
            Console.WriteLine("[TCP] Client connected: " + client.RemoteEndPoint);
        }
        catch (SocketException)
        {
        }
    }

    static void HandleTcpClient(Socket client)
    {
        try
        {
            byte[] buf = new byte[4096];
            int bytes = client.Receive(buf);
            if (bytes == 0)
            {
                RemoveClient(client);
                return;
            }

            string chunk = Encoding.UTF8.GetString(buf, 0, bytes);
            _tcpBuffers[client].Append(chunk);

            foreach (var line in LineProto.ExtractLines(_tcpBuffers[client]))
            {
                Console.WriteLine($"[TCP] {client.RemoteEndPoint} -> {line}");
                ProcessTcpLine(client, line);
            }
        }
        catch (SocketException)
        {
           
        }
        catch (Exception ex)
        {
            Console.WriteLine("[TCP] Error: " + ex.Message);
            RemoveClient(client);
        }
    }

    static void ProcessTcpLine(Socket client, string line)
    {
        var parts = line.Split('|');
        if (parts.Length == 0) return;

        string cmd = parts[0];

        if (cmd == "ADD_TASK" && parts.Length == 2)
        {
            var task = JsonSerializer.Deserialize<ZadatakProjekta>(parts[1]);
            if (task == null) { LineProto.SendLine(client, "ERR"); return; }

        
            task.Status = StatusZadatka.NaCekanju;

            if (!_zadaciPoMenadzeru.ContainsKey(task.Menadzer))
                _zadaciPoMenadzeru[task.Menadzer] = new List<ZadatakProjekta>();

            _zadaciPoMenadzeru[task.Menadzer].Add(task);
            LineProto.SendLine(client, "OK");
            return;
        }

        if (cmd == "GET_ASSIGNED" && parts.Length == 2)
        {
            string zaposleni = parts[1];

           
            var assigned = _zadaciPoMenadzeru.Values
                .SelectMany(x => x)
                .Where(t => t.Zaposleni.Equals(zaposleni, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Prioritet) 
                .ToList();

            string json = JsonSerializer.Serialize(assigned);
            LineProto.SendLine(client, "DATA|" + json);
            return;
        }

        if (cmd == "START_TASK" && parts.Length == 3)
        {
            string zaposleni = parts[1];
            string naziv = parts[2];

            var task = FindTaskByEmployeeAndName(zaposleni, naziv);
            if (task == null) { LineProto.SendLine(client, "NOT_FOUND"); return; }

            task.Status = StatusZadatka.UToku; 
            LineProto.SendLine(client, "OK");
            return;
        }

        if (cmd == "DONE_TASK" && (parts.Length == 3 || parts.Length == 4))
        {
            string zaposleni = parts[1];
            string naziv = parts[2];
            string komentar = parts.Length == 4 ? parts[3] : "";

            var task = FindTaskByEmployeeAndName(zaposleni, naziv);
            if (task == null) { LineProto.SendLine(client, "NOT_FOUND"); return; }

            task.Status = StatusZadatka.Zavrsen; 
            if (!string.IsNullOrWhiteSpace(komentar))
                task.Komentar = komentar;

            LineProto.SendLine(client, "OK");
            return;
        }

        LineProto.SendLine(client, "UNKNOWN_TCP");
    }

    static List<ZadatakProjekta> GetMenadzerTasks(string menadzer)
    {
        if (_zadaciPoMenadzeru.TryGetValue(menadzer, out var list))
            return list;
        return new List<ZadatakProjekta>();
    }

    static bool SetPrioritet(string menadzer, string naziv, int novi)
    {
        if (!_zadaciPoMenadzeru.TryGetValue(menadzer, out var list))
            return false;

        var task = list.FirstOrDefault(t => t.Naziv.Equals(naziv, StringComparison.OrdinalIgnoreCase));
        if (task == null) return false;

        task.Prioritet = novi;
        return true;
    }

    static ZadatakProjekta? FindTaskByEmployeeAndName(string zaposleni, string naziv)
    {
        return _zadaciPoMenadzeru.Values
            .SelectMany(x => x)
            .FirstOrDefault(t =>
                t.Zaposleni.Equals(zaposleni, StringComparison.OrdinalIgnoreCase) &&
                t.Naziv.Equals(naziv, StringComparison.OrdinalIgnoreCase));
    }

    static void RemoveClient(Socket client)
    {
        Console.WriteLine("[TCP] Client disconnected: " + client.RemoteEndPoint);
        _tcpClients.Remove(client);
        _tcpBuffers.Remove(client);
        try { client.Shutdown(SocketShutdown.Both); } catch { }
        try { client.Close(); } catch { }
    }

    static bool SetPrioritetAnyManager(string naziv, int novi)
    {
        foreach (var kv in _zadaciPoMenadzeru)
        {
            var task = kv.Value.FirstOrDefault(t => t.Naziv.Equals(naziv, StringComparison.OrdinalIgnoreCase));
            if (task != null)
            {
                task.Prioritet = novi;
                return true;
            }
        }
        return false;
    }
}
