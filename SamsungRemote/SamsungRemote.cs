using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WebSocketSharp;

namespace SamsungRemoteLib
{
    // WebSocketSharp async methods use BeginInvoke which is not supported for .NET Core
    // https://github.com/sta/websocket-sharp/pull/712
    // To use asynchronously execute *methodName*Async() which is simply synchronous method wrapped in await Task.Run

    public class SamsungRemote : IDisposable
    {
        private string websocketUrl;
        private bool wolEndpointSet;
        private byte[] macAddrBytes;
        private IPEndPoint? wolEndpoint;
        private WebSocket? websocket;
        private Settings settings;

        public SamsungRemote(Settings s)
        {
            settings = s;
            macAddrBytes = new byte[6];
            string urlPrefix = settings.Port == 8001 ? "ws" : "wss";
            websocketUrl = $"{urlPrefix}://{settings.IpAddr}:{settings.Port}/api/v2/channels/samsung.remote.control?name={settings.AppName}";

            //websocket.OnOpen
            //websocket.OnClose
            //websocket.OnMessage
            //websocket.OnError += Websocket_OnError;
        }

        public void Connect()
        {
            if (settings.Token != null)
            {
                websocketUrl += $"&token={settings.Token}";
            }
            else
            {
                GenerateNewToken();
            }

            websocket = new WebSocket(websocketUrl);
            websocket.Connect();
        }

        public void Press(string key)
        {
            if (websocket == null || !websocket.IsAlive) throw new ArgumentNullException("WebSocket is ***null*** call Connect() or ConnectAsync() before key press");
            if (settings.Token == null) throw new ArgumentNullException("Token is ***null*** call GenerateNewToken() or GenerateNewTokenAsync() before key press");

            Parameters parameters = new Parameters(key);
            Command cmd = new Command(parameters);
            string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
            Log("Sending key data: " + data);
            websocket?.Send(data);
        }

        public void GenerateNewToken(bool timeoutException = true)
        {
            Task task = Task.Run(() => GenerateNewTokenAsync(timeoutException));
            task.Wait();
        }

