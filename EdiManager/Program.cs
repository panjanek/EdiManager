// Author: Maciej Siekierski

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Mono.Options;

namespace EdiManager
{
    public class Program
    {
        private const string InvalidNumber = "Value '{0}' not recognized as a number";

        private const string NumberOutsideRange = "Number '{0}' is outside allowed range 0-65536";

        private const string DefaultEdimaxServer = "www.myedimax.com";

        private const int DefaultEdimaxUdpPort = 8766;

        static void Main(string[] args)
        {
            string password = null;
            bool showHelp = false;
            string outputFile = null;
            bool localWebPortSet = false;
            int verbosity = 0;
            string edimaxHost = DefaultEdimaxServer;
            int edimaxPort = DefaultEdimaxUdpPort;
            string edimaxIp = null;
            var options = new OptionSet() {
                { "p|password=", "Password for Edimax device. If password is not provided user will be prompted to enter password",
                  p => {
                      password = p;
                  }
                },
                { "m|imagefile=", "Specifies filename where image downloaded from camera will be saved when executing 'image' command",
                  v => outputFile = v
                },
                { "w|webport=", "Local TCP port to use when executing 'web' command. Default port is 9999.",
                  v => { ParseIntOption(v, "w", out EdimaxControl.LocalWebPort); localWebPortSet = true; }
                },
                { "v|verbose",  "Show more status messages. More -v means more messages", 
                  v => ++verbosity  
                },
                { "c|cleartext",  "Show messages exchanged with cloud in clear text format", 
                  v => Output.OutputTextData = true
                },
                { "x|hexadecimal",  "Show messages exchanged with Edimax cloud as raw hexadecimal data", 
                  v => Output.OutputRawData = true
                },
                { "t|tcptimeout=", "Timeout for receiving TCP data from Edimax cloud [ms]. Default is 20000",
                  v => ParseIntOption(v, "t", out EdimaxControl.TcpTimeoutMillis)
                },
                { "u|udptimeout=", "Timeout for receiving UDP data from Edimax cloud [ms]. Default is 10000",
                  v => ParseIntOption(v, "u", out EdimaxControl.UdpTimeoutMillis)
                },
                { "r|tcpretries=", "Number of retries after TCP connection breaks. Defauilt is 10",
                  v => ParseIntOption(v, "r", out EdimaxControl.TcpMaxRetries)
                },
                { "R|udpretries=", "Number of retries after TCP connection breaks. Default is 10",
                  v => ParseIntOption(v, "R", out EdimaxControl.UdpMaxRetries)
                },
                { "i|interval=", "Time interval between retrying connections [ms]. Default is 500",
                  v => ParseIntOption(v, "i", out EdimaxControl.IntervalMillis)
                },
                { "e|endpoint=",  "Cloud UDP endpoint address used for used for initiating connection to Edimax cloud. Default endpoint is www.myedimax.com:8766", 
                  v => {
                      if (v!=null) {
                          string[] split = v.Split(':');
                          IPAddress addr = null;
                          if (IPAddress.TryParse(split[0], out addr))                          
                              edimaxIp = addr.ToString();                          
                          else
                              edimaxHost = split[0];                              
                          if (split.Length > 1)
                          {
                              if (!int.TryParse(split[1], out edimaxPort))
                                  throw new OptionException(string.Format(InvalidNumber, split[1]), "e");
                          }
                      }
                  }
                },
                { "h|help",  "Show this message and exit", 
                  v => showHelp = v != null 
                },
            };

            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("Edimax.exe: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try 'Edimax.exe --help' for more information.");
                return;
            }

