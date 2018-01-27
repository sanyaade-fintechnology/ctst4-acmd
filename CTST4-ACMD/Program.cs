using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroMQ;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NLog.Config;

namespace CTST4_ACMD
{
    // holder for global variables
    class G
    {
        public static T4.API.Host client;
        public static ZSocket sockMDCTL = new ZSocket(ZSocketType.ROUTER);
        public static ZSocket sockMDPUB = new ZSocket(ZSocketType.PUB);
        public static ZSocket sockACCTL = new ZSocket(ZSocketType.ROUTER);
        public static ZSocket sockACPUB = new ZSocket(ZSocketType.PUB);
    }

    class CodecException : Exception
    {
        public CodecException() : base() { }
        public CodecException(string msg) : base(msg) { }
    }

    class DecodingException : Exception
    {
        public DecodingException() : base() { }
        public DecodingException(string msg) : base(msg) { }
    }

    class Program
    {

        static string USAGE_TEXT =
@"usage: CTST4_ACMD.exe [options...] [login_file] [md_ctl_addr] [md_pub_addr]
                        [ac_ctl_addr] [ac_pub_addr]";

static string HELP_TEXT = 
@"CTS T4 AC/MD connector module

positional arguments:
  login_file          path to json file containing login settings
  md_ctl_addr         MD CTL socket binding address
  md_pub_addr         MD PUB socket binding address
  ac_ctl_addr         AC CTL socket binding address
  ac_pub_addr         AC PUB socket binding address

optional arguments:
  -h, --help          show this help message and exit
  -l, --log-level     logging level";

        static Logger L = LogManager.GetCurrentClassLogger();
        static TextWriter ERR = Console.Error;

        static void ParseExitError(string msg)
        {
            ERR.WriteLine(USAGE_TEXT);
            ERR.WriteLine("error: " + msg);
            Environment.Exit(1);
        }

        static Dictionary<string, dynamic> ParseArguments2(string[] args)
        {
            // positional and optional arguments can be mixed together (python argparse style)
            Dictionary<string, dynamic> res = new Dictionary<string, dynamic>();

            // set default values
            res["log_level"] = LogLevel.Info;

            // positional arguments
            string[] posArguments = new string[]
            {
                "login_file",
                "md_ctl_addr",
                "md_pub_addr",
                "ac_ctl_addr",
                "ac_pub_addr"
            };

            bool posOnly = false;
            int posArgIdx = 0;
            int skipCounter = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--")
                {
                    posOnly = true;
                }
                bool optArgFound = false;
                if (!posOnly)
                {
                    optArgFound = true;
                    if (skipCounter > 0)
                    {
                        skipCounter -= 1;
                        continue;
                    }
                    if (arg.StartsWith("-h") || arg.StartsWith("--help"))
                    {
                        ERR.WriteLine(USAGE_TEXT + "\n");
                        ERR.WriteLine(HELP_TEXT);
                        Environment.Exit(0);
                    }
                    else if (arg.StartsWith("-l") || arg.StartsWith("--log-level"))
                    {
                        string val = args[i + 1];
                        try
                        {
                            res["log_level"] = LogLevel.FromOrdinal(int.Parse(val));
                        }
                        catch (Exception)
                        {
                            try
                            {
                                res["log_level"] = LogLevel.FromString(val);
                            }
                            catch (Exception)
                            {
                                ParseExitError("invalid log-level definition");
                            }
                        }
                        skipCounter = 1;
                    }
                    else
                    {
                        optArgFound = false;
                    }
                }
                if (!optArgFound)
                {
                    res[posArguments[posArgIdx]] = arg;
                    posArgIdx++;
                }
            }
            if (posArgIdx < posArguments.Length - 1)
            {
                ParseExitError("too few arguments");
            }
            return res;
        }

        static Dictionary<string, dynamic> ParseArguments(string[] args)
        {
            try
            {
                return ParseArguments2(args);
            }
            catch (Exception err) {
                ParseExitError("generic argument parser error:\n\n" + err.ToString());
            }
            return null;
        }

        static JObject ReadSettingsJson(string settingsFile)
        {
            return JObject.Parse(File.ReadAllText(settingsFile));
        }

        static void SetupLogging(Dictionary<string, dynamic> args)
        {
            var config = LogManager.Configuration;
            var rule = new LoggingRule(
                    "*", args["log_level"], config.FindTargetByName("console"));
            config.LoggingRules.Add(rule);
            // this confusing reassignment is required -- strange api
            LogManager.Configuration = config;
        }

