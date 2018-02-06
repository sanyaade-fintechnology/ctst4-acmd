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
using System.Threading;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using System.Collections;

namespace CTST4_ACMD
{
    class TestClass
    {
        public void SayHello()
        {
            Console.WriteLine("hello");
        }
    }

    // holder for global variables and static helper/util functions
    class G
    {
        public static T4.API.Host client;
        public static MDController mdController;
        public static ActionQueue mdAQ = new ActionQueue();
        public static ZSocket sockMDPUB = new ZSocket(ZSocketType.PUB);
        public static ActionQueue pubAQ = new ActionQueue();
        public static ZSocket sockACPUB = new ZSocket(ZSocketType.PUB);
        public static DateTime startTime = DateTime.UtcNow;
        

        public static string MarketToTickerID(T4.API.Market market)
        {
            return "/" + market.ExchangeID + "/" + market.ContractID + "/" + market.MarketID;
        }
    }

    class MDController : Controller
    {
        JArray CAPABILITIES = new JArray();

        class CTSSubDef
        {
            public T4.DepthBuffer depthBuffer;
            public T4.DepthLevels depthLevels;
            public bool handled = false;

            public CTSSubDef()
            {
                depthBuffer = T4.DepthBuffer.NoSubscription;
                depthLevels = T4.DepthLevels.Normal;
            }

            public CTSSubDef(T4.DepthBuffer dbuf, T4.DepthLevels dlvl)
            {
                depthBuffer = dbuf;
                depthLevels = dlvl;
            }

            public override bool Equals(object other)
            {
                var o = other as CTSSubDef;
                if (o == null)
                    return false;
                if (depthBuffer != o.depthBuffer)
                    return false;
                if (depthLevels != o.depthLevels)
                    return false;
                return true;
            }

            public override int GetHashCode()
            {
                return base.GetHashCode();
            }

            public override string ToString()
            {
                return String.Format("DepthBuffer={0}, DepthLevels={1}",
                                     depthBuffer, depthLevels);
            }
        }

        static Logger L = LogManager.GetCurrentClassLogger();

        ConcurrentDict<string, CTSSubDef> _ctsSubscriptions;

        public MDController(string name, string addr, ActionQueue actionQueue, ZContext ctx = null)
            : base(name, addr, actionQueue, ctx)
        {
            _ctsSubscriptions = new ConcurrentDict<string, CTSSubDef>();
            _ctsSubscriptions.Initializer = () => new CTSSubDef();

            CAPABILITIES.Add("LIST_DIRECTORY");
            CAPABILITIES.Add("SUBSCRIBE");
            CAPABILITIES.Add("GET_TICKER_FIELDS");
            CAPABILITIES.Add("GET_TICKER_INFO_PRICE_TICK_SIZE");
            CAPABILITIES = new JArray(CAPABILITIES.OrderBy((x) => x.ToString()));
        }

        [Controller.Command]
        JToken GetStatus(List<ZFrame> ident, JObject msg)
        {
            TimeSpan uptime = DateTime.UtcNow - G.startTime;
            JObject res = new JObject();
            res["name"] = "ctst4-md";
            res["connector_name"] = "ctst4";
            res["uptime"] = uptime.TotalSeconds;
            return res;
        }

        T4.API.ContractList GetContracts(T4.API.Exchange exchange)
        {
            if (exchange.Contracts.Complete)
                return exchange.Contracts;
            AutoResetEvent signal = new AutoResetEvent(false);
            exchange.Contracts.ContractListComplete += (x) => { signal.Set(); };
            signal.WaitOne();
            return exchange.Contracts;
        }

        T4.API.MarketList GetMarkets(T4.API.Contract contract)
        {
            if (contract.Markets.Complete)
                return contract.Markets;
            AutoResetEvent signal = new AutoResetEvent(false);
            contract.Markets.MarketListComplete += (x) => { signal.Set(); };
            signal.WaitOne();
            return contract.Markets;
        }

