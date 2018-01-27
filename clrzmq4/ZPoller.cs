using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;


namespace ZeroMQ
{
    using lib;
    using System.Diagnostics;

    public class ZPollItem
    {
        public ZSocket socket;
        public short events;

        public ZPollItem(ZSocket socket, short events)
        {
            this.socket = socket;
            this.events = events;
        }
    }
    
    unsafe public class ZPoller
    {
        public const int POLLIN = 1;
        public const int POLLOUT = 2;
        public const int POLLERR = 3;

        List<ZPollItem> items = new List<ZPollItem>();
        zmq_pollitem_windows_t* natItems = null;

        void PopulateNativeItems()
        {
            if (natItems != null)
            {
                Marshal.FreeHGlobal(new IntPtr(natItems));
            }
            int sizeOne = Marshal.SizeOf(typeof(zmq_pollitem_windows_t));
            natItems = (zmq_pollitem_windows_t*)Marshal.AllocHGlobal(sizeOne * items.Count)
                    .ToPointer();
            for (int i = 0; i < items.Count; i++)
            {
                natItems[i].SocketPtr = items[i].socket.SocketPtr;
                natItems[i].Events = items[i].events;
                natItems[i].ReadyEvents = 0;
            }
        }

        public void Register(ZSocket socket, short events)
        {
            items.Add(new ZPollItem(socket, events));
            PopulateNativeItems();
        }

        public void Unregister(ZSocket socket)
        {
            int numRemoved = items.RemoveAll((x) => x.socket == socket);
            if (numRemoved <= 0)
            {
                throw new Exception("socket doesn't exist");
            }
            Debug.Assert(numRemoved == 1);
            PopulateNativeItems();
        }

        public List<ZPollItem> Poll(long timeout)
        {
            int rc = zmq.poll(natItems, items.Count, timeout);
            if (rc < 0)
            {
                throw new ZException(ZError.GetLastErr());
            }
            List<ZPollItem> res = new List<ZPollItem>();
            for (int i = 0; i < items.Count; i++)
            {
                if (natItems[i].ReadyEvents > 0)
                {
                    res.Add(new ZPollItem(items[i].socket, natItems[i].ReadyEvents));
                }
            }
            return res;
        }

        public List<ZPollItem> Poll()
        {
            return Poll(-1);
        }

        //public void Test(List<ZSocket> sockets)
        //{
        //    int sizeOne = Marshal.SizeOf(typeof(zmq_pollitem_windows_t));
        //    byte* p = (byte*)Marshal.AllocHGlobal(sizeOne * sockets.Count).ToPointer();
        //    for (int i = 0; i < sizeOne; i++)
        //    {
        //        *(p + i) = 0;
        //    }
        //    zmq_pollitem_windows_t* items = (zmq_pollitem_windows_t*)p;
        //    for (int i = 0; i < sockets.Count; i++)
        //    {
        //        (items + i)->SocketPtr = sockets[i].SocketPtr;
        //        (items + i)->Events = 1;
        //        (items + i)->ReadyEvents = 0;
        //    }
        //    Console.WriteLine("polling ...");
        //    int pollRes = zmq.poll(items, sockets.Count, 100000000);
        //    if (pollRes == -1)
        //    {
        //        throw new Exception(ZError.GetLastErr().ToString());
        //    }
        //    Console.WriteLine("poll result: " + pollRes + " events");
        //    for (int i = 0; i < sockets.Count; i++)
        //    {
        //        short rev = (items + i)->ReadyEvents;
        //        Console.WriteLine(i + ": " + rev);
        //    }
        //    Marshal.FreeHGlobal(new IntPtr(items));
        //}

        //    while (!(result = (-1 != zmq.poll(natives, count, timeoutMs))))
        //    {
        //        error = ZError.GetLastErr();

        //        // No Signalling on Windows
        //        /* if (error == ZmqError.EINTR) {
        //            error = ZmqError.DEFAULT;
        //            continue;
        //        } */
        //        break;
        //    }

