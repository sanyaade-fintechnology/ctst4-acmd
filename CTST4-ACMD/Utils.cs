using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using ZeroMQ;

namespace CTST4_ACMD
{
    // not very fast -- plenty of locking ...
    public class ConcurrentDict<TKey, TValue> : ConcurrentDictionary<TKey, TValue>
    {
        public ConcurrentDict() : base() { }
        public ConcurrentDict(IDictionary<TKey, TValue> d) : base(d) { }

        public Func<TValue> Initializer { get; set; }

        protected readonly object _lock = new object();

        public new TValue this[TKey key]
        {
            get
            {
                lock (_lock)
                {
                    TValue val;
                    if (!TryGetValue(key, out val))
                    {
                        if (Initializer != null)
                        {
                            val = Initializer();
                        }
                        base[key] = val;
                    }
                    return val;
                }
            }
            set
            {
                base[key] = value;
            }
        }

        public TValue Pop(TKey key, TValue notFound)
        {
            TValue val;
            if (!TryRemove(key, out val))
            {
                return notFound;
            }
            return val;
        }

        public TValue Pop(TKey key)
        {
            TValue val;
            if (TryRemove(key, out val))
            {
                throw new KeyNotFoundException("key not found: " + key);
            }
            return val;
        }

        public TValue Get(TKey key)
        {
            return Get(key, default(TValue));
        }

        public TValue Get(TKey key, TValue notFound)
        {
            TValue val;
            if (TryGetValue(key, out val))
            {
                return val;
            }
            return notFound;
        }

        public ConcurrentDict<TKey, TValue> Update(IDictionary<TKey, TValue> other)
        {
            foreach (var kvp in other)
            {
                this[kvp.Key] = kvp.Value;
            }
            return this;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object other)
        {
            var o = other as IDictionary<TKey, TValue>;
            if (other == null)
                return false;
            if (this.Count != o.Count)
                return false;
            if (this.Except(o).Any())
                return false;
            return true;
        }
    }

    public class Dict<TKey, TValue> : Dictionary<TKey, TValue>
    {
        public Dict() : base() { }
        public Dict(IDictionary<TKey, TValue> d) : base(d) { }
        public Dict(Int32 initCapacity) : base(initCapacity) { }

        public Func<TValue> Initializer { get; set; }

        public new TValue this[TKey key]
        {
            get
            {
                TValue val;
                if (!TryGetValue(key, out val))
                {
                    if (Initializer != null)
                    {
                        val = Initializer();
                    }
                    Add(key, val);
                }
                return val;
            }
            set
            {
                base[key] = value;
            }
        }

        public TValue Pop(TKey key, TValue notFound)
        {
            TValue val;
            if (!TryGetValue(key, out val))
            {
                return notFound;
            }
            Remove(key);
            return val;
        }

        public TValue Pop(TKey key)
        {
            TValue val = this[key];
            Remove(key);
            return val;
        }

        public TValue Get(TKey key)
        {
            return Get(key, default(TValue));
        }

        public TValue Get(TKey key, TValue notFound)
        {
            TValue val;
            if (TryGetValue(key, out val))
            {
                return val;
            }
            return notFound;
        }

        public Dict<TKey, TValue> Update(IDictionary<TKey, TValue> other)
        {
            foreach (var kvp in other)
            {
                this[kvp.Key] = kvp.Value;
            }
            return this;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override bool Equals(object other)
        {
            var o = other as IDictionary<TKey, TValue>;
            if (other == null)
                return false;
            if (this.Count != o.Count)
                return false;
            if (this.Except(o).Any())
                return false;
            return true;
        }
    }

    public class ActionQueue
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
}