        T4.API.Market GetMarket(string exchangeID, string contractID, string marketID)
        {
            var contracts = GetContracts(G.client.MarketData.Exchanges[exchangeID]);
            var markets = GetMarkets(contracts[contractID]);
            return markets[marketID];
        }

        T4.API.Market GetMarket(string tickerID)
        {
            string[] spl = tickerID.Split('/').Where((x) => x.Length > 0).ToArray();
            if (spl.Length != 3)
            {
                throw new ArgumentException("invalid ticker_id");
            }
            return GetMarket(spl[0], spl[1], spl[2]);
        }

        [Controller.Command]
        JToken GetTickerInfo(List<ZFrame> ident, JObject msg)
        {
            string tickerID = msg["content"]["ticker"]["ticker_id"].ToString();
            string[] spl = tickerID.Split('/').Where((x) => x.Length > 0).ToArray();
            if (spl.Length != 3)
            {
                throw new ArgumentException("invalid ticker_id");
            }
            var market = GetMarket(spl[0], spl[1], spl[2]);
            L.Info("market: {0}", market);
            JObject res = new JObject();
            res["ticker_id"] = tickerID;
            res["float_price"] = true;
            res["float_volume"] = false;
            res["description"] = market.Description;
            // TODO: confirm tick_size logic
            res["price_tick_size"] = market.Numerator / (double)market.Denominator / Math.Pow(10, market.RealDecimals);
            res["price_tick_value"] = market.TickValue;
            res["volume_tick_size"] = market.VolumeIncrement;
            if (market.Details.Length > 0)
                res["details"] = market.Details;
            // TODO: parse order_types
            if (market.StrategyType != T4.StrategyType.None)
            {
                res["strategy_type"] = market.StrategyType.ToString();
                if (market.StrategyRatio != 0)
                {
                    res["strategy_ratio"] = market.StrategyRatio;
                }
                // TODO: export strategy_ratio?
            }
            res["activation_date"] = market.ActivationDate.ToString("yyyy-MM-dd");
            res["expiration"] = market.LastTradingDate.ToString("yyyy-MM-ddTHH:mm:ss.ffffff");
            res["ticker_type"] = market.ContractType.ToString();
            res["currency"] = market.Currency;
            if (market.StrikePrice != null)
            {
                res["strike"] = market.StrikePrice;
            }
            if (market.YieldYears > 0)
            {
                res["yield_years"] = market.YieldYears;
                res["yield_basis"] = market.YieldBasis;
                res["yield_par_value"] = market.YieldParValue;
                res["yield_payments_per_year"] = market.YieldPaymentsPerYear;
                res["yield_rate"] = market.YieldRate;
                res["yield_redemption"] = market.YieldRedemption;
                res["yield_value_denominator"] = market.YieldValueDenominator;
            }
            if (market.Legs.Count > 0)
            {
                var legs = new JArray();
                for (int i = 0; i < market.Legs.Count; i++)
                {
                    T4.API.Market.LegItem leg = market.Legs[i];
                    var d = new JObject();
                    if (leg.Delta.Length > 0)
                        d["delta"] = leg.Delta;
                    if (leg.Price.Length > 0)
                        d["price"] = leg.Price;
                    d["ratio"] = leg.Volume;
                    d["ticker_id"] = G.MarketToTickerID(leg.Market);
                    legs.Add(d);
                }
                res["legs"] = legs;
            }
            return new JArray() { res };
        }

