// Author: Maciej Siekierski

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EdiManager
{
    public static class EdimaxUtils
    {
        public static byte[] ReadBytes(Stream stream, int count)
        {
            byte[] result = new byte[count];
            int leftToRead = count;
            int offset = 0;
            int zeros = 0;
            while (leftToRead > 0)
            {
                Output.Log(4, "Waiting for {0}b", leftToRead);
                int read = stream.Read(result, offset, leftToRead);
                Output.Data("<<", result.Skip(offset).Take(read).ToArray());
                Output.Log(4, "Received {0}b", read);
                leftToRead -= read;
                offset += read;
                if (read == 0)
                    zeros++;
                else
                    zeros = 0;
                if (zeros > 100)
                {
                    throw new Exception("TCP Connection broken");
                }
            }

            return result;
        }

        public static byte[] ReadBytes(Stream stream, byte[] endMarker)
        {
            Output.Log(4, "Reading bytes until {0}", string.Join("", endMarker.Select(e => e.ToString("X2"))));
            List<byte> result = new List<byte>();
            bool finished = false;
            do
            {
                int b = stream.ReadByte();
                if (b == -1)
                {
                    throw new Exception("End of stream");
                }
                else
                {
                    result.Add((byte)b);
                    if (result.Count >= endMarker.Length)
                    {
                        finished = true;
                        for (int i = 0; i < endMarker.Length; i++)
                        {
                            if (result[result.Count - endMarker.Length + i] != endMarker[i])
                                finished = false;
                        }
                    }
                }

            }
            while (!finished);
            Output.Log(4, "Received {0}b", result.Count);
            Output.Data("<<", result.ToArray());
            return result.ToArray();
        }

        public static XDocument GetDocumentFromHttpLikeString(string text)
        {
            int idx = text.IndexOf("<SMARTPLUG");
            if (idx > -1)
            {
                string xml = text.Substring(idx);
                return XDocument.Parse(xml);
            }
            else
            {
                return null;
            }
        }

        public static string Decrypt(byte[] bytes, out int rotate)
        {
            rotate = 0;
            string result = "";
            for (int i = 0; i < bytes.Length; i++)
            {
                int val = bytes[i];
                if (rotate == 0)
                {
                    rotate = val - '<';
                    val = '<';
                }
                else
                {
                    val <<= rotate;
                    val = (val & 0xff) | ((val & 0xff00) >> 8);
                }

                result += (char)val;
            }
            return result;
        }

        public static byte[] Encrypt(string text, int rotate)
        {
            return Encrypt(Encoding.UTF8.GetBytes(text), rotate);
        }

        public static byte[] Encrypt(byte[] bytes, int rotate)
        {
            var encrypted = new List<byte>();
            for (int i = 0; i < bytes.Length; i++)
            {
                int val = bytes[i];
                if (i == 0)
                {
                    val = val + rotate;
                }
                else
                {
                    val = val << (8 - rotate);
                    val = ((val & 0xff00) >> 8) + (val & 0xff);
                }

                encrypted.Add((byte)val);
            }

            return encrypted.ToArray();
        }

        public static string GetMD5(string input)
        {
            using (MD5 md5Hash = MD5.Create())
            {
                byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder sBuilder = new StringBuilder();
                for (int i = 0; i < data.Length; i++)
                {
                    sBuilder.Append(data[i].ToString("x2"));
                }

                return sBuilder.ToString();
            }
        }

        public static string GetXmlValue(XDocument doc, string paramName)
        {
            if (doc.Root.Element(paramName) == null)
            {
                return null;
            }
            else if (doc.Root.Element(paramName).Attribute("value") == null)
            {
                return null;
            }
            else
            {
                return doc.Root.Element(paramName).Attribute("value").Value;
            }
        }

        public static string GetCodeDescription(string code)
        {
            //1120=bad password, 5000=offline, 1020=ok
            switch (code)
            {
                case "5000": return "Offline";
                case "1120": return "Bad password";
                case "1020": return "Online";
                default: return "Unknown code: " + code;
            }
        }

        public static string ExplainSchedule(string schedule)
        {
            if (string.IsNullOrEmpty(schedule))
            {
                return "notset";
            }
            else
            {
                string binary = "";
                for (int j = 0; j < schedule.Length; j++)
                {
                    int number = Convert.ToInt16("" + schedule[j], 16);
                    binary += Convert.ToString(number, 2).PadLeft(4, '0');
                }

                List<string> changes = new List<string>();
                char state = ' ';
                for (int i = 0; i < binary.Length; i++)
                {
                    string time = string.Format("{0}:{1}", (i / 60).ToString().PadLeft(2, '0'), (i % 60).ToString().PadLeft(2, '0'));
                    char newState = binary[i];
                    if (newState != state)
                    {
                        state = newState;
                        changes.Add(string.Format("{0}={1}", time, newState == '1' ? "ON" : "OFF"));
                    }
                }

                return string.Join(";", changes);
            }
        }
    }
}
