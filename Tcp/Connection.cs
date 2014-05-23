using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace netlib.Tcp
{
    public class Connection
    {
        public Object token;

        public SocketAsyncEventArgsPool asyncpool_buffered;
        public SocketAsyncEventArgsPool asyncpool;

        public Socket socket;

        public long Id;
        private static long idpool;

        internal int connected = 0;
        private int disconnectraised = 0;

        public delegate void onread(Connection conn, byte[] buf, int index, int readBytes);
        public onread OnRead = (con, buf, index, readBytes) =>
        {

        };

        public delegate void onconnect(Connection conn, bool sucess);
        public onconnect OnConnect = (conn, sucess) =>
        {

        };

        public delegate void ondisconnect(Connection conn);
        public ondisconnect OnDisconnect = (conn) =>
        {

        };

        /// <summary>
        /// tryes to start a connection to the host/port
        /// </summary>
        /// <returns>true if conn pending</returns>
        public bool Connect(string ip, int port)
        {
            IPAddress val;
            if (!IPAddress.TryParse(ip, out val))
                return false;

            IPEndPoint ipEnd = new IPEndPoint(val, port);

            return Connect(ipEnd);
        }

        public bool Connect(EndPoint ipEnd)
        {
            var asyncargs = asyncpool.Pop();

            var token = new usertoken(this, null);
            asyncargs.RemoteEndPoint = ipEnd;
            asyncargs.UserToken = token;
            try
            {
                bool willRaiseEvent = socket.ConnectAsync(asyncargs);
                if (!willRaiseEvent)
                    ProcessConnect(asyncargs);
                return true;
            }
            catch
            {
                asyncargs.UserToken = null;
                asyncpool.Push(asyncargs);
            }
            return false;
        }

        public int Send(byte[] buffer)
        {
            var hastosend = false;
            lock (sendlock)
            {
                if (!sending)
                {
                    sending = true;
                    hastosend = true;
                }
                else
                {
                    queuedsends.Enqueue(buffer);
                }
            }

            if (hastosend)
                InternalSend(buffer);

            return buffer.Count();
        }

        private void InternalSend(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (!this.socket.Connected)
            {
                return;
            }

            SocketAsyncEventArgs seae = asyncpool.Pop();

            System.Threading.Interlocked.Increment(ref concurrentsends);

            try
            {
                var token = new usertoken(this, buffer);
                seae.UserToken = token;
                seae.SetBuffer(buffer, 0, buffer.Length);
                System.Threading.Thread.MemoryBarrier();
                bool willRaiseEvent = this.socket.SendAsync(seae);
                if (!willRaiseEvent)
                {
                    ProcessSend(seae);
                }
            }
            catch (Exception excp)
            {
                Disconnect();
                asyncpool.Push(seae);
            }
        }

        public static void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                var token = (e.UserToken as usertoken);
                Connection conn = null;
                if (token != null)
                    conn = token.client;

                switch (e.LastOperation)
                {
                    case SocketAsyncOperation.Connect:
                        conn.ProcessConnect(e);
                        break;
                    case SocketAsyncOperation.Receive:
                        conn.ProcessReceive(e);
                        break;
                    case SocketAsyncOperation.Send:
                        conn.ProcessSend(e);
                        break;
                    case SocketAsyncOperation.Disconnect:
                        conn.ProcessDisconnect(e);
                        break;
                    default:
                        throw new ArgumentException("The last operation completed on the socket was not a receive or send");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("internal exception {0}\r\n{1}\r\n", ex, ex.Message);
            }
        }

        public void ProcessSend(SocketAsyncEventArgs e)
        {
            var token = (e.UserToken as usertoken);
            var conn = token.client;
            if (e.SocketError == SocketError.Success)
            {
                if (token.buffer.Length > e.BytesTransferred)
                {

                }

                //asume that it happily sended all the data
                conn.NextSend();

                e.UserToken = null;
                e.SetBuffer(null, 0, 0);
                asyncpool.Push(e);
            }
            else
            {
                Disconnect();

                e.UserToken = null;
                e.SetBuffer(null, 0, 0);
                asyncpool.Push(e);
            }
        }

        private void NextSend()
        {
            byte[] next = null;
            lock (sendlock)
            {
                if (queuedsends.Count == 0)
                {
                    System.Threading.Thread.MemoryBarrier();
                    sending = false;
                    return;
                }
                next = queuedsends.Dequeue();
            }

            //this thread has the lock of sending = true;
            InternalSend(next);
        }

        private int concurrentsends = 0;
        private readonly Object sendlock = new object();
        private volatile bool sending = false;
        private Queue<byte[]> queuedsends = new Queue<byte[]>();

        public Connection(Socket _socket)
        {
            this.Id = System.Threading.Interlocked.Increment(ref idpool);
            this.socket = _socket;
        }

        private long seae_id = 0;

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            var token = (e.UserToken as usertoken);
            var conn = token.client;
            if ((e.SocketError == SocketError.Success) && (e.BytesTransferred > 0))
            {
                //requeue
                try
                {
                    conn.OnRead(conn, e.Buffer, e.Offset, e.BytesTransferred);

                    var willraise = socket.ReceiveAsync(e);
                    if (!willraise)
                        ProcessReceive(e);
                    return;
                }
                catch (Exception ex)
                {
                    //error.. close conn?
                    Console.WriteLine("internal exception {0}\r\n{1}\r\n", ex, ex.Message);
                }
            }

            Disconnect();

            e.UserToken = null;
            conn.asyncpool_buffered.Push(e);
        }

        private void ProcessDisconnect(SocketAsyncEventArgs e)
        {
            var conn = (e.UserToken as usertoken).client;

            try
            {
                OnDisconnect(this);
                socket.Dispose();
            }
            catch
            {
            }

            e.UserToken = null;
            conn.asyncpool.Push(e);
        }

        private void ProcessConnect(SocketAsyncEventArgs e)
        {
            var token = (e.UserToken as usertoken);
            var conn = token.client;
            try
            {
                conn.OnConnect(conn, (e.ConnectSocket != null));
                if (e.ConnectSocket != null)
                {
                    var e2 = asyncpool_buffered.Pop();
                    Interlocked.Exchange(ref seae_id, (e2 as CustomSocketAsyncEventArgs).uid);
                    e2.UserToken = token;

                    var willraise = socket.ReceiveAsync(e2);
                    if (!willraise)
                        ProcessReceive(e2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("internal exception {0}\r\n{1}\r\n", ex, ex.Message);
            }

            e.UserToken = null;
            conn.asyncpool.Push(e);
        }
        
        public void Disconnect()
        {
            if (Interlocked.CompareExchange(ref disconnectraised, 1, 0) == 0)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                }
                catch
                {
                }
                try
                {
                    socket.Close();
                }
                catch
                {
                }
        
                try
                {
                    OnDisconnect(this);
                }
                catch
                {
                }

                /*
                SocketAsyncEventArgs seae = asyncpool.Pop();
                seae.UserToken = new usertoken(this, null);
                try
                {
                    bool willRaise = socket.DisconnectAsync(seae);
                    if (!willRaise)
                    {
                        ProcessDisconnect(seae);
                    }
                }
                catch
                {
                    seae.UserToken = null;
                    asyncpool.Push(seae);
                }
                */
            }
        }

        public static string ToHexString(byte[] data)
        {
            var dataLength = data.Count();
            string result = "";

            for (int i = 0; i < dataLength; i++)
            {
                result += data[i].ToString("X2");
            }
            return result;
        }

        public static byte[] HexStringToByte(string hexString)
        {
            byte[] HexAsBytes = new byte[hexString.Length / 2];
            for (int index = 0; index < HexAsBytes.Length; index++)
            {
                string byteValue = hexString.Substring(index * 2, 2);
                HexAsBytes[index] = byte.Parse(byteValue, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            }

            return HexAsBytes;
        }

        internal void OnAccept()
        {
            try
            {
                OnConnect(this, true);

                var e2 = asyncpool_buffered.Pop();

                Interlocked.Exchange(ref seae_id, (e2 as CustomSocketAsyncEventArgs).uid);

                e2.UserToken = new usertoken(this, e2.Buffer);

                var willraise = socket.ReceiveAsync(e2);
                if (!willraise)
                    ProcessReceive(e2);

            }
            catch (Exception ex)
            {
                Console.WriteLine("internal exception {0}\r\n{1}\r\n", ex, ex.Message);
            }
        }
    }
}
