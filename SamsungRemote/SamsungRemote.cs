using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using WebSocketSharp;

namespace SamsungRemote
{
    public class SamsungRemote
    {
        private Settings settings;
        private WebSocket websocket;
        private string websocketUrl;
        private bool tokenInitialized;


        public SamsungRemote(Settings s)
        {
            settings = s;
            string urlPrefix = settings.Port == 8001 ? "ws" : "wss";
            websocketUrl = $"{urlPrefix}://{settings.IpAddr}:{settings.Port}/api/v2/channels/samsung.remote.control?name={settings.AppName}";

            tokenInitialized = false;
            if (File.Exists("SamsungRemote.token"))
            {
                settings.Token = File.ReadAllText("SamsungRemote.token");
            }

            websocketUrl += $"&token={settings.Token}";
            websocket = new WebSocket(websocketUrl);
        }

        // Make initialize ? await Task.Run => Constructor?/Init for example
        // IDisposable?
        private void Connect()
        {
            websocket.Connect();
        }

        private void Close()
        {
            websocket.Close();
        }

        // WebSocketSharp async methods uses BeginInvoke which is not supported for .NET Core https://devblogs.microsoft.com/dotnet/migrating-delegate-begininvoke-calls-for-net-core/
        private async Task SendAsync(string key)
        {
            await Task.Run(() =>
            {
                Send(key);
            });
        }
        
        private void Send(string key)
        {
            if (settings.Token.Equals("token")) throw new Exception("Invalid token");
            Parameters parameters = new Parameters(key);
            Command cmd = new Command(parameters);
            string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
            Debug.WriteLine("Sending key data: " + data);
            websocket.Send(data);
        }

        private async Task GenerateNewToken()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            using (websocket = new WebSocket(websocketUrl))
            {
                websocket.OnOpen += async (sender, e) =>
                {
                    Parameters parameters = new Parameters(Keys.KEY_VOLDOWN);
                    Command cmd = new Command(parameters);
                    string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
                    Debug.WriteLine("Generate new token request: " + data);
                    await SendAsync(data);                   
                };

                websocket.OnMessage += (sender, e) =>
                {
                    Debug.WriteLine("Generate new token data: " + e.Data); 
                    JObject response = JObject.Parse(e.Data);
                    string tempToken = response?["data"]?["token"]?.ToString() ?? "token";
                    if (tempToken.Equals("token")) throw new Exception("Invalid token");

                    File.WriteAllText("SamsungRemote.token", settings.Token);
                    Debug.WriteLine($"New token {settings.Token} saved to file");
                    settings.Token = tempToken;
                    tokenSource.Cancel();
                };

                websocket.OnError += (sender, e) =>
                {
                    Debug.WriteLine("Generate new token error: " + e.Message);
                };

                websocket.OnClose += (sender, e) =>
                {
                    Debug.WriteLine("Genereate new token ws close: " + e.Reason);
                };

                websocket.Connect();
                await Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token); // allow user 30 seconds to accept connection prompt on TV
            }
        }

        private async Task<bool> IsActive(int delay = 0)
        {
            if (delay != 0) await Task.Delay(delay);

            using (HttpClient client = new HttpClient())
            {
                string urlSuffix = settings.Port == 55000 ? "/ms/1.0/" : "/api/v2/";
                string url = $"http://{settings.IpAddr}:8001{urlSuffix}";

                CancellationTokenSource tokenSource = new CancellationTokenSource();
                HttpResponseMessage response;
                client.Timeout = TimeSpan.FromSeconds(2);

                try
                {
                    response = await client.GetAsync(url);
                }
                catch (WebException)
                {
                    return false;
                }
                catch (TaskCanceledException ex)
                {
                    if (ex.CancellationToken == tokenSource.Token)
                    {
                        return false;
                    }
                    else
                    {
                        return false;
                    }
                }

                string content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"isActive response: {content}");
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

    public class Settings
    {
        public string AppName { get; set; }
        public string IpAddr { get; set; }
        public string MacAddr { get; set; }
        public int Port { get; set; }
        public string Token { get; set; }

        Settings(string appName, string ipAddr, string macAddr, int port, string token = "token")
        {
            byte[] appNameBytes = System.Text.Encoding.UTF8.GetBytes(appName);
            AppName = Convert.ToBase64String(appNameBytes);
            IpAddr = ipAddr;
            MacAddr = macAddr;
            Port = port;
            Token = token.Equals("token") ? String.Empty : token;
        }
    }

    public class Command
    {
        // Json payload requires lowercase
#pragma warning disable IDE1006 // Naming Styles
        public string method { get; set; }
        public Parameters parameters { get; set; }
#pragma warning restore IDE1006 // Naming Styles

        public Command(Parameters p)
        {
            method = "ms.remote.control";
            parameters = p;
        }
    }

    public class Parameters
    {
        public string Cmd { get; set; }
        public string DataOfCmd { get; set; }
        public string Option { get; set; }
        public string TypeOfRemote { get; set; }

        public Parameters(string key)
        {
            Cmd = "Click";
            DataOfCmd = key;
            Option = "false";
            TypeOfRemote = "SendRemoteKey";
        }
    }
}