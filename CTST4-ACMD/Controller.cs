using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroMQ;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NLog;

namespace CTST4_ACMD
{
    class Controller
    {
        static Logger L = LogManager.GetCurrentClassLogger();

        public class Command : Attribute { }

        protected Dictionary<string, MethodInfo> _commands = new Dictionary<string, MethodInfo>();
        protected string _name;
        protected string _tag;

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

        public Controller(string name, string addr, ZContext ctx = null)
        {
            if (ctx == null)
            {
                ctx = ZContext.Current;
            }
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

        void SendResult(List<ZFrame> ident, string msgID, JObject content)
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
            var framesOut = new List<ZFrame>(ident);
            framesOut.Add(new ZFrame());
            framesOut.Add(new ZFrame(msg.ToString()));
            Socket.SendFrames(framesOut);
        }

        public void HandleMessage2(List<ZFrame> ident, JObject msg)
        {
            string msgID = msg["msg_id"].ToString();
            string cmd = msg["command"].ToString();
            string debugStr = "ident={0}, command={1}, msg_id={2}";
            debugStr = String.Format(debugStr, IdentToString(ident), cmd, msgID);
            L.Debug(_tag + "> " + debugStr);
            JObject res = null;
            try
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
                res = (JObject)method.Invoke(this, new Object[] { ident, msg });
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
                var spl = ExtractMessage(Socket.ReceiveMultipart());
                var ident = spl.Item1;
                var msg = spl.Item2;
                HandleMessage2(ident, msg);
            }
            catch (Exception err)
            {
                L.Error(err, _tag + "generic error handling MD message:");
            }

        }
    }
}
