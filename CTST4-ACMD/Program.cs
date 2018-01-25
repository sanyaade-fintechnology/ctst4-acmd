using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroMQ;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CTST4_ACMD
{
    class Program
    {
        static T4.API.Host client;
        static ZeroMQ.ZContext ctx;
        static ZeroMQ.ZSocket sockMDCTL;
        static ZeroMQ.ZSocket sockMDPUB;
        //ZeroMQ.ZSocket sockACCTL;
        //ZeroMQ.ZSocket sockACPUB;

        static Dictionary<string, dynamic> ParseArguments(string[] args)
        {
            Dictionary<string, dynamic> res = new Dictionary<string,dynamic>();
            res["settings_file"] = args[0];
            return res;
        }

        static JObject ReadSettingsJson(string settingsFile)
        {
            return JObject.Parse(File.ReadAllText(settingsFile));
        }

        static void Main(string[] args_raw)
        {
            Dictionary<string, dynamic> args = ParseArguments(args_raw);
            dynamic settings = ReadSettingsJson(args["settings_file"]);

            Dictionary<string, T4.APIServerType> strToServerType = 
                new Dictionary<string,T4.APIServerType>() {
                    { "simulator", T4.APIServerType.Simulator },
                    { "live", T4.APIServerType.Live },
            };
            client = new T4.API.Host(
                strToServerType[settings["api_server_type"].ToString()],
                settings["app_name"].ToString(),
                settings["app_license"].ToString(),
                settings["firm"].ToString(),
                settings["username"].ToString(),
                settings["password"].ToString());
            Console.WriteLine("connected");
        }
    }
}
