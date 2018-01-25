using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ZeroMQ;

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

        static void Main(string[] args)
        {
            client = new T4.API.Host(
                    T4.APIServerType.Simulator,
                    "T4Example",
                    "112A04B0-5AAF-42F4-994E-FA7CB959C60B",
                    "CTS",
                    "LWalker",
                    "3qlp2FIZ");
            Console.WriteLine("connected");

        }
    }
}
