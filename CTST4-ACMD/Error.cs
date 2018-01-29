using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace CTST4_ACMD
{
    public class Error
    {
        public class ErrorInfo
        {
            public int code;
            public string name;
            public string message;

            public ErrorInfo(int code, string name, string message)
            {
                this.code = code;
                this.name = name;
                this.message = message;
            }
        }

        public static Dictionary<int, ErrorInfo> ErrByCode = new Dictionary<int, ErrorInfo>();
        public static Dictionary<string, int> ECodeByName = new Dictionary<string, int>();

        static Error()
        {
            var reader = new StreamReader(@"codes\errcodes.csv");
            reader.ReadLine();  // skip headers

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                var spl = line.Split(',');
                int ecode = int.Parse(spl[0]);
                var errInfo = new ErrorInfo(int.Parse(spl[0]), spl[1], spl[2]);
                ErrByCode[errInfo.code] = errInfo;
                ECodeByName[errInfo.name] = errInfo.code;
            }
            reader.Close();
        }

        public static JObject GenError(int ecode, string msg = null)
        {
            Debug.Assert(ErrByCode.ContainsKey(ecode));
            if (msg == null)
            {
                msg = ErrByCode[ecode].message;
            }
            var content = new JObject();
            content["ecode"] = ecode;
            content["msg"] = msg;
            var res = new JObject();
            res["result"] = "error";
            res["content"] = content;
            return res;
        }
    }
}