        [Controller.Command]
        JToken ListDirectory(List<ZFrame> ident, JObject msg)
        {
            string dir = "/";
            try
            {
                dir = msg["content"]["directory"].ToString();
            }
            catch (Exception) { }
            if (dir == "")
            {
                dir = "/";
            }
            T4.API.MarketData md = G.client.MarketData;
            JArray res = new JArray();
            string[] spl = dir.Split('/').Where((x) => x.Length > 0).ToArray();
            if (spl.Length == 0)
            {
                foreach (T4.API.Exchange exchange in G.client.MarketData.Exchanges)
                {
                    var r = new JObject();
                    r["name"] = exchange.ExchangeID;
                    r["description"] = exchange.Description;
                    res.Add(r);
                }
            }
            else if (spl.Length == 1)
            {
                foreach (T4.API.Contract contract in GetContracts(md.Exchanges[spl[0]]))
                {
                    var r = new JObject();
                    r["name"] = contract.ContractID;
                    r["description"] = contract.Description;
                    res.Add(r);
                }
            }
            else if (spl.Length == 2)
            {
                var markets = GetMarkets(GetContracts(md.Exchanges[spl[0]])[spl[1]]);
                foreach (T4.API.Market market in markets)
                {
                    var r = new JObject();
                    r["name"] = market.MarketID;
                    r["ticker_id"] = G.MarketToTickerID(market);
                    r["description"] = market.Description;
                    res.Add(r);
                }

            }
            else if (spl.Length == 3)
            {
                T4.API.Market market = GetMarket(spl[0], spl[1], spl[2]);
                var r = new JObject();
                r["name"] = market.MarketID;
                r["ticker_id"] = G.MarketToTickerID(market);
                r["description"] = market.Description;
                res.Add(r);
            }
            else
            {
                throw new ArgumentException("directory not found");
            }
            res = new JArray(res.OrderBy((x) => x["name"]));
            return res;
        }

        [Controller.Command]
        JToken GetTickerFields(List<ZFrame> ident, JObject msg)
        {
            // only ticker_id is ok
            return new JObject();
        }

        [Controller.Command]
        JToken ListCapabilities(List<ZFrame> ident, JObject msg)
        {
            return CAPABILITIES;
        }

        // TODO: what is this, something needs to be done here??
        void MarketCheckSubscription(T4.API.Market poMarket, ref T4.DepthBuffer penDepthBuffer,
                                     ref T4.DepthLevels penDepthLevels)
        {
            string tickerID = G.MarketToTickerID(poMarket);
            L.Info("marketchecksubscription: {0}, {1}, {2}",
                   tickerID, penDepthBuffer, penDepthLevels);
        }

