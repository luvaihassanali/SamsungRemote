# SamsungRemote
# Overview
  
SamsungRemote is a .NET library used to communicate with Samsung TV over Wi-Fi. WebSocket client is used to maintain connection to TV which is implemented using [websocket-sharp](https://github.com/sta/websocket-sharp).
## Features
- Compatible with authentication protocol defined here: [Samsung TV network remote control protocol by sc0ty](http://sc0ty.pl/2012/02/samsung-tv-network-remote-control-protocol/)
- Turn on TV by sending Wake-On-Lan Magic Packet using MAC address
- Tested with Samsung 2019 UN50RU7100FXZC (2019)

# Usage
See [SamsungRemoteDemo](https://github.com/luvaihassanali/SamsungRemote/blob/master/SamsungRemoteDemo/Program.cs) for more detailed example
```
Settings settings = new Settings(
    appName: "SamsungRemoteDemo", // converted to base64 string as ID for TV
    ipAddr: "192.168.1.100", // IP of TV
    subnet: "255.255.255.255", // Subnet (required for TurnOn() function)
    macAddr: "00-A1-B2-C3-D4-E5", // MAC address of TV (required for TurnOn() function)
    port: 8002, // Port for WebSocket communication
    token: null, // Authorization token
    debug: true); // Control Debug.WriteLine statements in SamsungRemote class
    
using (SamsungRemote remote = new SamsungRemote(settings))
{
    remote.TurnOn();
    if (remote.IsTvOn(2000))
    {
        // Since token is null Connect() will call GenerateNewToken() method
        // Token being null will show dialog on TV for user to accept new connection
        remote.Connect();
        remote.Press(Keys.VOLDOWN);
        Task.Delay(200).Wait() // Delay is required between sending two keys
        remote.Press(Keys.VOLDOWN);
    }
}
// GenerateNewToken() will update settings.Token with new token value 
// Saved token is used on next initialization so TV will not show new connection dialog 
```

# How to get MAC address
To be able to turn on TV, the MAC address (and subnet) is required to be set. These are some options to determine what it is:
1. The MAC address may be displayed in Network Settings of TV
2. Login to router home page (e.g. 192.168.1.1) and check connected client information
3. Use Wireshark/Fiddler to capture packets sent by existing mobile apps with power on functionality ([myTifi](https://apps.apple.com/us/app/mytifi-remote-for-samsung-tv/id441912305) worked in my case)

# Notes
:warning: TurnOn() and IsTvOn() functions will fail if called within 20-30 seconds of turning TV off :warning:
- During testing it was observed if TurnOn() or IsTvOn() were called within 20-30 seconds of TV being turned off, then TurnOn() would do nothing or produce delayed power on, and IsTvOn() would return true
- A delay is required in between sending keys repeadeatly (200ms is good, not tested with a lower interval)
- MAC address and subnet can be omitted but exception will be thrown if TurnOn() function is called
- Currently WebSocketSharp async methods use BeginInvoke which is not supported for .NET Core https://github.com/sta/websocket-sharp/pull/712 (wrap methods in await Task.Run(() => { Function(); }) as a workaround)
- If acceptance prompt is ignored with a supplied token, commands will work but prompt will display again on next connect, until accepted

# References
- [Toxblh/samsung-tv-control](https://github.com/Toxblh/samsung-tv-control)
- [Bntdumas/SamsungIPRemote](https://github.com/Bntdumas/SamsungIPRemote) 
- [jakubpas/samsungctl](https://github.com/jakubpas/samsungctl)
