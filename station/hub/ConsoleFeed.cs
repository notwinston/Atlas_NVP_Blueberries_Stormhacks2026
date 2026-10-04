using System.Collections.Generic;
using System.Threading.Channels;

namespace Atlas.Station
{
    /// <summary>
    /// The browsers connected to <c>/ws</c>. The link loop hands each message over as finished JSON text and
    /// never waits for a socket: every Console has its own bounded queue, and a Console that cannot keep up
    /// loses its oldest messages rather than slowing the relay (Master Plan section 7.5, rule 2).
    /// </summary>
    public sealed class ConsoleFeed
    {
        /// <summary>One browser.</summary>
        public sealed class Client
        {
            internal readonly Channel<string> Queue = Channel.CreateBounded<string>(
                new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

            public ChannelReader<string> Reader { get { return Queue.Reader; } }

            /// <summary>Queues a message for this Console only.</summary>
            public void Send(string json) { Queue.Writer.TryWrite(json); }
        }

        readonly object _lock = new object();
        readonly List<Client> _clients = new List<Client>();
        volatile int _count;

        /// <summary>Connected Consoles. Read on the link loop to skip building messages nobody receives.</summary>
        public int Count { get { return _count; } }

        public Client Add()
        {
            var client = new Client();
            lock (_lock)
            {
                _clients.Add(client);
                _count = _clients.Count;
            }
            return client;
        }

        public void Remove(Client client)
        {
            lock (_lock)
            {
                _clients.Remove(client);
                _count = _clients.Count;
            }
            client.Queue.Writer.TryComplete();
        }

        public void Broadcast(string json)
        {
            if (_count == 0) return;
            lock (_lock)
            {
                for (int i = 0; i < _clients.Count; i++) _clients[i].Queue.Writer.TryWrite(json);
            }
        }
    }
}
