using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atlas.Link
{
    /// <summary>
    /// The world hash (Master Plan section 7.3.1): 16 hexadecimal digits that name the world a client holds. The
    /// phone puts it into <c>Hello</c> and into every <c>world</c> event; the Spectator computes the same from
    /// its own kernel, room and space, and draws nothing while the two differ ("world mismatch").
    /// <para>
    /// The hash is the first 16 lower-case hexadecimal digits of the SHA-256 of the UTF-8 text
    /// <c>&lt;spaceId&gt;|4,&lt;q&gt;|&lt;seed&gt;|&lt;generatorVersion&gt;|&lt;geometryHash&gt;|&lt;lMillimetres&gt;</c>, every number in
    /// decimal. It is computed when the world changes, never per frame.
    /// </para>
    /// </summary>
    public static class WorldHash
    {
        /// <summary>Digits of a world hash.</summary>
        public const int Length = 16;

        /// <summary>The geometry hash to use while no RoomModel is active: 64 zeros.</summary>
        public const string NoGeometry = "0000000000000000000000000000000000000000000000000000000000000000";

        /// <summary>
        /// The world hash.
        /// </summary>
        /// <param name="spaceId"><c>State.spaceId</c>.</param>
        /// <param name="q">Tiles round a vertex of the {4, q} tiling the walker's kernel uses.</param>
        /// <param name="seed">The world generator's seed.</param>
        /// <param name="generatorVersion">Version of the world generator; 1 in Phase 1.</param>
        /// <param name="geometryHash">
        /// The RoomModel's geometry hash, 64 lower-case hexadecimal digits. Null or empty stands for
        /// <see cref="NoGeometry"/>.
        /// </param>
        /// <param name="lMillimetres">Metres per kernel unit in millimetres; see <see cref="Millimetres"/>.</param>
        public static string Compute(int spaceId, int q, long seed, int generatorVersion, string geometryHash, int lMillimetres)
        {
            byte[] text = Encoding.UTF8.GetBytes(Text(spaceId, q, seed, generatorVersion, geometryHash, lMillimetres));
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(text);
            var hex = new char[Length];
            for (int i = 0; i < Length / 2; i++)
            {
                hex[i * 2] = HexDigit(digest[i] >> 4);
                hex[i * 2 + 1] = HexDigit(digest[i] & 0xF);
            }
            return new string(hex);
        }

        /// <summary>The text that is hashed, for a log line or a mismatch report.</summary>
        public static string Text(int spaceId, int q, long seed, int generatorVersion, string geometryHash, int lMillimetres)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            return spaceId.ToString(c) + "|4," + q.ToString(c) + "|" + seed.ToString(c) + "|" + generatorVersion.ToString(c) + "|" +
                   (string.IsNullOrEmpty(geometryHash) ? NoGeometry : geometryHash) + "|" + lMillimetres.ToString(c);
        }

        /// <summary>
        /// Metres per kernel unit as the whole millimetres that go into the hash, rounded half away from zero so
        /// that the phone and the Spectator agree on every runtime.
        /// </summary>
        public static int Millimetres(double metresPerUnit)
        {
            return (int)Math.Round(metresPerUnit * 1000.0, MidpointRounding.AwayFromZero);
        }

        static char HexDigit(int value) { return (char)(value < 10 ? '0' + value : 'a' + value - 10); }
    }
}
