namespace ZeroMQ.lib
{
	using System;
	using System.Runtime.InteropServices;

	[StructLayout(LayoutKind.Sequential)]
	public struct zmq_pollitem_posix_t // : zmq_pollitem_i
	{
		private IntPtr socketPtr;
		private int fileDescriptor; // POSIX fd is an Int32
		private short events;
		private short readyEvents;

		public IntPtr SocketPtr
		{
			get { return socketPtr; }
			set { socketPtr = value; }
		}

		public int FileDescriptor
		{
			get { return fileDescriptor; }
			set { fileDescriptor = value; }
		}

		public short Events
		{
			get { return events; }
			set { events = value; }
		}

		public short ReadyEvents
		{
			get { return readyEvents; }
			set { readyEvents = value; }
		}
	}
}