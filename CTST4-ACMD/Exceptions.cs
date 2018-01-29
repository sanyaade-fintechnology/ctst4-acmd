using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CTST4_ACMD
{
    public class CodecException : Exception
    {
        public CodecException() : base() { }
        public CodecException(string msg) : base(msg) { }
    }

    public class DecodingException : Exception
    {
        public DecodingException() : base() { }
        public DecodingException(string msg) : base(msg) { }
    }

    public class CommandNotImplementedException : Exception
    {
        public CommandNotImplementedException() : base() { }
        public CommandNotImplementedException(string msg) : base(msg) { }
    }
}
