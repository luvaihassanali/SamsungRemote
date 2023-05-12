using SamsungRemoteLib;
using System.Configuration;
using System.Reflection;

internal class Program
{
    private static List<string> keyList = new List<string>();
    private static void Main(string[] args)
    {
        Console.WriteLine("Demo start");
        GenerateKeylist();

        // Load previous values from configuration file
        string appName = ConfigurationManager.AppSettings["AppName"];
        string ipAddr = ConfigurationManager.AppSettings["IpAddr"];
        string macAddr = ConfigurationManager.AppSettings["MacAddr"];
        int port = int.Parse(ConfigurationManager.AppSettings["Port"]);
        string subnet = ConfigurationManager.AppSettings["Subnet"];
        string token = ConfigurationManager.AppSettings["Token"]; // On first execution token will be blank
        bool debug = bool.Parse(ConfigurationManager.AppSettings["Debug"]);

        Console.WriteLine("Initializing SamsungRemote...");
        Settings settings = new Settings(
            appName: "SamsungRemoteDemo",
            ipAddr: "192.168.1.100", // IP of TV
            subnet: "255.255.255.0", // Required if MAC address supplied
            macAddr: "00-A1-B2-C3-D4-E5", // MAC address of TV (required for TurnOn() function)
            port: 8002,
            token: token, // If token is empty string it is treated same as null
            debug: true); // Boolean to control Debug.WriteLine statements in SamsungRemote class
        Console.WriteLine($"Settings: {settings}\n");

        using (SamsungRemote remote = new SamsungRemote(settings))
        {
            /*
              - TurnOn() function sends Wake On Lan Magic Packets to TV (repeat parameter to send multiple times)
              - IsTvOn() function sends HTTP GET request to verify TV availability
            
              !!! WARNING !!!
            
              After the TV is turned off, for about 20-30 seconds, both methods do not work correctly:
              TurnOn() will do nothing (or be delayed) and IsTvOn() will still return true 
            */

            if (!remote.IsTvOn())
            {
                Console.WriteLine("Turn TV on");
                remote.TurnOn(); // Optional repeat parameter for TurnOn([int repeat = 10]) specifies how many times to send packets
            }

            int delay = 2000;
            Console.WriteLine($"Waiting {delay} seconds for TV to power up...");
            if (remote.IsTvOn(delay)) // Optional parameter for IsTvOn([int delay=0]) sets wait time in milliseconds before sending request
            {
                string msg = settings.Token == null ? "Connecting to TV... Check for prompt to accept new connection" : "Connecting to TV...";
                Console.WriteLine(msg);
                remote.Connect();
                Console.WriteLine("Pressing volume down");
                remote.Press(Keys.VOLDOWN);
            }

            Console.WriteLine($"Pressing volume up after {delay} seconds");
            Task.Delay(delay).Wait();
            remote.Press(Keys.VOLUP);

            Console.WriteLine("Enter key code e.g 'KEY_VOLDOWN' or enter multiple key codes separated by ; character e.g. 'KEY_2;KEY_PLUS100;KEY_1'\nEnter 'exit' to end program");
            while (true)
            {
                string? inputKey = Console.ReadLine();
                if (inputKey == null) continue;
                if (inputKey.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                string[] inputKeys = inputKey.Contains(';') ? inputKey.Split(";") : new string[] { inputKey };
                for (int i = 0; i < inputKeys.Length; i++)
                {
                    string key = inputKeys[i].Trim();
                    if (IsValidKeycode(key))
                    {
                        remote.Press(key);
                        Task.Delay(200).Wait(); // Need delay between sending key press or will not register later command
                    }
                }
            }

            // Turn off TV
            remote.Press(Keys.POWER);

            // After first token generation, new token value is saved in settings.Token
            // When SamsungRemote constructor is called on next launch with token that is already paired, TV will not prompt user to accept connection again
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            config.AppSettings.Settings["Token"].Value = settings.Token;
            config.Save(ConfigurationSaveMode.Minimal);
            Console.WriteLine("Demo end");
        }
    }

    private static void GenerateKeylist()
    {
        Console.WriteLine("Key list:");
        Type t = typeof(Keys);
        MemberInfo[] memberInfo = t.GetMembers();
        string[] exceptionList = "GetType ToString Equals GetHashCode 0 1 2 3 4 5 6 7 8 9 POWER POWEROFF SOURCE PLUS100 PRECH VOLUP VOLDOWN MUTE CH_LIST CHDOWN CHUP HOME W_LINK GUIDE LEFT UP RIGHT DOWN ENTER RETURN EXIT SETTINGS INFO SUB_TITLE STOP REWIND FF PLAY PAUSE".Split(" ");
        foreach (MemberInfo member in memberInfo)
        {
            string name = member.Name.Replace("get_", "KEY_");
            if (name.Contains("NUM")) name = name.Replace("NUM", "");
            if (!exceptionList.Contains(name))
            {
                keyList.Add(name);
                Console.Write($"{name} ");
            }
        }
        Console.WriteLine(Environment.NewLine);
    }

    private static bool IsValidKeycode(string inputKey)
    {
        if (keyList.Contains(inputKey)) return true;
        Console.WriteLine("Key invalid");
        return false;
    }
}