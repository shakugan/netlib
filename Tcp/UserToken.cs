using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace netlib.Tcp
{
    public class usertoken
    {
        public int uid = 0;
        private static int uid_pool = 0;
        public volatile Connection client;
        public volatile byte[] buffer;
        public usertoken(Connection client, byte[] buffer)
        {
            this.client = client;
            this.buffer = buffer;
            this.uid = System.Threading.Interlocked.Increment(ref uid_pool);
        }
    }
}
