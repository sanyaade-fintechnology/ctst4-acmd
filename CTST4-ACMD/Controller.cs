using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroMQ;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NLog;
using System.Threading;
using System.Threading.Tasks;

namespace CTST4_ACMD
{
    class Controller
    {
        static Logger L = LogManager.GetCurrentClassLogger();

        public class Command : Attribute { }

        protected Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>();
        protected string _name;
        protected string _tag;
        protected ActionQueue _actionQueue;
        protected ConcurrentDict<string, SubscriptionDefinition> _subscriptions;
        protected ConcurrentDict<string, object> _subLocks;  // reentrancy control

        public ZSocket Socket { get; private set;  }

        // add subscriptions

        static string MethodNameToCommandName(string name)
        {
            StringBuilder res = new StringBuilder();
            foreach (char c in name)
            {
                if (res.Length > 0)
                {
                    if (char.IsUpper(c))
                    {
                        res.Append("_");
                    }
                }
                res.Append(c);
            }
            return res.ToString().ToLower();
        }

        public Controller(string name, string addr, ActionQueue actionQueue, ZContext ctx = null)
        {
            if (ctx == null)
            {
                ctx = ZContext.Current;
            }
            _actionQueue = actionQueue;
            _name = name;
            _tag = "[" + _name + "] ";
            Socket = new ZSocket(ctx, ZSocketType.ROUTER);
            Socket.Bind(addr);
            BindingFlags flags = BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            var methods = GetType()
                    .GetMethods(flags)
                    .Where(x => x.GetCustomAttributes(typeof(Command), false).Length > 0);
            foreach (var method in methods)
            {
                string cmd = MethodNameToCommandName(method.Name);
                _commands[cmd] = method;
            }
            _subscriptions = new ConcurrentDict<string, SubscriptionDefinition>();
            _subscriptions.Initializer = () => new SubscriptionDefinition();
            _subLocks = new ConcurrentDict<string, object>();
            _subLocks.Initializer = () => new object();
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

        void SendResult(List<ZFrame> ident, string msgID, JToken content)
        {
            var dataOut = new JObject();
            dataOut["msg_id"] = msgID;
            dataOut["content"] = content;
            dataOut["result"] = "ok";
            SendReply(ident, dataOut);
        }

        void SendError(List<ZFrame> ident, string msgID, string ename, string msg = null)
        {
            var dataOut = Error.GenError(Error.ECodeByName[ename], msg);
            dataOut["msg_id"] = msgID;
            SendReply(ident, dataOut);
        }

        void SendReply(List<ZFrame> ident, JObject msg)
        {
            var msgParts = new List<ZFrame>(ident);
            msgParts.Add(new ZFrame());
            msgParts.Add(new ZFrame(msg.ToString()));
            // Socket must not be touched from threads other than the event loop thread
            _actionQueue.Enqueue(new Action(() => Socket.SendMultipart(msgParts)));
        }

        JToken HandleModifySubscription2(List<ZFrame> ident, JObject msg)
        {
            JToken res = null;
            JObject content = (JObject)msg["content"];
            string tickerID = content["ticker_id"].ToString();
            var oldSubDef = _subscriptions[tickerID];
            var contentDict = (Dictionary<string, dynamic>)content
                    .ToObject(typeof(Dictionary<string, dynamic>));
            var newSubDef = new SubscriptionDefinition(oldSubDef).Update(contentDict);
            if (oldSubDef.Equals(newSubDef))
            {
                // nothing to do
                res = new JObject();
                res["trades"] = "no change";
                res["order_book"] = "no change";
                return res;
            }
            if (newSubDef.Empty())
            {
                _subscriptions.Pop(tickerID, null);
            }
            else
            {
                _subscriptions[tickerID] = newSubDef;
            }
            var msgMod = (JObject)msg.DeepClone();
            ((JObject)msgMod["content"]).Update(JObject.FromObject(oldSubDef.Data));
            try
            {
                string cmd = msg["command"].ToString();
                res = (JToken)_commands[cmd].Invoke(this, new Object[] { ident, msgMod });
            }
            catch (Exception err)
            {
                _subscriptions[tickerID] = oldSubDef;
                throw err;
            }
            return res;
        }

        JToken HandleModifySubscription1(List<ZFrame> ident, JObject msg)
        {
            // prevent reentrancy for same ticker_id
            lock (_subLocks[msg["content"]["ticker_id"].ToString()])
            {
                return HandleModifySubscription2(ident, msg);
            }
        }

        JToken HandleGetSubscriptions(List<ZFrame> ident, JObject msg)
        {
            // This does not guarantee a snapshot of the dictionary, values may be changed 
            // during the iteration.
            var d = _subscriptions.ToDictionary((kvp) => kvp.Key, (kvp) => kvp.Value.Data);
            return JToken.FromObject(d);
        }

        public void HandleMessage2(List<ZFrame> ident, JObject msg)
        {
            string msgID = msg["msg_id"].ToString();
            string cmd = msg["command"].ToString();
            string debugStr = "ident={0}, command={1}, msg_id={2}";
            debugStr = String.Format(debugStr, IdentToString(ident), cmd, msgID);
            debugStr += ", thread=" + Thread.CurrentThread.ManagedThreadId;
            L.Debug(_tag + "> " + debugStr);
            JToken res = null;
            try
            {
                if (cmd == "modify_subscription")
                {
                    res = HandleModifySubscription1(ident, msg);
                }
                else if (cmd == "get_subscriptions")
                {
                    res = HandleGetSubscriptions(ident, msg);
                }
                else
                {
                    MethodInfo method = null;
                    try
                    {
                        method = _commands[cmd];
                    }
                    catch (KeyNotFoundException)
                    {
                        throw new CommandNotImplementedException();
                    }
                    res = (JToken)method.Invoke(this, new Object[] { ident, msg });
                }
            }
            catch (ArgumentException err)
            {
                L.Error(err, _tag + "argument exception:");
                SendError(ident, msgID, "ARGS", err.ToString());
            }
            catch (CommandNotImplementedException)
            {
                L.Error(_tag + "command not implemented: " + cmd);
                SendError(ident, msgID, "NOTIMPL", cmd);
            }
            catch (Exception err)
            {
                L.Error(err, _tag + "unknown error occurred:");
                SendError(ident, msgID, "GENERIC", err.ToString());
            }
            if (res != null)
            {
                SendResult(ident, msgID, res);
            }
            L.Debug(_tag + "< " + debugStr);
        }

        public void HandleMessage()
        {
            try
            {
                var msgParts = Socket.ReceiveMultipart();
                if (msgParts.Last().Length == 0)
                {
                    // send pong
                    Socket.SendMultipart(msgParts);
                    return;
                }
                var spl = ExtractMessage(msgParts);
                var ident = spl.Item1;
                var msg = spl.Item2;
                // run on default thread pool
                (new Task(() => HandleMessage2(ident, msg))).Start();
            }
            catch (Exception err)
            {
                L.Error(err, _tag + "generic error handling MD message:");
            }

        }
    }
}
