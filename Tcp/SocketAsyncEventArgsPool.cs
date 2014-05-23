using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

using System.Net.Sockets;
using System.Collections.Concurrent;

namespace netlib.Tcp
{
    public class CustomSocketAsyncEventArgs : SocketAsyncEventArgs
    {
        public long uid;
        static long uid_pool;
        public CustomSocketAsyncEventArgs()
        {
            uid = System.Threading.Interlocked.Increment(ref uid_pool);
        }
    }

    public class SocketAsyncEventArgsPool
    {
        private Stack<SocketAsyncEventArgs> pool;

        public delegate void EventArgsInitializer(SocketAsyncEventArgs s);
        private EventArgsInitializer _argsinit;

        /// <summary>
        /// Initializes the pool to the specified size.
        /// </summary>
        /// <param name="capacity">Maximum number of <see cref="System.Net.Sockets.SocketAsyncEventArgs"/>
        /// objects the pool can hold.</param>
        public SocketAsyncEventArgsPool(int capacity, EventArgsInitializer _argsinit)
        {
            pool = new Stack<SocketAsyncEventArgs>(capacity);
            this._argsinit = _argsinit;
        }

        /// <summary>
        /// Adds a <see cref="System.Net.Sockets.SocketAsyncEventArgs"/> instance to the pool.
        /// </summary>
        /// <param name="item">The <see cref="System.Net.Sockets.SocketAsyncEventArgs"/> instance
        /// to add to the pool.</param>
        /// <exception cref="ArgumentNullException">If <paramref name="item"/> is null.</exception>
        public void Push(SocketAsyncEventArgs item)
        {
            if (item == null)
                if (System.Diagnostics.Debugger.IsAttached)
                    System.Diagnostics.Debugger.Break();

            lock (pool)
            {
                pool.Push(item);
            }
        }

        /// <summary>
        /// Removes and returns a <see cref="System.Net.Sockets.SocketAsyncEventArgs"/> instance
        /// from the pool.
        /// </summary>
        /// <returns>An available <see cref="System.Net.Sockets.SocketAsyncEventArgs"/> instance
        /// in the pool.</returns>
        public SocketAsyncEventArgs Pop()
        {
            lock (pool)
            {
                if (pool.Count < 1)
                {
                    var n = new CustomSocketAsyncEventArgs();
                    _argsinit(n);
                    return n;
                }
                return pool.Pop();
            }
        }

        /// <summary>
        /// The number of <see cref="System.Net.Sockets.SocketAsyncEventArgs"/> instances
        /// available in the pool.
        /// </summary>
        public int Count
        {
            get { return pool.Count; }
        }
    }
}
