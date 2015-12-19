// Author: Maciej Siekierski

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EdiManager
{
    public static class Output
    {
        public static int VerbosityLevel = 1;

        public static bool OutputRawData = false;

        public static bool OutputTextData = false;

        public static void Info(string text, params object[] args)
        {
            Console.WriteLine(text, args);
        }

        public static void Error(string text, params object[] args)
        {
            Console.WriteLine(text, args);
        }

        public static void Data(string description, string data)
        {
            if (OutputTextData)
            {
                Console.WriteLine(string.Format("{0} TXT", description));
                Console.WriteLine(data);
            }
        }

        public static void Data(string description, byte[] data)
        {
            if (OutputRawData)
            {
                Console.WriteLine(string.Format("{0} HEX", description));
                Console.WriteLine(string.Join("", data.Select(b => b.ToString("x2"))));
            }
        }

        public static void Log(int level, string text, params object[] args)
        {
            if (level <= VerbosityLevel)
            Console.WriteLine(text, args);
        }
    }
}
