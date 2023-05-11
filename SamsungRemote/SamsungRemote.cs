using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WebSocketSharp;

namespace SamsungRemoteLib
{
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
            websocket.OnError += Websocket_OnError;
            websocket.Connect();
        }

        public void Press(string key)
        {
            if (settings.Token == null) throw new ArgumentNullException("Token is ***null*** execute Connect() before pressing keys");
            Parameters parameters = new Parameters(key);
            Command cmd = new Command(parameters);
            string data = JsonConvert.SerializeObject(cmd).Replace("parameters", "params");
            Log("Sending key: " + key);
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
                    Log("Connection established");
                };

                websocket.OnMessage += (sender, e) =>
                {
                    JObject response = JObject.Parse(e.Data);
                    Log("OnMessage data: " + e.Data.Trim());
                    string method = response?["event"]?.ToString() ?? String.Empty;
                    if (method.Equals("ms.channel.connect"))
                    {
                        string newToken = response?["data"]?["token"]?.ToString() ?? String.Empty;
                        settings.Token = newToken;
                        Log($"New token {settings.Token} generated");
                        websocketUrl += $"&token={settings.Token}";
                        tokenSource.Cancel();
                    }
                };

                websocket.OnError += (sender, e) =>
                {
                    Log("Generate new token error: " + e.Message);
                };

                websocket.Connect();
                Log("Accept dialog for new connection on TV...");
                // Allow time for OnMessage to fire before socket closes on exit of using statement
                try
                {
                    await Task.Delay(30000, tokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                }
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
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return true;
                }
                return false;
            }
        }

        public void TurnOn(int repeat = 10)
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

        private void Websocket_OnError(object? sender, WebSocketSharp.ErrorEventArgs e)
        {
            Debug.WriteLine($"WebSocket error: {e.Message}");
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
            if (token != null && token.Equals(String.Empty)) token = null;
            Token = token;
            Debug = debug;
        }

        public override string ToString()
        {
            return $@"AppName: {AppName}
IpAddr: {IpAddr}
MacAddr: {MacAddr}
Port: {Port}
Subnet: {Subnet}
Token: {Token}
Debug: {Debug}";
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
        public static string NUM0 { get => "KEY_0"; }
        public static string NUM1 { get => "KEY_1"; }
        public static string NUM2 { get => "KEY_2"; }
        public static string NUM3 { get => "KEY_3"; }
        public static string NUM4 { get => "KEY_4"; }
        public static string NUM5 { get => "KEY_5"; }
        public static string NUM6 { get => "KEY_6"; }
        public static string NUM7 { get => "KEY_7"; }
        public static string NUM8 { get => "KEY_8"; }
        public static string NUM9 { get => "KEY_9"; }
        public static string POWER { get => "KEY_POWER"; }
        public static string SOURCE { get => "KEY_SOURCE"; }
        public static string PLUS100 { get => "KEY_PLUS100"; } // DASH/MINUS "-" button
        public static string PRECH { get => "KEY_PRECH"; }
        public static string VOLUP { get => "KEY_VOLUP"; }
        public static string VOLDOWN { get => "KEY_VOLDOWN"; }
        public static string MUTE { get => "KEY_MUTE"; }
        public static string CH_LIST { get => "KEY_CH_LIST"; }
        public static string CHDOWN { get => "KEY_CHDOWN"; }
        public static string CHUP { get => "KEY_CHUP"; }
        public static string HOME { get => "KEY_HOME"; }
        public static string GUIDE { get => "KEY_GUIDE"; }
        public static string LEFT { get => "KEY_LEFT"; }
        public static string UP { get => "KEY_UP"; }
        public static string RIGHT { get => "KEY_RIGHT"; }
        public static string DOWN { get => "KEY_DOWN"; }
        public static string ENTER { get => "KEY_ENTER"; }
        public static string RETURN { get => "KEY_RETURN"; }
        public static string EXIT { get => "KEY_EXIT"; }
        public static string MENU { get => "KEY_MENU"; } // SETTINGS button
        public static string INFO { get => "KEY_INFO"; }
        public static string SUB_TITLE { get => "KEY_SUB_TITLE"; } // CC/VD button
        public static string STOP { get => "KEY_STOP"; }
        public static string REWIND { get => "KEY_REWIND"; }
        public static string FF { get => "KEY_FF"; }
        public static string PLAY { get => "KEY_PLAY"; }
        public static string PAUSE { get => "KEY_PAUSE"; }
        public static string RED { get => "KEY_RED"; }
        public static string GREEN { get => "KEY_GREEN"; }
        public static string YELLOW { get => "KEY_YELLOW"; }
        public static string CYAN { get => "KEY_CYAN"; }

    }
}