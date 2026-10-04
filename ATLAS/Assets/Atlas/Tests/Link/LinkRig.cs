using System.Collections.Generic;

namespace Atlas.Link.Tests
{
    /// <summary>
    /// A Hub, a phone and optional extra clients on one in-memory network with one manual clock, stepped like a
    /// frame loop. Shared by the exchange and watchdog tests.
    /// </summary>
    sealed class LinkRig
    {
        public const int Port = 9050;

        public readonly ManualClock Clock = new ManualClock(1000000);
        public readonly LoopbackNetwork Network;
        public readonly LinkHost Host;
        public readonly ParamRegistry Registry = new ParamRegistry();
        public readonly LinkClient Phone;
        public readonly List<LinkClient> Others = new List<LinkClient>();
        public readonly List<LinkState> PhoneStates = new List<LinkState>();

        public ParamEntry Rho, Copies, Predict;

        public LinkRig(int seed = 1)
        {
            Network = new LoopbackNetwork(Clock, seed);
            Host = new LinkHost(Network.CreateEndpoint(), Clock) { SessionId = "s-test", ForgeUrl = "http://10.0.0.2:8000" };
            Rho = Registry.DeclareFloat("curved.cullRho", "curved", 2f, 3.5f, 3f);
            Copies = Registry.DeclareInt("glued.copies", "glued", 3, 5, 5);
            Predict = Registry.DeclareBool("pose.gyroPredict", "pose", true);
            Phone = new LinkClient(Network.CreateEndpoint(), Clock, LinkRole.Phone, Registry) { Device = "test-phone", Scene = "S0" };
            Phone.StateChanged += (previous, current) => PhoneStates.Add(current);
        }

        public LinkClient AddClient(LinkRole role)
        {
            var client = new LinkClient(Network.CreateEndpoint(), Clock, role);
            Others.Add(client);
            return client;
        }

        public void StartAll()
        {
            Host.Start(Port);
            Phone.Start("10.0.0.2", Port);
            for (int i = 0; i < Others.Count; i++) Others[i].Start("10.0.0.2", Port);
        }

        public void Step(double stepMs = 5.0)
        {
            Clock.AdvanceMs(stepMs);
            Host.Poll();
            Phone.Poll();
            for (int i = 0; i < Others.Count; i++) Others[i].Poll();
            // A second Hub poll relays what the clients just sent, as its own loop would within a millisecond.
            Host.Poll();
        }

        public void Run(double ms, double stepMs = 5.0)
        {
            int steps = (int)System.Math.Round(ms / stepMs);
            for (int i = 0; i < steps; i++) Step(stepMs);
        }

        /// <summary>Starts everything and runs until the handshakes are done.</summary>
        public void Connect()
        {
            StartAll();
            Run(1500);
        }
    }
}
