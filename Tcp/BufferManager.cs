using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace netlib.Tcp
{
    public class BufferManager
    {
        public volatile Object[] bufferBlocks;
        public volatile int currentblockbuffer = 0;

        volatile int index = 0;
        volatile int allocforSaea;
        int blocksize = 0;

        public BufferManager(int expectedinstances, int totalBufferBytesInEachSaeaObject)
        {
            this.bufferBlocks = new Object[1000];
            this.blocksize = totalBufferBytesInEachSaeaObject * expectedinstances;
            this.bufferBlocks[0] = new byte[blocksize];
            this.allocforSaea = totalBufferBytesInEachSaeaObject;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns>true if a buffer was assigned</returns>
        internal bool SetBuffer(SocketAsyncEventArgs args)
        {
            lock (this)
            {
                if (index + allocforSaea > blocksize)
                {
                    currentblockbuffer += 1;
                    bufferBlocks[currentblockbuffer] = new byte[blocksize];
                    index = 0;
                }

                var buf = (byte[])bufferBlocks[currentblockbuffer];

                args.SetBuffer(buf, index, allocforSaea);
                index += allocforSaea;

                return true;
            }
        }

        internal void FreeBuffer(SocketAsyncEventArgs args)
        {
            args.SetBuffer(null, 0, 0);
        }
    }
}