        //    for (int i = 0; i < count; ++i)
        //    {
        //        ZPollItem item = items.ElementAt(i);
        //        zmq_pollitem_windows_t* native = natives + i;

        //        item.ReadyEvents = (ZPoll)native->ReadyEvents;
        //    }


        //public void Poll(int timeout)
        //{
        //    if (items.Count == 0)
        //        return;

        //    if (dirty) {
        //        if (pItems != null)
        //        {
        //            foreach (zmq_pollitem_windows_t item in items)
        //            {
        //                IntPtr p1 = Marshal.AllocHGlobal(sizeof(zmq_pollitem_windows_t));
        //                Marshal.StructureToPtr(item, p1, false);
        //                IntPtr p2 = Marshal.AllocHGlobal(8 * items.Count);
                        
        //            }
        //            GCHandle handle = GCHandle.Alloc(items.ToArray(), GCHandleType.Pinned);
        //            IntPtr p = handle.AddrOfPinnedObject();
        //            Marshal.PtrToStructure(p, typeof(zmq_pollitem_windows_t));

        //            //Marshal.DestroyStructure
        //            //Marshal.AllocHGlobal(sizeof(zmq_pollitem_windows_t) * items.Count);
        //        }
        //        //pItems = zmq_pollitem_posix_t[items.Count];
        //        dirty = false;
        //    }

        //    while (true)
        //    {
        //        zmq.poll(pItems, 
        //    }
        //}


        
        //class PollDefinition
        //{
        //    public ZSocket socket;
        //    public short events;
        //    public short readyEvents;
        //}

        //List<PollDefinition> pollDefs = new List<PollDefinition>();

        //public ZPoller()
        //{
            
        //}


        //unsafe public void Poll(int timeout)
        //{
            
        //    // will create a native object from c# object on every call to poll, possibly slow
        //    zmq_pollitem_windows_t* p = stackalloc zmq_pollitem_windows_t[pollDefs.Count];
        //    for (int i = 0; i < pollDefs.Count; i++)
        //    {
        //        PollDefinition d = pollDefs[i];
        //        p->SocketPtr = d.socket.SocketPtr;
        //        p->Events = d.events;
        //        p->ReadyEvents = d.readyEvents;
        //    }
        //    while (true)
        //    {
        //        zmq.poll(
        //    }

        //}
    }
}



            //unsafe internal static bool PollMany(
            //    IEnumerable<ZSocket> sockets, 
            //    IEnumerable<ZPollItem> items, ZPoll pollEvents, 
            //    out ZError error, TimeSpan? timeout = null)
            //{
            //    error = default(ZError);
            //    bool result = false;
            //    int count = items.Count();
            //    int timeoutMs = !timeout.HasValue ? -1 : (int)timeout.Value.TotalMilliseconds;

            //    zmq_pollitem_windows_t* natives = stackalloc zmq_pollitem_windows_t[count];
            //    // fixed (zmq_pollitem_windows_t* natives = managedArray) {

            //    for (int i = 0; i < count; ++i)
            //    {
            //        ZSocket socket = sockets.ElementAt(i);
            //        ZPollItem item = items.ElementAt(i);
            //        zmq_pollitem_windows_t* native = natives + i;

            //        native->SocketPtr = socket.SocketPtr;
            //        native->Events = (short)(item.Events & pollEvents);
            //        native->ReadyEvents = (short)ZPoll.None;
            //    }

            //    while (!(result = (-1 != zmq.poll(natives, count, timeoutMs))))
            //    {
            //        error = ZError.GetLastErr();

            //        // No Signalling on Windows
            //        /* if (error == ZmqError.EINTR) {
            //            error = ZmqError.DEFAULT;
            //            continue;
            //        } */
            //        break;
            //    }

            //    for (int i = 0; i < count; ++i)
            //    {
            //        ZPollItem item = items.ElementAt(i);
            //        zmq_pollitem_windows_t* native = natives + i;

            //        item.ReadyEvents = (ZPoll)native->ReadyEvents;
            //    }
            //    // }

            //    return result;
            //}