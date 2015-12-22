# EdiManager
Command line utility for controlling Edimax plugs and cameras remotely through Edimax cloud service.

# How it works
Sends commands and receives data from Edimax WiFi plugs and IP cameras using Edimax cloud service. Using EdiManager does not require being in the same network as the device nor configuring router port forwarding. Edimax plugs and cameras maintain connection to the cloud servers using undocummented protocol. EdiManager uses this protocol to control Edimax devices. Device is identified by Device ID (MAC address on a sticker). Access is authenticated with password.

# Usage
```
           EdiManager.exe DeviceId [Command] [Options]

DeviceId : Device Identifier, typically MAC address printed on sticker
Command  : Commmand executed on device. If no command is provided, default
           'probe' command will be used. EdiPlug commands:
               on            Switch on EdiPlug device
               off           Switch off EdiPlug device
               toggle        Toggle EdiPlug
               pluginfo      Output device info
               getschedule   Get schedule
               power         Get current power consumption
               history       Get power consumption history
           Edimax IP camera commands:
               image         Get camera snapshot and save to jpg file. Name of
                             the file can be specified with -m. Otherwise
                             default name name will be used:
                             <DeviceId>_<DateTime>.jpg
               web           Setup HTTP proxy to access camera web interface
                             through local TCP port. As a default web interface
                             will be available at localhost:9999.
                             Other port can be specified with -w
           Commands applicable for any device:
               probe         Perform UDP probing only (checks if the device
                             is online)
Options:
  -p, --password=VALUE       Password for Edimax device. If password is not
                               provided user will be prompted to enter password
  -m, --imagefile=VALUE      Specifies filename where image downloaded from
                               camera will be saved when executing 'image'
                               command
  -w, --webport=VALUE        Local TCP port to use when executing 'web' command.
                                Default port is 9999.
  -v, --verbose              Show more status messages. More -v means more
                               messages
  -c, --cleartext            Show messages exchanged with cloud in clear text
                               format
  -x, --hexadecimal          Show messages exchanged with Edimax cloud as raw
                               hexadecimal data
  -t, --tcptimeout=VALUE     Timeout for receiving TCP data from Edimax cloud [
                               ms]. Default is 20000
  -u, --udptimeout=VALUE     Timeout for receiving UDP data from Edimax cloud [
                               ms]. Default is 10000
  -r, --tcpretries=VALUE     Number of retries after TCP connection breaks.
                               Defauilt is 10
  -R, --udpretries=VALUE     Number of retries after TCP connection breaks.
                               Default is 10
  -i, --interval=VALUE       Time interval between retrying connections [ms].
                               Default is 500
  -e, --endpoint=VALUE       Cloud UDP endpoint address used for used for
                               initiating connection to Edimax cloud. Default
                               endpoint is www.myedimax.com:8766
  -h, --help                 Show this message and exit

Edimanager works with EdiPlugs SP1101W and SP2101W and cameras IC-3116W,
IC-3140W, IC-7001W but should work with other Edimax camera models as well.

Switching EdiPlug example:
        EdiManager.exe 74DA38XXXXXX toggle -p 1234
Downloading camera snapshot example:
        EdiManager.exe 74DA38XXXXXX image -p 1234 -m snapshot.jpg
```


# Notes

- Works with Mono
- Hints form [here](http://blog.guntram.de/?p=37) used for deobfuscation code