        static void connectToCTST4(JObject settings)
        {
            Dictionary<string, T4.APIServerType> strToServerType =
                new Dictionary<string, T4.APIServerType>() {
                    { "simulator", T4.APIServerType.Simulator },
                    { "live", T4.APIServerType.Live },
            };

            string apiServerType = settings["api_server_type"].ToString();
            string userName = settings["username"].ToString();
            L.Info("connecting to CTST4 ({0}) as {1} ...", apiServerType, userName);
            // starts several new threads after a blocking connect call
            G.client = new T4.API.Host(
                strToServerType[apiServerType],
                settings["app_name"].ToString(),
                settings["app_license"].ToString(),
                settings["firm"].ToString(),
                userName,
                settings["password"].ToString());
            L.Info("connected to CTS T4");
        }

        static void SetupZMQSockets(Dictionary<string, dynamic> args)
        {
            G.sockMDCTL.Bind(args["md_ctl_addr"]);
            G.sockMDPUB.Bind(args["md_pub_addr"]);
            G.sockACCTL.Bind(args["ac_ctl_addr"]);
            G.sockACPUB.Bind(args["ac_pub_addr"]);
        }
       
        static Tuple<List<ZFrame>, JObject> ExtractMessage(List<ZFrame> msgParts)
        {
            int splIdx = msgParts.FindIndex(x => x.Length == 0);
            if (splIdx != msgParts.Count - 2)
            {
                throw new ArgumentException("malformed message");
            }
            var ident = msgParts.GetRange(0, splIdx);
            var msg = msgParts.Last();
            return new Tuple<List<ZFrame>, JObject>(ident, JObject.Parse(msg.ToString()));
        }

        static string IdentToString(List<ZFrame> ident)
        {
            var encoding = Encoding.GetEncoding("latin1");
            var parts = ident.Select(x => x.ToString(encoding).Replace('/', '\\'));
            return String.Join("/", parts);
        }

        static void HandleMDCTLMessage1(List<ZFrame> ident, JObject msg)
        {
            string cmd = msg["command"].ToString();
            string msgID = msg["msg_id"].ToString();
            string debugStr = "ident={0}, command={1}, msg_id={2}";
            debugStr = String.Format(debugStr, IdentToString(ident), cmd, msgID);
            L.Debug("> " + debugStr);
            JObject res = new JObject();
            try
            {
                res["123"] = "testing";
            }
            catch (Exception err)
            {
                L.Error(err, "generic error handling MD message:");
            }
            if (res.Count > 0)
            {
                var msgOut = new JObject();
                msgOut["result"] = "ok";
                msgOut["msg_id"] = msgID;
                msgOut["content"] = res;
                var framesOut = new List<ZFrame>(ident);
                framesOut.Add(new ZFrame());
                framesOut.Add(new ZFrame(msgOut.ToString()));
                G.sockMDCTL.SendFrames(framesOut);
            }
            L.Debug("< " + debugStr);
        }

        static void StartEventLoop()
        {
            var poller = new ZPoller();
            poller.Register(G.sockACCTL, ZPoller.POLLIN);
            poller.Register(G.sockMDCTL, ZPoller.POLLIN);
            while (true)
            {
                try
                {
                    var pollRes = poller.Poll(-1);
                    foreach (var item in pollRes)
                    {
                        var sock = item.socket;
                        var spl = ExtractMessage(sock.ReceiveMultipart());
                        var ident = spl.Item1;
                        var msg = spl.Item2;
                        if (sock == G.sockMDCTL)
                        {
                            HandleMDCTLMessage1(ident, msg);
                        }
                        else if (sock == G.sockACCTL)
                        {
                            // handle ac message
                        }
                    }
                }
                catch (Exception err)
                {
                    L.Error(err, "unhandled exception on event loop:");
                }
            }
        }

        static void Main(string[] args_raw)
        {
            /* None of the argument parsers I checked for .NET didn't handle positional arguments
             * very well or not at all. I decided to write ad-hoc parser here. Someone should
             * write a proper argument parser for .NET ... */
            var args = ParseArguments(args_raw);
            SetupLogging(args);
            var settings = ReadSettingsJson(args["login_file"]);
            // connectToCTST4(settings);
            SetupZMQSockets(args);
            StartEventLoop();
        }
    }
}