            if (showHelp || extra.Count == 0 || extra.Count > 2)
            {
                Console.WriteLine("EdiManager by Maciej Siekierski    https://github.com/panjanek/EdiManager.git");
                Console.WriteLine("Sends commands and receives data from Edimax WiFi plugs and IP cameras using");
                Console.WriteLine("Edimax cloud service. Using EdiManager does not require being at the same network");
                Console.WriteLine("as the device nor to configure router port forwarding. Edimax plugs and cameras");
                Console.WriteLine("maintain connection to the cloud servers using undocummented protocol. EdiManager");
                Console.WriteLine("uses this protocol to control Edimax devices using device ID and password only.");  
                Console.WriteLine("Usage:");
                Console.WriteLine();
                Console.WriteLine("           EdiManager.exe DeviceId [Command] [Options]");
                Console.WriteLine();
                Console.WriteLine("DeviceId : Device Identifier, typically MAC address printed on sticker");
                Console.WriteLine("Command  : Commmand executed on device. If no command is provided, default");
                Console.WriteLine("           'probe' command will be used. EdiPlug commands:");
                Console.WriteLine("               on            Switch on EdiPlug device\n" +
                                  "               off           Switch off EdiPlug device\n" +
                                  "               toggle        Toggle EdiPlug\n" +
                                  "               pluginfo      Output device info\n" +
                                  "               getschedule   Get schedule\n" +
                                  "               power         Get current power consumption\n" +
                                  "               history       Get power consumption history\n"+
                                  "           Edimax IP camera commands:\n"+
                                  "               image         Get camera snapshot and save to jpg file. Name of\n"+
                                  "                             the file can be specified with -m. Otherwise\n"+
                                  "                             default name name will be used:\n"+
                                  "                             <DeviceId>_<DateTime>.jpg\n"+
                                  "               web           Setup HTTP proxy to access camera web interface\n"+
                                  "                             through local TCP port. As a default web interface\n"+
                                  "                             will be available at localhost:9999.\n"+
                                  "                             Other port can be stecified with -w\n" +
                                  "           Commands appllicable for any device:\n"+
                                  "               probe         Perform UDP probing only (checks if the device\n"+
                                  "                             is online)");
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                Console.WriteLine();
                Console.WriteLine("Edimanager works with EdiPlugs SP1101W and SP2101W and cameras IC-3116W,");
                Console.WriteLine("IC-3140W, IC-7001W but should work with other Edimax camera models as well.");
                Console.WriteLine();
                Console.WriteLine("Switching EdiPlug example:");
                Console.WriteLine("        EdiManager.exe 74DA38XXXXXX toggle -p 1234");
                Console.WriteLine("Downloading camera snapshot example:");
                Console.WriteLine("        EdiManager.exe 74DA38XXXXXX image -p 1234 -m snapshot.jpg");
                return;
            }

            Output.VerbosityLevel = verbosity;
            string deviceId = extra[0].ToUpper();
            EdimaxCommand command = EdimaxCommand.Probe;
            if (extra.Count == 2)
            {
                if (!Enum.TryParse<EdimaxCommand>(extra[1], true, out command))
                {
                    Output.Error("Value '{0}' not recognized as valid command. Allowed commands: {1}", extra[1], string.Join(",", Enum.GetNames(typeof(EdimaxCommand)).Select(n => n.ToLower())));
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(edimaxIp))
            {
                Output.Log(2, "Resolving host {0} IP address", edimaxHost);
                try
                {
                    edimaxIp = Dns.GetHostAddresses(edimaxHost).Select(adr => adr.ToString()).FirstOrDefault();
                }
                catch (Exception x)
                {
                    Output.Error("Error resolving host {0} address: {1}", edimaxHost, x.Message);
                    return;
                }
            }

            Output.Log(1, "Using Edimax UDP cloud service at {0}:{1}", edimaxIp, edimaxPort);  
            if (string.IsNullOrWhiteSpace(password))
            {
                Console.Write("Enter password for device {0}:", deviceId);
                password = ConsoleReadPassword();
                Console.WriteLine();
            }

            if (!string.IsNullOrWhiteSpace(outputFile) && command != EdimaxCommand.Image)
            {
                Output.Log(1, "ignoring -m option");
            }

            if (localWebPortSet && command != EdimaxCommand.Web)
            {
                Output.Log(1, "ignoring -w option");
            }

            var ediControl = new EdimaxControl(edimaxIp, edimaxPort);
            var response = ediControl.EdimaxCloudRequest(deviceId, password, command, outputFile);           
            return;
        }

        private static void ParseIntOption(string value, string optionName, out int result)
        {
            int parsed = 0;
            if (!int.TryParse(value, out parsed))
            {
                throw new OptionException(string.Format(InvalidNumber, value), optionName);
            }
            else
            {
                if (parsed >=0 && parsed <= 65536)
                {
                    result = parsed;
                }
                else
                {
                    throw new OptionException(string.Format(NumberOutsideRange, value), optionName);
                }
            }
        }

        private static string ConsoleReadPassword()
        {
            string pwd = "";
            while (true)
            {
                ConsoleKeyInfo i = Console.ReadKey(true);
                if (i.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (i.Key == ConsoleKey.Backspace)
                {
                    if (pwd.Length > 0)
                    {
                        pwd = pwd.Substring(0, pwd.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else
                {
                    pwd += i.KeyChar;
                    Console.Write("*");
                }
            }
            return pwd;
        }
    }
}
