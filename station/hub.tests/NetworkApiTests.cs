using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Atlas.Station.Tests
{
    /// <summary>GET /api/network (contract network.md 3.2): the bundled atlas.network/1 files.</summary>
    [TestFixture]
    public class NetworkApiTests
    {
        TestHub _t;
        string _folder;

        [SetUp]
        public async Task SetUp()
        {
            _t = await TestHub.StartAsync();
            _folder = Path.Combine(_t.Hub.Options.ProjectFolder, "Assets", "StreamingAssets", "networks");
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Path.Combine(_folder, "network_83.json"), "{\"seed\":83}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_folder, "network_7.json"), "{\"seed\":7}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_folder, "notes.txt"), "x");
        }

        [TearDown]
        public async Task TearDown() { await _t.DisposeAsync(); }

        [Test]
        public async Task List_ReturnsBundledNetworks()
        {
            Atlas.Link.JsonValue body = await _t.GetJson("/api/network");
            Atlas.Link.JsonValue files = body["files"];
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].AsString(), Is.EqualTo("network_7.json"));
            Assert.That(files[1].AsString(), Is.EqualTo("network_83.json"));
        }

        [Test]
        public async Task Get_ReturnsBundledFileBytes()
        {
            using (HttpResponseMessage r = await _t.Http.GetAsync("/api/network?file=network_7.json"))
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(200));
                Assert.That(r.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
                Assert.That(await r.Content.ReadAsByteArrayAsync(), Is.EqualTo(File.ReadAllBytes(Path.Combine(_folder, "network_7.json"))));
            }
        }

        [Test]
        public async Task Get_RejectsBadOrUnknownFile()
        {
            Atlas.Link.JsonValue bad = await _t.GetJson("/api/network?file=..%2Fsecret.json", 400);
            Assert.That(bad["error"].AsString(), Is.Not.Empty);
            await _t.GetJson("/api/network?file=network_x.json", 400);
            await _t.GetJson("/api/network?file=network_99.json", 404);
        }
    }
}
