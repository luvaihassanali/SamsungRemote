using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
// WebSocketSharp async methods use BeginInvoke which is not supported for .NET Core https://devblogs.microsoft.com/dotnet/migrating-delegate-begininvoke-calls-for-net-core/
using WebSocketSharp;

namespace SamsungRemoteLib
{
    public class SamsungRemote : IDisposable
    {
        private bool disposed;
        private string websocketUrl;
        private bool tokenInitialized;
        private bool wolEndpointSet;
        private byte[] macAddrBytes;
        private IPEndPoint? wolEndpoint;
        private WebSocket? websocket;
        private Settings settings;

        public SamsungRemote(Settings s)
        {
            disposed = false;
            settings = s;
            tokenInitialized = false;
            macAddrBytes = new byte[6];
            string urlPrefix = settings.Port == 8001 ? "ws" : "wss";
            websocketUrl = $"{urlPrefix}://{settings.IpAddr}:{settings.Port}/api/v2/channels/samsung.remote.control?name={settings.AppName}";
        }

        public void Connect()
        {
            if (File.Exists("SamsungRemote.token"))
            {
                settings.Token = File.ReadAllText("SamsungRemote.token");
                tokenInitialized = true;
                websocketUrl += $"&token={settings.Token}";
            }
            else
            {
                GenerateNewToken();
            }

            websocket = new WebSocket(websocketUrl);
            websocket.Connect();
        }

        public void ConnectWithToken(string token)
        {
            settings.Token = token;
            tokenInitialized = true;
            websocketUrl += $"&token={settings.Token}";
            websocket = new WebSocket(websocketUrl);
            websocket.Connect();
        }

        public void Send(string key)
        {
            if (!tokenInitialized) throw new TokenInvalidException();
            Parameters parameters = new Parameters(key);
            Command cmd = new Command(parameters);
            string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
            Debug.WriteLine("Sending key data: " + data);
            websocket?.Send(data);
        }

        public void GenerateNewToken()
        {
            Task task = Task.Run(() => GenerateNewTokenAsync());
            task.Wait();
        }

        public async Task GenerateNewTokenAsync()
        {
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            using (websocket = new WebSocket(websocketUrl))
            {
                websocket.OnOpen += (sender, e) =>
                {
                    Parameters parameters = new Parameters(Keys.KEY_VOLDOWN);
                    Command cmd = new Command(parameters);
                    string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
                    Debug.WriteLine("Generate new token request: " + data);
                    Send(data);
                };

                websocket.OnMessage += (sender, e) =>
                {
                    Debug.WriteLine("Generate new token data: " + e.Data);
                    JObject response = JObject.Parse(e.Data);
                    string tempToken = response?["data"]?["token"]?.ToString() ?? "token";

                    File.WriteAllText("SamsungRemote.token", settings.Token);
                    Debug.WriteLine($"New token {settings.Token} saved to file");

                    settings.Token = tempToken;
                    websocketUrl += $"&token={settings.Token}";
                    tokenInitialized = true;
                    tokenSource.Cancel();
                };

                websocket.OnError += (sender, e) =>
                {
                    Debug.WriteLine("Generate new token error: " + e.Message);
                };

                websocket.Connect();
                Debug.WriteLine("Waiting 30 seconds for user to accept connection prompt on TV");
                await Task.Delay(TimeSpan.FromSeconds(30), tokenSource.Token); // allow user 30 seconds to accept connection prompt on TV
                if (settings.Token.Equals("token")) throw new TokenInvalidException();
            }
        }

        public bool IsActive(int delay = 0)
        {
            if (delay != 0) Task.Delay(delay).Wait();
            Task<bool> task = Task.Run(() => IsActiveAsync());
            task.Wait();
            return task.Result;
        }

        public async Task<bool> IsActiveAsync(int delay = 0)
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
                Debug.WriteLine($"isActive response: {content.Trim()}");
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
                    Debug.WriteLine($"SendMagicPacket error: {ex.Message}");
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

            IPAddress subnetMask = IPAddress.Parse(settings.SubnetMask);
            byte[] subnetAddrBytes = subnetMask.GetAddressBytes();
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Debug.WriteLine("Closing websocket connection");
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
        public string SubnetMask { get; set; }
        public string Token { get; set; }

        public Settings(string appName, string ipAddr, string macAddr, int port = 8002, string token = "token", string subnetMask = "255.255.255.0")
        {
            byte[] appNameBytes = System.Text.Encoding.UTF8.GetBytes(appName);
            AppName = Convert.ToBase64String(appNameBytes);
            IpAddr = ipAddr;
            MacAddr = macAddr.Replace("-", "");
            Port = port;
            SubnetMask = subnetMask;
            Token = token.Equals("token") ? String.Empty : token;
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

    public class TokenInvalidException : Exception
    {
        public TokenInvalidException()
        {
        }

        public TokenInvalidException(string message)
            : base(message)
        {
        }

        public TokenInvalidException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public static class Keys
    {
        public static string KEY_VOLDOWN { get => "KEY_VOLDOWN"; }
    }
}