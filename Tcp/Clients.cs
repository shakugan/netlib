using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net;
using System.Net.Sockets;

/*
 * TCPEngine.ClientsPool
 * | SocketAsyncEventArgsPool
 * | TCPEngine.Connection
*/

namespace netlib.Tcp
{
    public class ClientsPool
    {
        SocketAsyncEventArgsPool asyncpool;
        SocketAsyncEventArgsPool asyncpool_buffered;
        public BufferManager buffManager;

        public ClientsPool()
        {
            asyncpool = new SocketAsyncEventArgsPool(1000, (t) => {
                t.Completed += new EventHandler<SocketAsyncEventArgs>(Connection.IO_Completed);
            });

            buffManager = new BufferManager(2000, 4096);

            asyncpool_buffered = new SocketAsyncEventArgsPool(1000, (t) => {
                t.Completed += new EventHandler<SocketAsyncEventArgs>(Connection.IO_Completed);
                buffManager.SetBuffer(t);
            });
        }

        public Connection newClient()
        {
            var _socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
            var n = new Connection(_socket);
            n.asyncpool = asyncpool;
            n.asyncpool_buffered = asyncpool_buffered;
            return n;
        }
    }
}