        public async Task GenerateNewTokenAsync(bool timeoutException = true)
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            using (websocket = new WebSocket(websocketUrl))
            {
                websocket.OnOpen += (sender, e) =>
                {
                    Parameters parameters = new Parameters(Keys.KEY_HOME); // token only received with home
                    Command cmd = new Command(parameters);
                    string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
                    Log("Generate new token request: " + data);
                    Press(data);
                };

                websocket.OnMessage += (sender, e) =>
                {
                    Log("Generate new token data: " + e.Data);
                    JObject response = JObject.Parse(e.Data);
                    string newToken = response?["data"]?["token"]?.ToString() ?? "token";

                    Log($"New token {settings.Token} generated");
                    settings.Token = newToken;
                    websocketUrl += $"&token={settings.Token}";
                    tokenSource.Cancel();
                };

                websocket.OnError += (sender, e) =>
                {
                    Log("Generate new token error: " + e.Message);
                };

                websocket.Connect();
                Log("Waiting 30 seconds for user to accept connection prompt on TV...");
                await Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token); // allow user 30 seconds to accept connection prompt on TV
                if (settings.Token == null && timeoutException) throw new ArgumentNullException("Token is ***null*** check TV for accept connection prompt");
            }
        }

        public bool IsTvOn(int delay = 0)
        {
            if (delay != 0) Task.Delay(delay).Wait();
            Task<bool> task = Task.Run(() => IsTvOnAsync());
            task.Wait();
            return task.Result;
        }

        public async Task<bool> IsTvOnAsync(int delay = 0)
        {
            if (delay != 0) await Task.Delay(delay);

            using (HttpClient client = new HttpClient())
            {
                string urlSuffix = settings.Port == 55000 ? "/ms/1.0/" : "/api/v2/";
                string url = $"http://{settings.IpAddr}:8001{urlSuffix}";

                CancellationTokenSource tokenSource = new CancellationTokenSource();
                HttpResponseMessage response;
                client.Timeout = TimeSpan.FromSeconds(1);

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
                Log($"isActive response: {content.Trim()}");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
                return false;
            }
        }

        public void TurnOn(int repeat = 1)
        {
            if (!wolEndpointSet)
            {
                string mac = settings.MacAddr.Trim().Replace("-", "");
                if (mac.Length != 12) throw new ArgumentException("MAC address should be 12 characters without separator");
                wolEndpoint = InitializeMagic();
                wolEndpointSet = true;
            }

            using (UdpClient client = new UdpClient())
            {
                try
                {
                    byte[] packet = CreateMagicPacket(macAddrBytes);
                    for (int i = 0; i < repeat; i++)
                    {
                        client.Send(packet, packet.Length, wolEndpoint);
                    }
                }
                catch (Exception ex)
                {
                    Log($"SendMagicPacket error: {ex.Message}");
                }
            }
        }

        public IPEndPoint InitializeMagic()
        {
            IPAddress[] host;
            try
            {
                host = Dns.GetHostAddresses(settings.IpAddr);
            }
            catch
            {
                throw new ArgumentException($"Could not resolve address: {settings.IpAddr}");
            }
            if (host.Length == 0)
            {
                throw new ArgumentNullException($"Could not resolve address: {settings.IpAddr}");
            }

            IPAddress subnet = IPAddress.Parse(settings.Subnet);
            byte[] subnetAddrBytes = subnet.GetAddressBytes();
            byte[] ipAddrBytes = host[0].GetAddressBytes();
            for (int i = 0; i < ipAddrBytes.Length; i++)
            {
                subnetAddrBytes[i] = (byte)(ipAddrBytes[i] | (subnetAddrBytes[i] ^ 0xff));
            }

            wolEndpoint = new IPEndPoint(new IPAddress(subnetAddrBytes), settings.Port);
            for (int j = 0; j < 6; j++)
            {
                try
                {
                    settings.MacAddr.Substring(2 * j, 2);
                    macAddrBytes[j] = Convert.ToByte(settings.MacAddr.Substring(2 * j, 2), 0x10);
                }
                catch (Exception)
                {
                    throw new ArgumentException("Invalid MAC address");
                }
            }

            return wolEndpoint;
        }

        public byte[] CreateMagicPacket(byte[] macAddress)
        {
            byte[] packet = new byte[0x66];
            for (int i = 0; i < 6; i++)
            {
                packet[i] = 0xff;
            }
            for (int j = 1; j <= 0x10; j++)
            {
                macAddress.CopyTo(packet, j * 6);
            }
            return packet;
        }

        public void Log(string msg)
        {
            if (settings.Debug) Debug.WriteLine(msg);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _disposed = false; // To detect redundant calls
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Log("Closing websocket connection");
                    websocket?.Close();
                }
            }
        }
    }

    public class Settings
    {
        public string AppName { get; set; }
        public string IpAddr { get; set; }
        public string MacAddr { get; set; }
        public int Port { get; set; }
        public string Subnet { get; set; }
        public string? Token { get; set; }
        public bool Debug { get; set; }

        public Settings(string appName, string ipAddr, string subnet, string macAddr, int port, string? token, bool debug)
        {
            byte[] appNameBytes = System.Text.Encoding.UTF8.GetBytes(appName);
            AppName = Convert.ToBase64String(appNameBytes);
            IpAddr = ipAddr;
            MacAddr = macAddr.Replace("-", "");
            Port = port;
            Subnet = subnet;
            Token = token;
            Debug = debug;
        }
    }

    public class Command
    {
        // HTTP request invalid if payload field names start with uppercase letter
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

    public static class Keys
    {
        public static string KEY_VOLDOWN { get => "KEY_VOLDOWN"; }
        public static string KEY_HOME { get => "KEY_HOME"; }
    }
}