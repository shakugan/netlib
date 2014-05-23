using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace netlib.Tcp
{
    public class Server
    {
        private int m_numConnections;   // the maximum number of connections the sample is designed to handle simultaneously  
        private int m_receiveBufferSize;// buffer size to use for each socket I/O operation 
        const int opsToPreAlloc = 2;    // read, write (don't alloc buffer space for accepts)
        Socket listenSocket;            // the socket used to listen for incoming connection requests 
        // pool of reusable SocketAsyncEventArgs objects for write, read and accept socket operations
        private readonly SocketAsyncEventArgs acceptArgs = new SocketAsyncEventArgs();
        int m_totalBytesRead;           // counter of the total # bytes received by the server 
        int m_numConnectedSockets;      // the total number of clients connected to the server 
        Semaphore m_maxNumberAcceptedClients;


        private SocketAsyncEventArgsPool asyncpool;
        private SocketAsyncEventArgsPool asyncpool_buffered;
        private BufferManager buffManager;

        public Server(int numConnections = 1000, int receiveBufferSize = 4096)
        {
            this.m_numConnectedSockets = 0;
            this.m_numConnections = numConnections;
            this.m_receiveBufferSize = receiveBufferSize;

            asyncpool = new SocketAsyncEventArgsPool(numConnections, (t) =>
            {
                t.Completed += new EventHandler<SocketAsyncEventArgs>(Connection.IO_Completed);
            });

            buffManager = new BufferManager(numConnections, receiveBufferSize);

            asyncpool_buffered = new SocketAsyncEventArgsPool(numConnections, (t) =>
            {
                t.Completed += new EventHandler<SocketAsyncEventArgs>(Connection.IO_Completed);
                buffManager.SetBuffer(t);
            });
        }

        public delegate Connection newconnection(Server serv, Socket socket);
        public newconnection ConnectionFactory = (Server, socket) => 
        {
            var n = new Connection(socket);
            return n;
        };

        // Starts the server such that it is listening for  
        // incoming connection requests.     
        // 
        // <param name="localEndPoint">The endpoint which the server will listening 
        // for connection requests on</param> 


        public void Start(IPEndPoint localEndPoint, params SocketOptionName[] options)
        {
            // create the socket which listens for incoming connections
            listenSocket = new Socket(localEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            foreach (SocketOptionName option in options)
                listenSocket.SetSocketOption(SocketOptionLevel.Tcp, option, true);

            listenSocket.Bind(localEndPoint);
            // start the server with a listen backlog of 100 connections
            listenSocket.Listen(20);

            //  this.acceptArgs.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptCallback);
            //    if (!this.listenSocket.AcceptAsync(this.acceptArgs))
            //       AcceptCallback(null, this.acceptArgs);

            // post accepts on the listening socket
            for (int i = 0; i < 4; i++)
            {
                StartAccept(null);
            }

            //Console.WriteLine("{0} connected sockets with one outstanding receive posted to each....press any key", m_outstandingReadCount);
            Console.WriteLine("TPCEngine Server Listening on " + localEndPoint.Address + ":" + localEndPoint.Port);
        }
        public List<SocketAsyncEventArgs> acceptpool = new List<SocketAsyncEventArgs>();

        private void AcceptCallback(object sender, SocketAsyncEventArgs args)
        {
            try
            {
                //    var session = sessionCreator();
                var socket = args.AcceptSocket;
                //    session.SetSocket(socket);
                args.AcceptSocket = null;
                if (!listenSocket.AcceptAsync(args))
                    AcceptCallback(null, args);
            }
            catch (Exception e)
            {
#if LOGER
                HiveCluster3.Logger.ston.LogException(e);
#endif
            }
        }
        // Begins an operation to accept a connection request from the client  
        // 
        // <param name="acceptEventArg">The context object to use when issuing 
        // the accept operation on the server's listening socket</param> 
        public void StartAccept(SocketAsyncEventArgs acceptEventArg)
        {
            if (acceptEventArg == null)
            {
                acceptEventArg = new SocketAsyncEventArgs();
                acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(AcceptEventArg_Completed);
                acceptpool.Add(acceptEventArg);
            }
            
            
            // socket must be cleared since the context object is being reused
            acceptEventArg.AcceptSocket = null;

            //   m_maxNumberAcceptedClients.WaitOne();
            bool willRaiseEvent = listenSocket.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                ProcessAccept(acceptEventArg);
            }
        }

        // This method is the callback method associated with Socket.AcceptAsync  
        // operations and is invoked when an accept operation is complete 
        // 
        void AcceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            ProcessAccept(e);
        }

        private void ProcessAccept(SocketAsyncEventArgs e)
        {
            try
            {
                Interlocked.Increment(ref m_numConnectedSockets);

                var conn = ConnectionFactory(this, e.AcceptSocket);

                conn.asyncpool = asyncpool;
                conn.asyncpool_buffered = asyncpool_buffered;

                //set connected to 1
                Interlocked.Exchange(ref conn.connected, 1);

                conn.OnAccept();

            }
            catch (Exception exp)
            {
                Console.WriteLine("exception {0}\r\n{1}\r\n",exp, exp.Message);
            }

            // Accept the next connection request
            StartAccept(e);
        }

        // This method is called whenever a receive or send operation is completed on a socket  
        // 
        // <param name="e">SocketAsyncEventArg associated with the completed receive operation</param>
        void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            // determine which type of operation just completed and call the associated handler 
            switch (e.LastOperation)
            {
                default:
                    throw new ArgumentException("The last operation completed on the socket was not a receive or send");
            }
        }

        public void CloseClientConnection(Connection e)
        {
            if (Interlocked.Exchange(ref e.connected, 0) == 1)
            {
                Interlocked.Decrement(ref m_numConnectedSockets);
#if LOGGER
                HiveCluster3.Logger.ston.LogLine("Closing Connection: ", HiveCluster3.LogType.Server);
#endif
                CloseClientSocket(e.socket);

                e.OnDisconnect(e);
            }
        }

        public void CloseClientSocket(Socket e)
        {
#if LOGGER
            HiveCluster3.Logger.ston.LogLine(" -dc " + e.RemoteEndPoint, HiveCluster3.LogType.Server);
#endif

            // close the socket associated with the client 

            // close the socket associated with the client 
            try
            {
                e.Shutdown(SocketShutdown.Both);
                //   e.Shutdown(SocketShutdown.Receive);
            }
            // throws if client process has already closed 
            catch (Exception) { }
            e.Close();

            // decrement the counter keeping track of the total number of clients connected to the server
            // m_maxNumberAcceptedClients.Release();

            // Free the SocketAsyncEventArg so they can be reused by another client
        }
    }
}
