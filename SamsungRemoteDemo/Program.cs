using SamsungRemoteLib;
using System.Configuration;
using static System.Net.Mime.MediaTypeNames;

internal class Program
{

    private static void Main(string[] args)
    {
        Console.WriteLine("Demo start");

        string appName = ConfigurationManager.AppSettings["AppName"];
        string ipAddr = ConfigurationManager.AppSettings["IpAddr"];
        string macAddr = ConfigurationManager.AppSettings["MacAddr"];
        int port = Int32.Parse(ConfigurationManager.AppSettings["Port"]);
        string subnet = ConfigurationManager.AppSettings["Subnet"];
        string? token = ConfigurationManager.AppSettings["Token"];
        bool debug = bool.Parse(ConfigurationManager.AppSettings["IpAddr"]);

        Settings settings = new Settings(
            appName: "SamsungRemoteDemo", 
            ipAddr: "***REMOVED***", 
            subnet: "255.255.255.0", 
            macAddr: "***REMOVED***",
            port: 8002,
            token: null,
            debug: true);

        SamsungRemote remote = new SamsungRemote(settings);

        // Sends a HTTP GET request to TV to verify availability 
        // !!! WARNING !!!
        // The IsTvOn() method will still return true untill after about 30 seconds since the TV is turned off
        if (!remote.IsTvOn())
        {
            // TurnOn() function sends Wake On Lan Magic Packets to TV (can use optional repeat parameter to send multiple times)
            remote.TurnOn();
        }

        /* 
        // Alternatively IsTvOn method can take delay parameter (IsTvOnAsync method available)
        remote.TurnOn();
        if (remote.IsTvOn(1000))
        {
            remote.Press(Keys.KEY_VOLDOWN); // This would throw an exception since not Connect() not called and token == null
        }
        */
        
        // If token == null, prompt will be generated on TV to accept connection
        remote.Connect();
        remote.Press(Keys.KEY_VOLDOWN);

        // After first launch, is important to save new token that settings variable is updated with
        // When SamsungRemote constructor is created again with token that is already paired, TV will not prompt user to accept connection
        Configuration config = ConfigurationManager.OpenExeConfiguration(AppDomain.CurrentDomain.BaseDirectory);
        config.AppSettings.Settings["Token"].Value = settings.Token;
        config.Save(ConfigurationSaveMode.Minimal);

        Console.WriteLine("Demo end");
    }
}