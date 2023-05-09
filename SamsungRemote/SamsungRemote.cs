using System.Diagnostics;
using System.Net;
using WebSocketSharp;

namespace SamsungRemote
{
    public class SamsungRemote
    {
        private string appName;
        private string ipAddr;
        private string macAddr;
        private int port;
        private string token;
        private string wsUrl;

        WebSocket ws;

        public SamsungRemote(string a, string i, string m, int p, string t = "token")
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(a);
            appName = Convert.ToBase64String(bytes);
            ipAddr = i;
            macAddr = m;
            port = p;
            token = t.Equals("token") ? String.Empty : t;

            string urlPrefix = port == 8001 ? "ws" : "wss";
            wsUrl = $"{urlPrefix}://{ipAddr}:{port}/api/v2/channels/samsung.remote.control?name={appName}";

            if (File.Exists("SamsungRemote.token"))
            {
                token = File.ReadAllText("SamsungRemote.token");
                wsUrl += $"&token={token}";
                ws = new WebSocket(wsUrl);
            }
            else
            {
                token = GenerateNewToken();
                File.WriteAllText("SamsungRemote.token", token);
                wsUrl += $"&token={token}";
                ws = new WebSocket(wsUrl);
            }

            //return $"{(Config.Port == 8001 ? "ws" : "wss")}://{Config.IpAddr}:{Config.Port}/api/v2/channels/samsung.remote.control?name={nameApp}{(!Config.Token.Equals(String.Empty) ? $"&token={Config.Token}" : "")}";
        }

        private void CloseAsync()
        {
            ws.CloseAsync();
        }

        private void Close()
        {
            ws.Close();
        }

        private string GenerateNewToken()
        {
            string res = String.Empty;
            for (int i = 0; i < 8; i++)
            {
                res += Random.Shared.Next().ToString();
            }
            return res;
        }

        private async Task<bool> IsActive()
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = null;
                string url = $"http://{ipAddr}:8001{(Config.Port == 55000 ? "/ms/1.0/" : "/api/v2/")}"; // `http://${this.IP}:8001${this.PORT === 55000 ? '/ms/1.0/' : '/api/v2/'}`
                client.Timeout = TimeSpan.FromSeconds(2);
                CancellationTokenSource tokenSource = new CancellationTokenSource();
                try
                {
                    response = await client.GetAsync(url);
                }
                catch (WebException ex)
                {
                    // handle web exception
                    return false;
                }
                catch (TaskCanceledException ex)
                {
                    if (ex.CancellationToken == tokenSource.Token)
                    {
                        // a real cancellation, triggered by the caller
                        return false;
                    }
                    else
                    {
                        // a web request timeout (possibly other things!?)
                        return false;
                    }
                }
                string content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($" Response content: {content}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
                return false;
            }
        }
    }

    public static class Keys
    {
        public static string KEY_VOLDOWN { get => "KEY_VOLDOWN"; }
    }
}