// Author: Maciej Siekierski

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EdiManager
{
    public class EdimaxControl
    {
        public static int UdpTimeoutMillis = 10000;

        public static int TcpTimeoutMillis = 20000;

        public static int LocalWebPort = 9999;

        public static int TcpMaxRetries = 10;

        public static int UdpMaxRetries = 10;

        public static int IntervalMillis = 500;

        private static string[] Weekdays = new string[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };

        private static byte[] DoubleCRLF = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };

        private const string UdpRequestCmd = "<param><code value=\"1030\" /><id value=\"{0}\" /><lanip value=\"192.168.1.2\" /><lanport value=\"36587\" /><nattype value=\"7\" /><reqdirport value=\"0\" /><reqfwver value=\"1.0#010000\" /><auth value=\"{1}\" /><seq value=\"{0}{2}\" /></param>";

        private const string PlugInfoCmd = "<?xml version=\"1.0\" encoding=\"UTF8\"?><SMARTPLUG id=\"edimax\"><CMD id=\"get\"><SYSTEM_INFO></SYSTEM_INFO><Device.System.Power.State></Device.System.Power.State><Device.System.Power.NextToggle></Device.System.Power.NextToggle></CMD></SMARTPLUG>";

        private const string SetPlugStateCmd = "<?xml version=\"1.0\" encoding=\"UTF8\"?><SMARTPLUG id=\"edimax\"><CMD id=\"setup\"><Device.System.Power.State>{0}</Device.System.Power.State></CMD></SMARTPLUG>";

        private const string GetScheduleCmd = "<?xml version=\"1.0\" encoding=\"UTF8\"?><SMARTPLUG id=\"edimax\"><CMD id=\"get\"><SCHEDULE></SCHEDULE></CMD></SMARTPLUG>";

        private const string GetPowerCmd = "<?xml version=\"1.0\" encoding=\"UTF8\"?><SMARTPLUG id=\"edimax\"><CMD id=\"get\"><NOW_POWER></NOW_POWER></CMD></SMARTPLUG>";

        private const string GetHistoryCmd = "<?xml version=\"1.0\" encoding=\"UTF8\"?><SMARTPLUG id=\"edimax\"><CMD id=\"get\"><NOW_POWER></NOW_POWER><POWER_HISTORY><Device.System.Power.History.Energy unit=\"HOUR\" date=\"{0}-{1}\"></Device.System.Power.History.Energy></POWER_HISTORY></CMD></SMARTPLUG>";

        private const string RequestHttpUrlCmd = "<param><code value=\"1080\" /><media value=\"4\" /></param><param><code value=\"1100\" /><url value=\"{0}\" /><auth value=\"{1}\" /></param>";

        private string EdimaxUdpIp = null;

        private int EdimaxUdpPort = 8766;

        public EdimaxControl(string udpIp, int udpPort)
        {
            this.EdimaxUdpIp = udpIp;
            this.EdimaxUdpPort = udpPort;
        }
        
        public EdimaxResult EdimaxCloudRequest(string deviceId, string password, EdimaxCommand disposition = EdimaxCommand.Probe, string outputFile = null)
        {
            EdimaxResult result = new EdimaxResult() { DeviceId = deviceId };
            result.UdpResult = QueryUdp(deviceId, password);
            if (result.UdpResult.Success)
            {
                if (!string.IsNullOrWhiteSpace(result.UdpResult.TcpRelayId))
                {
                    if (disposition == EdimaxCommand.Probe)
                    {
                        return result;
                    }

                    Thread.Sleep(300);
                    try
                    {
                        TcpClient tcp = ConnectToRelay(deviceId, result.UdpResult);
                        if (disposition == EdimaxCommand.PlugInfo || disposition == EdimaxCommand.GetSchedule || disposition == EdimaxCommand.On || disposition == EdimaxCommand.Off || disposition == EdimaxCommand.Toggle || disposition == EdimaxCommand.Power || disposition == EdimaxCommand.History)
                        {
                            string cmd = string.Format(PlugInfoCmd);
                            Output.Log(1, "Quering device {0} through TCP {1}:{2}, relayId={3}", deviceId, result.UdpResult.TcpRelayIp, result.UdpResult.TcpRelayPort, result.UdpResult.TcpRelayId);
                            string response = TcpSendAndReceivePnv(tcp, CreateCommandMessage(cmd));
                            if (response != null)
                            {
                                result.TcpSuccess = true;
                                var respDoc = EdimaxUtils.GetDocumentFromHttpLikeString(response);
                                if (respDoc != null)
                                {
                                    result.State = respDoc.Descendants("Device.System.Power.State").Select(n => n.Value).FirstOrDefault();
                                    result.NextToggle = respDoc.Descendants("Device.System.Power.NextToggle").Select(n => n.Value).FirstOrDefault();
                                    result.EmailRecipient = respDoc.Descendants("Device.System.SMTP.0.Mail.Sender").Select(n => n.Value).FirstOrDefault();
                                    result.EmailSender = respDoc.Descendants("Device.System.SMTP.0.Mail.Recipient").Select(n => n.Value).FirstOrDefault();
                                    result.RunCus = respDoc.Descendants("Run.Cus").Select(n => n.Value).FirstOrDefault();
                                    result.RunModel = respDoc.Descendants("Run.Model").Select(n => n.Value).FirstOrDefault();
                                    result.RunFW = respDoc.Descendants("Run.FW.Version").Select(n => n.Value).FirstOrDefault();
                                    Output.Info("Info received from device {0}:", deviceId);
                                    Output.Info("    State           : {0}", result.State);
                                    Output.Info("    NextToggle      : {0}", result.NextToggle);
                                    Output.Info("    RunCus          : {0}", result.RunCus);
                                    Output.Info("    RunModel        : {0}", result.RunModel);
                                    Output.Info("    RunFW           : {0}", result.RunFW);
                                    Output.Info("    EmailRecipient  : {0}", result.EmailRecipient);
                                    Output.Info("    EmailSender     : {0}", result.EmailSender);
                                }

                                if (disposition == EdimaxCommand.On || disposition == EdimaxCommand.Off || disposition == EdimaxCommand.Toggle)
                                {
                                    string state = null;
                                    if (disposition == EdimaxCommand.On)
                                    {
                                        state = "ON";
                                    }
                                    else if (disposition == EdimaxCommand.Off)
                                    {
                                        state = "OFF";
                                    }
                                    else if (disposition == EdimaxCommand.Toggle)
                                    {
                                        state = result.State == "ON" ? "OFF" : "ON";
                                    }

                                    Output.Log(1, "Sending command Device.System.Power.State={0}", state);
                                    string command = string.Format(SetPlugStateCmd, state);
                                    string cmdResponse = TcpSendAndReceivePnv(tcp, CreateCommandMessage(command));
                                    var cmdRespDoc = EdimaxUtils.GetDocumentFromHttpLikeString(cmdResponse);
                                    if (cmdRespDoc != null)
                                    {
                                        var cmdStatus = cmdRespDoc.Descendants("CMD").Select(v => v.Value).FirstOrDefault();
                                        Output.Log(1, "Command: {0} returned status {1}", disposition, cmdStatus);
                                    }
                                }
                                else if (disposition == EdimaxCommand.GetSchedule)
                                {
                                    Output.Log(1, "Sending {0} command", disposition);
                                    string command = string.Format(GetScheduleCmd);
                                    string cmdResponse = TcpSendAndReceivePnv(tcp, CreateCommandMessage(command));
                                    var cmdRespDoc = EdimaxUtils.GetDocumentFromHttpLikeString(cmdResponse);
                                    result.Schedule = new string[7];
                                    for (int i = 0; i < 7; i++)
                                    {
                                        result.Schedule[i] = cmdRespDoc.Root.Descendants(string.Format("Device.System.Power.Schedule.{0}", i)).Select(n => n.Value).FirstOrDefault();
                                    }

                                    result.ScheduleExplanation = string.Join("/", result.Schedule.Select((s, i) => string.Format("{0}:{1}", Weekdays[i], EdimaxUtils.ExplainSchedule(s))));
                                    Output.Info("Schedule for {0}:", deviceId);
                                    Output.Info("        " + result.ScheduleExplanation.Replace("/", "\r\n        "));
                                }
                                else if (disposition == EdimaxCommand.Power)
                                {
                                    Output.Log(1, "Sending {0} command", disposition);
                                    string command = string.Format(GetPowerCmd);
                                    string cmdResponse = TcpSendAndReceivePnv(tcp, CreateCommandMessage(command));
                                    var cmdRespDoc = EdimaxUtils.GetDocumentFromHttpLikeString(cmdResponse);
                                    result.Power = cmdRespDoc.Root.Descendants("Device.System.Power.NowPower").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.Current = cmdRespDoc.Root.Descendants("Device.System.Power.NowCurrent").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyDay = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Day").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyWeek = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Week").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyMonth = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Month").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    Output.Info("    Current        : {0} [A]", result.Current.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    Power          : {0} [W]", result.Power.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyDay      : {0} [Wh]", result.EnergyDay.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyWeek     : {0} [Wh]", result.EnergyWeek.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyMonth    : {0} [Wh]", result.EnergyMonth.ToString(CultureInfo.InvariantCulture));
                                }
                                else if (disposition == EdimaxCommand.History)
                                {
                                    string from = DateTime.Now.AddDays(-14).ToString("yyyyMMdd00");
                                    string to = DateTime.Now.ToString("yyyyMMddHH");
                                    Output.Log(1, "Sending GetPowerHistory command for {0}-{1}", from, to);
                                    string command = string.Format(GetHistoryCmd, from, to);
                                    string cmdResponse = TcpSendAndReceivePnv(tcp, CreateCommandMessage(command));
                                    var cmdRespDoc = EdimaxUtils.GetDocumentFromHttpLikeString(cmdResponse);
                                    string history = cmdRespDoc.Root.Descendants("Device.System.Power.History.Energy").Select(n => n.Value).FirstOrDefault();
                                    result.EnergyHistory = history.Split('-').Select((v, i) => new { i = i, v = v }).GroupBy(g => g.i / 24).Select(g => string.Join("-", g.Select(v => v.v))).ToArray();
                                    result.Power = cmdRespDoc.Root.Descendants("Device.System.Power.NowPower").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.Current = cmdRespDoc.Root.Descendants("Device.System.Power.NowCurrent").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyDay = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Day").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyWeek = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Week").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    result.EnergyMonth = cmdRespDoc.Root.Descendants("Device.System.Power.NowEnergy.Month").Select(n => double.Parse(n.Value, CultureInfo.InvariantCulture)).FirstOrDefault();
                                    Output.Info("    Current        : {0} [A]", result.Current.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    Power          : {0} [W]", result.Power.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyDay      : {0} [Wh]", result.EnergyDay.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyWeek     : {0} [Wh]", result.EnergyWeek.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    EnergyMonth    : {0} [Wh]", result.EnergyMonth.ToString(CultureInfo.InvariantCulture));
                                    Output.Info("    Power history  : from {0} to {1}:\r\n        {2}", from, to, string.Join("\r\n        ", result.EnergyHistory));
                                }
                            }
                        }
                        else if (disposition == EdimaxCommand.Image || disposition == EdimaxCommand.Web)
                        {
                            string headers = null;
                            byte[] body = null;
                            string httpStatus = null;
                            string contentType = null;
                            string auth64 = null;
                            Output.Log(2, "Testing access");
                            auth64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Format("admin:{0}", password)));
                            string getMainPageCmd = string.Format(RequestHttpUrlCmd, "/", auth64);
                            TcpSendAndReceiveHttpWithReconnect(ref tcp, ref result, password, getMainPageCmd, out headers, out body, out httpStatus, out contentType);
                            result.UdpResult.Password = null;
                            if (httpStatus == "200" || httpStatus == "301" || httpStatus == "302")
                            {
                                result.UdpResult.Password = password;
                                Output.Log(2, "Access to {0} granted", deviceId);
                            }
                            else
                            {
                                Output.Error("Access to {0} is forbiden. HttpStatus:{1} - Access denied", deviceId, httpStatus);
                                return result;
                            }

                            if (disposition == EdimaxCommand.Image)
                            {
                                string cmd = string.Format(RequestHttpUrlCmd, "/mobile.jpg", auth64);
                                Output.Log(1, "Sending image request to {0}", deviceId);
                                TcpSendAndReceiveHttp(tcp, cmd, out headers, out body, out httpStatus, out contentType);
                                if (httpStatus == "200" && contentType.Contains("image/"))
                                {                                 
                                    result.FileGenerated = string.IsNullOrWhiteSpace(outputFile) ? string.Format("{0}_{1}.jpg", deviceId, DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss")) : outputFile;
                                    File.WriteAllBytes(result.FileGenerated, body);
                                    Output.Info("{0}b saved to image file {1}", body.Length, result.FileGenerated);
                                }
                                else if (contentType.Contains("text/"))
                                {
                                    Output.Error("Image not found in response. Body:\r\n    {0}", Encoding.UTF8.GetString(body).Replace("\n", "\n    "));
                                }
                            }
                            else if (disposition == EdimaxCommand.Web)
                            {
                                TcpListener listener = new TcpListener(LocalWebPort);
                                listener.Start();
                                Output.Info("Waiting for connection on local port {0}", LocalWebPort);
                                while (true)
                                {
                                    try
                                    {
                                        TcpClient conn = listener.AcceptTcpClient();
                                        Output.Log(2, "New Web connection accepted");
                                        var stream = conn.GetStream();
                                        byte[] headerBytes = EdimaxUtils.ReadBytes(stream, DoubleCRLF);
                                        string headerStr = Encoding.UTF8.GetString(headerBytes);
                                        string requestLine = headerStr.Split('\r')[0];
                                        string method = requestLine.Split(' ')[0];
                                        string url = requestLine.Split(' ')[1];
                                        if (method == "POST")
                                        {
                                            int reqLen = int.Parse(headerStr.Split('\n').Select(h => h.Trim('\r')).Where(h => h.StartsWith("Content-Length:")).FirstOrDefault().Split(':').LastOrDefault().Trim(' '));
                                            byte[] bodyBytes = EdimaxUtils.ReadBytes(stream, reqLen);
                                            string postParams = Encoding.UTF8.GetString(bodyBytes);
                                            url = url + "?" + postParams;
                                        }

                                        Output.Log(1, "Requesting {0} from device {1}", url, deviceId);
                                        if (url == "/snapshot.cgi")
                                            url = "/mobile.jpg";
                                        string urlCmd = string.Format(RequestHttpUrlCmd, url, auth64);
                                        TcpSendAndReceiveHttpWithReconnect(ref tcp, ref result, result.UdpResult.Password, urlCmd, out headers, out body, out httpStatus, out contentType);
                                        if (url.Contains(".js") || url.Contains(".css"))
                                        {
                                            string crlf2 = Encoding.UTF8.GetString(DoubleCRLF);
                                            string cacheHeaders = string.Format("Cache-Control: public, max-age=86400");
                                            headers = headers.Replace(crlf2, "\r\n" + cacheHeaders + crlf2);
                                        }

                                        stream.Write(Encoding.UTF8.GetBytes(headers), 0, headers.Length);
                                        stream.Write(body, 0, body.Length);
                                        conn.Close();
                                    }
                                    catch (Exception x)
                                    {
                                        Output.Error("Error on web request: {0}", x.Message);
                                    }
                                }
                            }
                        }

                        tcp.Close();
                    }
                    catch (Exception x)
                    {
                        Output.Error("TCP communication error: {0}", x.Message);
                    }
                }
                else
                {
                    Output.Error("Unable to connect to device: {0}, code={1} - {2}", deviceId, result.UdpResult.Status, EdimaxUtils.GetCodeDescription(result.UdpResult.Status));
                }
            }

            return result;
        }

        private TcpClient ConnectToRelay(string deviceId, UdpResponse udpResult)
        {
            Output.Log(2, "Connecting to relay at {0}:{1}", udpResult.TcpRelayIp, udpResult.TcpRelayPort);
            TcpClient tcp = new TcpClient(udpResult.TcpRelayIp, udpResult.TcpRelayPort);
            tcp.ReceiveTimeout = TcpTimeoutMillis;
            string handshake = string.Format("<r&{0}&{1}\r\n\r\n", deviceId, udpResult.TcpRelayId);
            Output.Data(">>", handshake);
            byte[] handshakeBytes = EdimaxUtils.Encrypt(handshake, 7);
            var stream = tcp.GetStream();
            stream.Write(handshakeBytes, 0, handshakeBytes.Length);
            Output.Data(">>", handshakeBytes);
            Output.Log(2, "Relay connection to {0} ready", deviceId);
            return tcp;
        }

        private string TcpSendAndReceivePnv(TcpClient tcp, byte[] request)
        {
            try
            {
                var stream = tcp.GetStream();
                Output.Log(3, "Sending {0}b of data to TCP relay", request.Length);
                Output.Data(">>", request);
                stream.Write(request, 0, request.Length);
                var pnvHeaderBin = EdimaxUtils.ReadBytes(stream, DoubleCRLF);
                var pnvHeaderStr = Encoding.UTF8.GetString(pnvHeaderBin);
                var lenStr = pnvHeaderStr.Split('r')[0].Split(':')[1].Trim();
                var len = int.Parse(lenStr);
                Output.Log(3, "Received header: {0}", pnvHeaderStr.Replace("\r\n\r\n", ""));
                var pnvResponseBin = EdimaxUtils.ReadBytes(stream, len);
                int r;
                var decrypted = EdimaxUtils.Decrypt(pnvResponseBin, out r);
                Output.Data("<<", pnvHeaderStr + decrypted);
                return decrypted;
            }
            catch (Exception tcpEx)
            {
                Output.Error("TCP communication error: {0}", tcpEx.Message);
                return null;
            }
        }

        private void TcpSendAndReceiveHttpWithReconnect(ref TcpClient tcp, ref EdimaxResult result, string password, string cmd, out string headers, out byte[] body, out string httpStatus, out string contentType)
        {
            headers = null;
            body = null;
            httpStatus = null;
            contentType = null;
            bool requestCompleted = false;
            int attempts = 1;
            do
            {
                try
                {
                    TcpSendAndReceiveHttp(tcp, cmd, out headers, out body, out httpStatus, out contentType);
                    requestCompleted = true;
                }
                catch (Exception tcpEx)
                {
                    Output.Log(2, "Relay communication error: {0}\n. Reconnecting... (attempt {1})", tcpEx.Message, attempts);
                    Thread.Sleep(IntervalMillis);
                    try
                    {
                        result.UdpResult = QueryUdp(result.DeviceId, password);
                        if (result.UdpResult.Success)
                        {
                            tcp = ConnectToRelay(result.DeviceId, result.UdpResult);
                        }

                        Output.Log(2, "Reconnected.");
                    }
                    catch (Exception reconnEx)
                    {
                        attempts++;
                        Output.Log(2, "Error reconnecting: " + reconnEx.Message);
                        if (attempts > TcpMaxRetries)
                            throw new Exception(string.Format("TCP Connection to cloud broken. {0} reconnect attempts failed", attempts));
                    }
                }
            } while (!requestCompleted);
        }

        private void TcpSendAndReceiveHttp(TcpClient tcp, string cmd, out string headers, out byte[] body, out string httpStatus, out string contentType)
        {
            headers = null;
            body = null;
            httpStatus = null;
            contentType = null;
            Output.Log(2, "Sending command request to {0}", tcp.Client.RemoteEndPoint);
            var stream = tcp.GetStream();
            byte[] cmdBytes = CreateCommandMessage(cmd);
            Output.Data(">>", cmdBytes);
            stream.Write(cmdBytes, 0, cmdBytes.Length);
            Output.Log(2, "Waiting for binary header");
            byte[] binPre = EdimaxUtils.ReadBytes(stream, 12);
            int httpLen = binPre[0] + (binPre[1] << 8) + (binPre[2] << 16) + (binPre[3] << 24);
            Output.Log(2, "According to binary header {0}b of HTTP response will follow", httpLen);
            byte[] httpResponse = EdimaxUtils.ReadBytes(stream, httpLen);
            string httpStr = Encoding.UTF8.GetString(httpResponse);
            int bodyIdx = httpStr.IndexOf("\r\n\r\n") + 4;
            headers = httpStr.Substring(0, bodyIdx);
            body = httpResponse.Skip(bodyIdx).ToArray();
            httpStatus = headers.Split(' ')[1];
            string contentTypeHeader = headers.Split('\n').Select(h => h.Trim('\r')).Where(h => h.StartsWith("Content-Type:")).FirstOrDefault();
            contentType = contentTypeHeader == null ? null : contentTypeHeader.Split(':').LastOrDefault().Trim(' ');
            Output.Data("<<", (contentType.Contains("text") || contentType.Contains("json") || contentType.Contains("xml")) ? httpStr : (headers + "<non-text-data>"));
            Output.Log(1, "Complete HTTP response received with status {0}, contentType: {1}, body size:{2}.", httpStatus, contentType, body.Length);
            Output.Log(3, "Headers:\r\n    {0}", headers.Replace("\r\n", "\r\n    "));
        }

        private byte[] CreateCommandMessage(string command)
        {
            List<byte> cmdMessage = new List<byte>();
            string header = string.Format("PnvDataLen: {0}\r\n\r\n", command.Length);
            Output.Data(">>", header + command);
            cmdMessage.AddRange(Encoding.UTF8.GetBytes(header));
            cmdMessage.AddRange(EdimaxUtils.Encrypt(command, 7));
            return cmdMessage.ToArray();
        }

        private UdpResponse QueryUdp(string deviceId, string password)
        {
            try
            {
                var udp = CreateUdpSocket();
                XDocument doc = null;
                UdpResponse result = null;
                Output.Log(2, "Probing device {0} by UDP", deviceId);
                string udpResponse = null;
                int attempt = 0;
                do
                {
                    try
                    {
                        udpResponse = UdpSendAndReceive(udp, new IPEndPoint(IPAddress.Parse(EdimaxUdpIp), EdimaxUdpPort), deviceId, password);
                    }
                    catch (Exception udpExp)
                    {
                        attempt++;
                        if (attempt > UdpMaxRetries)
                            throw new Exception(string.Format("UDP connection to cloud broken. {0} attempts failed.", attempt));
                        Output.Log(2, "UDP connection to cloud broken: {0}. Reconnecting... (attempt {1})", udpExp.Message, attempt);
                        Thread.Sleep(IntervalMillis);
                        udp = CreateUdpSocket();
                    }
                }
                while (udpResponse == null);
                doc = XDocument.Parse(udpResponse);
                result = new UdpResponse();
                result.Success = true;
                result.Status = EdimaxUtils.GetXmlValue(doc, "code");
                Output.Log(2, "Received UDP response for device {0}. Status is {1}", deviceId, result.Status);
                result.IsOnline = result.Status != "5000";
                if (result.Status == "1070")
                {
                   result.Password = password;
                   Output.Log(2, "Access to device {0} granted", deviceId);
                }
                
                result.TcpRelayIp = EdimaxUtils.GetXmlValue(doc, "relayip");
                result.TcpRelayId = EdimaxUtils.GetXmlValue(doc, "relayid");
                result.TcpRelayPort = EdimaxUtils.GetXmlValue(doc, "relayreqport") == null ? 0 : int.Parse(EdimaxUtils.GetXmlValue(doc, "relayreqport"));
                result.DeviceIp = EdimaxUtils.GetXmlValue(doc, "ip");
                result.Alias = EdimaxUtils.GetXmlValue(doc, "alias");
                result.Model = EdimaxUtils.GetXmlValue(doc, "model");
                result.Type = EdimaxUtils.GetXmlValue(doc, "type");
                result.ProdId = EdimaxUtils.GetXmlValue(doc, "prodid");
                if (!string.IsNullOrWhiteSpace(result.TcpRelayId))
                {
                    Output.Info("Device {0} is online at {1}", deviceId, result.DeviceIp);
                    Output.Info("    Alias     : {0}", result.Alias);
                    Output.Info("    Type      : {0}", result.Type);
                    Output.Info("    Model     : {0}", result.Model);
                    Output.Info("    Ip        : {0}", result.DeviceIp);
                    Output.Info("    Relay     : {0}:{1}", result.TcpRelayIp, result.TcpRelayPort);
                    Output.Info("    RelayId   : {0}", result.TcpRelayId);
                    Output.Info("    Status    : {0}", result.Status);
                }

                udp.Close();
                return result;
            }
            catch (Exception x)
            {
                Output.Error("UDP communication error: {0}", x.Message);
                return new UdpResponse() { Success = false };
            }
        }

        private Socket CreateUdpSocket()
        {
            Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(IPAddress.Any, 0));
            udp.ReceiveTimeout = UdpTimeoutMillis;
            return udp;
        }

        private string UdpSendAndReceive(Socket udp, IPEndPoint destination, string deviceId, string password)
        {
            string auth = EdimaxUtils.GetMD5(string.Format("admin:{0}", password));
            long unixTimestamp = (long)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalMilliseconds;
            string checkStr = checkStr = string.Format(UdpRequestCmd, deviceId, auth, unixTimestamp);
            Output.Data(">>", checkStr);
            //checkStr = string.Format("<param><code value=\"2030\" /><id value=\"{0}\" /><lanip value=\"192.168.1.2\" /><lanport value=\"53321\" /><nattype value=\"0\" /><reqdirport value=\"0\" /><reqfwver value=\"1.3.4.a#020100\" /><relayid value=\"{0}{1}\" /></param>", deviceId, unixTimestamp);
            byte[] request = EdimaxUtils.Encrypt(checkStr, 7);
            Output.Log(2, "Sending UDP request to {0}:{1} for device {2} with pass {3}", destination.Address.ToString(), destination.Port, deviceId, password);
            Output.Data(">>", request);
            udp.SendTo(request, destination);
            byte[] response = new byte[2048];
            EndPoint responder = (EndPoint)new IPEndPoint(destination.Address, destination.Port);
            Output.Log(3, "Waiting for UDP response");
            int receivedCount = udp.ReceiveFrom(response, ref responder);
            Output.Log(3, "Got {0}b in UDP response", receivedCount);
            response = response.Take(receivedCount).ToArray();
            Output.Data("<<", response);
            int r;
            string decrypted = EdimaxUtils.Decrypt(response, out r);
            Output.Data("<<", decrypted);
            return decrypted;
        }
    }

    public class UdpResponse
    {
        public bool Success { get; set; }

        public string Status { get; set; }

        public bool IsOnline { get; set; }

        public string TcpRelayIp { get; set; }

        public int TcpRelayPort { get; set; }

        public string TcpRelayId { get; set; }

        public string Password { get; set; }

        public string DeviceIp { get; set; }

        public string Alias { get; set; }

        public string Model { get; set; }

        public string Type { get; set; }

        public string ProdId { get; set; }
    }

    public class EdimaxResult
    {
        public string DeviceId { get; set; }

        public UdpResponse UdpResult { get; set; }

        public bool TcpSuccess { get; set; }

        public string State { get; set; }

        public string NextToggle { get; set; }

        public string[] Schedule { get; set; }

        public string ScheduleExplanation { get; set; }

        public double Power { get; set; }

        public double Current { get; set; }

        public double EnergyDay { get; set; }

        public double EnergyWeek { get; set; }

        public double EnergyMonth { get; set; }

        public string[] EnergyHistory { get; set; }

        public string EmailSender { get; set; }

        public string EmailRecipient { get; set; }

        public string RunModel { get; set; }

        public string RunCus { get; set; }

        public string RunFW { get; set; }

        public string FileGenerated { get; set; }
    }

    public enum EdimaxCommand
    {
        Probe,
        PlugInfo,
        On,
        Off,
        Toggle,
        GetSchedule,
        Power,
        History,
        Image,
        Web
    }
}