        [Controller.Command]
        JToken ModifySubscription(List<ZFrame> ident, JObject msg)
        {
            var content = (JObject)msg["content"];
            string tickerID = content["ticker_id"].ToString();
            SubscriptionDefinition sd = _subscriptions[tickerID];
            var oldSubDef = _ctsSubscriptions[tickerID];
            L.Info(sd);
            var market = GetMarket(tickerID);
            T4.DepthBuffer dbuf = T4.DepthBuffer.NoSubscription;
            T4.DepthLevels dlvl = T4.DepthLevels.Normal;
            JToken res = null;
            if (sd["trades_speed"] > 7)
            {
                if (sd["order_book_speed"] > 7)
                    dbuf = T4.DepthBuffer.FastTrade;
                else if (sd["order_book_speed"] > 4)
                    dbuf = T4.DepthBuffer.SmartTrade;
                else if (sd["order_book_speed"] > 0)
                    dbuf = T4.DepthBuffer.SlowTrade;
                else
                    dbuf = T4.DepthBuffer.TradeOnly;
            }
            else
            {
                if (sd["order_book_speed"] > 7)
                    dbuf = T4.DepthBuffer.FastSmart;
                else if (sd["order_book_speed"] > 4)
                    dbuf = T4.DepthBuffer.Smart;
                else if (sd["order_book_speed"] > 0)
                    dbuf = T4.DepthBuffer.SlowSmart;
            }
            if ((dbuf == T4.DepthBuffer.NoSubscription || dbuf == T4.DepthBuffer.TradeOnly)
                && sd["emit_quotes"])
            {
                dbuf = T4.DepthBuffer.SlowSmart;
                dlvl = T4.DepthLevels.BestOnly;
            }
            else if (dbuf != T4.DepthBuffer.NoSubscription)
            {
                // does cts t4 api support arbitrary depth levels or enum numbers?
                dlvl = (T4.DepthLevels)Math.Min(sd["order_book_levels"], (int)T4.DepthLevels.All);
            }
            CTSSubDef newSubDef = new CTSSubDef(dbuf, dlvl);
            if (oldSubDef.Equals(newSubDef))
            {
                res = JToken.FromObject("no change");
                return res;
            }
            //else
            //{
            //    if (newSubDef.DepthBuffer == T4.DepthBuffer.NoSubscription)
            //    {
            //        res["order_book"] = "unsubscribed";
            //        res["trades"] = "unsubscribed";
            //    }
            //    else if (newSubDef.DepthBuffer == T4.DepthBuffer.TradeOnly)
            //    {
            //        res["order_book"] = "unsubscribed";
            //        res["trades"] = "subscribed";
            //    }
            //}
            res = JToken.FromObject(newSubDef.ToString());
            _ctsSubscriptions[tickerID] = newSubDef;
            if (!oldSubDef.handled)
            {
                market.MarketCheckSubscription += MarketCheckSubscription;
            }
            newSubDef.handled = true;
            market.DepthSubscribe(dbuf, dlvl);
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

        static void testing(System.Collections.ICollection poMsgs)
        {
            L.Info(poMsgs.Count);
            foreach (object x in poMsgs)
            {
                var msg = x as T4.Messages.Message;
                L.Info("{0} ({1} bytes)", x, msg.Length);
                if (x is T4.Messages.MsgMarketDepth2)
                {
                    Console.WriteLine(msg.Bytes.HexDump());
                }
            }
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
            L.Info("connecting to CTS T4 ({0}) ...", apiServerType);
            // starts several new threads after a blocking connect call
            G.client = new T4.API.Host(
                strToServerType[apiServerType],
                settings["app_name"].ToString(),
                settings["app_license"].ToString(),
                settings["firm"].ToString(),
                userName,
                settings["password"].ToString());
            

            L.Info("connected to CTS T4, version: {0}", G.client.Version);
            L.Info("logging in as {0} and populating data structures ...", userName);
            AutoResetEvent signal = new AutoResetEvent(false);
            G.client.LoginSuccess += () =>
            {
                signal.Set();
            };
            G.client.LoginFailure += (T4.LoginResult res) =>
            {
                L.Fatal("login failed: {0}", res);
                Environment.Exit(1);
            };
            signal.WaitOne();
            L.Info("logged in");
        }

        static void SetupZMQSockets(Dictionary<string, dynamic> args)
        {
            G.mdController = new MDController("MD", args["md_ctl_addr"], G.mdAQ);
            G.sockMDPUB.Bind(args["md_pub_addr"]);
            G.sockACPUB.Bind(args["ac_pub_addr"]);
        }
       
        static void StartEventLoop()
        {
            var poller = new ZPoller();
            poller.Register(G.mdController.Socket, ZPoller.POLLIN);
            poller.Register(G.mdAQ.Socket, ZPoller.POLLIN);
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
                        else if (sock == G.mdAQ.Socket)
                        {
                            G.mdAQ.ConsumeAll();
                        }
                    }
                }
                catch (Exception err)
                {
                    L.Error(err, "unhandled exception on event loop:");
                }
            }
        }

        static void PublisherLoop()
        {
            while (true)
            {
                G.pubAQ.Socket.ReceiveFrame();
                try
                {
                    G.pubAQ.ConsumeAll();
                }
                catch (Exception err)
                {
                    L.Error(err, "unhandled exception on publisher loop:");
                }
            }
        }

        static JArray OrderBookToJArray(T4.API.Market market, T4.API.Market.Depth.DepthList book)
        {
            JArray res = new JArray();
            for (int i = 0; i < book.Count; i++)
            {
                var lvl = book[i];
                JObject r = new JObject();
                r["price"] = market.ConvertTicksToRealDecimal(lvl.Ticks);
                r["size"] = lvl.Volume;
                r["num_orders"] = lvl.NumOfOrders;
                res.Add(r);
            }
            return res;
        }

        static void HandleMarketDepthUpdate(T4.API.Market market)
        {
            string tickerID = G.MarketToTickerID(market);
            JObject msg = new JObject();
            var ld = market.LastDepth;
            msg["bids"] = OrderBookToJArray(market, ld.Bids);
            msg["asks"] = OrderBookToJArray(market, ld.Offers);
            if (ld.ImpliedBids.Count > 0)
                msg["implied_bids"] = OrderBookToJArray(market, ld.ImpliedBids);
            if (ld.ImpliedOffers.Count > 0)
                msg["implied_asks"] = OrderBookToJArray(market, ld.ImpliedOffers);
            msg["timestamp"] = market.LastDepth.Time.ToUniversalTime().ToUnixTimestamp();
            var msgParts = new List<ZFrame>();
            msgParts.Add(new ZFrame(tickerID + "\x01"));
            msgParts.Add(new ZFrame(" " + msg.ToString()));
            G.pubAQ.Enqueue(() => G.sockMDPUB.SendMultipart(msgParts));
        }

        static void HandleMarketHighLow(T4.API.Market market)
        {
            string tickerID = G.MarketToTickerID(market);
            var msg = new JObject();
            var daily = new JObject();
            daily["high"] = market.ConvertTicksToRealDecimal(market.LastHighLow.HighTicks);
            daily["low"] = market.ConvertTicksToRealDecimal(market.LastHighLow.LowTicks);
            daily["open"] = market.ConvertTicksToRealDecimal(market.LastHighLow.OpenTicks);
            msg["daily"] = daily;
            var msgParts = new List<ZFrame>();
            msgParts.Add(new ZFrame(tickerID + "\x03"));
            msgParts.Add(new ZFrame(" " + msg.ToString()));
            G.pubAQ.Enqueue(() => G.sockMDPUB.SendMultipart(msgParts));
        }

        static void HandleMarketSettlement(T4.API.Market market)
        {
            var ls = market.LastSettlement;
            string tickerID = G.MarketToTickerID(market);
            L.Info("marketsettlement: {0}, oi={1}, cleared_vol={2}, held_ticks={3}, " +
                   "held_time={4}, held_date={5}, ticks={6}, vwap={7}",
                    tickerID, ls.OpenInterest, ls.ClearedVolume, ls.HeldTicks,
                    ls.HeldTime, ls.HeldTradeDate, ls.Ticks, ls.VWAP);
            // TODO: anything worth exposing to zmapi here?
        }

        static void HandleMarketTradeVolume(T4.API.Market market,
                                            T4.API.Market.TradeVolume poChanges)
        {
            string tickerID = G.MarketToTickerID(market);
            var ld = market.LastDepth;
            var msg = new JObject();
            msg["price"] = market.ConvertTicksToRealDecimal(ld.LastTradeSpdTicks);
            msg["size"] = ld.LastTradeSpdVolume;
            msg["spread_trade"] = ld.LastTradeDueToSpread;
            var msgParts = new List<ZFrame>();
            msgParts.Add(new ZFrame(tickerID + "\x02"));
            msgParts.Add(new ZFrame(" " + msg.ToString()));
            G.pubAQ.Enqueue(() => G.sockMDPUB.SendMultipart(msgParts));
            // emit volume-by-price info from poChanges?
        }

        static void Main(string[] args_raw)
        {
            /* None of the argument parsers I checked for .NET didn't handle positional arguments
             * very well or not at all. I decided to write ad-hoc parser here. Someone should
             * write a proper argument parser for .NET ... */

            var args = ParseArguments(args_raw);
            SetupLogging(args);
            var settings = ReadSettingsJson(args["login_file"]);
            connectToCTST4(settings);
            SetupZMQSockets(args);
            G.client.MarketData.MarketDepthUpdate += HandleMarketDepthUpdate;
            G.client.MarketData.MarketHighLow += HandleMarketHighLow;
            G.client.MarketData.MarketSettlement += HandleMarketSettlement;
            G.client.MarketData.MarketTradeVolume += HandleMarketTradeVolume;
            Thread thread = new Thread(PublisherLoop);
            thread.Name = "publisher-thread";
            thread.Start();
            StartEventLoop();
        }
    }
}
