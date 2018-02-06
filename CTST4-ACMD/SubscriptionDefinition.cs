using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using System.Runtime.Serialization;
using System.Collections.Concurrent;

namespace CTST4_ACMD
{
    /* Breaking some rules of c# here ... c# is just too bothersome.
     * This code is not speed critical. */
    public class SubscriptionDefinition
    {
        public Dict<string, dynamic> Data = new Dict<string,dynamic>() 
        {
            {"trades_speed", 0},
            {"order_book_speed", 0},
            {"order_book_levels", 0},
            {"emit_quotes", false}
        };

        HashSet<string> FIELDS = new HashSet<string>()
        {
            "trades_speed",
            "order_book_speed",
            "order_book_levels",
            "emit_quotes"
        };

        public dynamic this[string key]
        {
            get
            {
                return Data[key];
            }
            set
            {
                if (FIELDS.Contains(key))
                {
                    Data[key] = value;
                }
                else
                {
                    throw new MissingFieldException("missing field: " + key);
                }
            }
        }

        public SubscriptionDefinition() : this(new JObject()) { }

        public SubscriptionDefinition(JObject data)
        {
            JToken token = null;
            if (data.TryGetValue("trades_speed", out token))
                Data["trades_speed"] = int.Parse(token.ToString());
            if (data.TryGetValue("order_book_speed", out token))
                Data["order_book_speed"] = int.Parse(token.ToString());
            if (data.TryGetValue("order_book_levels", out token))
                Data["order_book_levels"] = int.Parse(token.ToString());
            if (data.TryGetValue("emit_quotes", out token))
                Data["emit_quotes"] = bool.Parse(token.ToString());
        }

        public SubscriptionDefinition(SubscriptionDefinition other)
        {
            Data = new Dict<string, dynamic>(other.Data);
        }

        public SubscriptionDefinition Update(IDictionary<string, dynamic> d)
        {
            foreach (var kvp in d)
            {
                if (!FIELDS.Contains(kvp.Key))
                    continue;
                Data[kvp.Key] = kvp.Value;
            }
            return this;
        }

        public bool Empty()
        {
            if (Data["trades_speed"] > 0)
                return false;
            if (Data["order_book_speed"] > 0)
                return false;
            if (Data["order_book_levels"] > 0)
                return false;
            if (Data["emit_quotes"])
                return false;
            return true;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object other)
        {
            var o = other as SubscriptionDefinition;
            if (o == null)
                return false;
            return Data.Equals(o.Data);
        }

        public override string ToString()
        {
            string s = "<SD";
            s += " trades_speed=" + Data["trades_speed"];
            s += " order_book_speed=" + Data["order_book_speed"];
            s += " order_book_levels=" + Data["order_book_levels"];
            s += " emit_quotes=" + Data["emit_quotes"];
            s += ">";
            return s;
        }
    }
}
