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
using System.Reflection;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;

namespace CTST4_ACMD
{
    // holder for global variables
    class G
    {
        public static T4.API.Host client;
        public static MDController mdController;
        public static ZSocket sockMDPUB = new ZSocket(ZSocketType.PUB);
        public static ZSocket sockACPUB = new ZSocket(ZSocketType.PUB);
        public static DateTime startTime = DateTime.UtcNow;
        public static ActionQueue actionQueue = new ActionQueue();
    }

    class ActionQueue
    {
        public ZSocket Socket { get; private set; }

        string _addr;
        ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        ZContext _ctx;

        public ActionQueue(ZContext ctx = null)
        {
            if (ctx == null)
            {
                ctx = ZContext.Current;
            }
            _ctx = ctx;
            Socket = new ZSocket(_ctx, ZSocketType.PULL);
            _addr = "inproc://taskqueue-notifier-" + Guid.NewGuid().ToString();
            Socket.Bind(_addr);
        }

        public void Enqueue(Action action)
        {
            _queue.Enqueue(action);
            ZSocket sock = new ZSocket(_ctx, ZSocketType.PUSH);
            sock.Connect(_addr);
            sock.Send(new ZFrame());
            sock.Close();
        }

        public int ConsumeAll()
        {
            int numConsumed = 0;
            Socket.ReceiveFrame();
            Action action;
            while (_queue.TryDequeue(out action))
            {
                action();
                numConsumed += 1;
            }
            return numConsumed;
        }
    }

    class MDController : Controller
    {
        public MDController(string name, string addr, ZContext ctx = null)
            : base(name, addr, ctx)
        {
        }

        [Controller.Command]
        protected JObject GetStatus(List<ZFrame> ident, JObject msg)
        {
            TimeSpan uptime = DateTime.UtcNow - G.startTime;
            JObject res = new JObject();
            res["name"] = "ctst4-md";
            res["connector_name"] = "ctst4";
            res["uptime"] = uptime.TotalSeconds;
            return res;
        }
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
            G.mdController = new MDController("MD", args["md_ctl_addr"]);
            G.sockMDPUB.Bind(args["md_pub_addr"]);
            G.sockACPUB.Bind(args["ac_pub_addr"]);
        }
       
        static void StartEventLoop()
        {
            var poller = new ZPoller();
            poller.Register(G.mdController.Socket, ZPoller.POLLIN);
            poller.Register(G.actionQueue.Socket, ZPoller.POLLIN);
            while (true)
            {
                try
                {
                    var pollRes = poller.Poll(-1);
                    foreach (var item in pollRes)
                    {
                        var sock = item.socket;
                        if (sock == G.mdController.Socket)
                        {
                            G.mdController.HandleMessage();
                        }
                        else if (sock == G.actionQueue.Socket)
                        {
                            Debug.Assert(G.actionQueue.ConsumeAll() > 0);
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
