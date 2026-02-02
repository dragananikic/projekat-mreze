using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public static class LineProto
    {
        public static void SendLine(Socket s, string line)
        {
            var data = Encoding.UTF8.GetBytes(line + "\n");
            s.Send(data);
        }

        public static IEnumerable<string> ExtractLines(StringBuilder sb)
        {
            while (true)
            {
                var str = sb.ToString();
                int idx = str.IndexOf('\n');
                if (idx < 0) yield break;

                string line = str.Substring(0, idx).TrimEnd('\r');
                sb.Remove(0, idx + 1);
                yield return line;
            }
        }
    }
}
