using System;
using System.Globalization;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Atlas.Link.Tests
{
    public sealed class AtlasJsonTests
    {
        [Test]
        public void Parses_every_kind()
        {
            JsonValue v = AtlasJson.Parse(" { \"a\" : null, \"b\": true, \"c\": false, \"d\": -12, \"e\": 1.5e2, \"f\": \"x\", \"g\": [1, [2], {}], \"h\": {\"k\": 1} } ");
            Assert.That(v.IsObject);
            Assert.That(v.Count, Is.EqualTo(8));
            Assert.That(v["a"].IsNull);
            Assert.That(v.Has("a"));
            Assert.That(v["b"].AsBool(), Is.True);
            Assert.That(v["c"].AsBool(true), Is.False);
            Assert.That(v["d"].AsInt(), Is.EqualTo(-12));
            Assert.That(v["d"].IsInteger);
            Assert.That(v["e"].AsDouble(), Is.EqualTo(150.0));
            Assert.That(v["e"].IsInteger, Is.False);
            Assert.That(v["f"].AsString(), Is.EqualTo("x"));
            Assert.That(v["g"].IsArray);
            Assert.That(v["g"].Count, Is.EqualTo(3));
            Assert.That(v["g"][1][0].AsInt(), Is.EqualTo(2));
            Assert.That(v["g"][2].IsObject);
            Assert.That(v["h"]["k"].AsLong(), Is.EqualTo(1L));
            Assert.That(v.KeyAt(3), Is.EqualTo("d"));
        }

        [Test]
        public void Missing_keys_and_wrong_kinds_fall_back()
        {
            JsonValue v = AtlasJson.Parse("{\"n\": 3, \"s\": \"text\", \"a\": [1]}");
            Assert.That(v["nope"].IsNull);
            Assert.That(v.Has("nope"), Is.False);
            Assert.That(v["nope"]["deeper"][4].AsFloat(7f), Is.EqualTo(7f));
            Assert.That(v["a"][5].AsInt(9), Is.EqualTo(9));
            Assert.That(v["a"][-1].IsNull);
            Assert.That(v["s"].AsInt(4), Is.EqualTo(4));
            Assert.That(v["n"].AsString("fallback"), Is.EqualTo("fallback"));
            Assert.That(v["n"].AsBool(true), Is.True);
            Assert.That(v.GetFloat("n"), Is.EqualTo(3f));
            Assert.That(v.GetString("s"), Is.EqualTo("text"));
            Assert.That(v.GetInt("missing", -1), Is.EqualTo(-1));
            JsonValue got;
            Assert.That(v.TryGet("n", out got), Is.True);
            Assert.That(v.TryGet("zz", out got), Is.False);
            Assert.That(got.IsNull);
        }

        [Test]
        public void Builders_write_compact_json_in_insertion_order()
        {
            JsonValue v = JsonValue.Object()
                .Set("z", 1)
                .Set("a", "two")
                .Set("list", JsonValue.Array().Add(true).Add(JsonValue.Null).Add(2.5))
                .Set("empty", JsonValue.Object())
                .Set("none", JsonValue.Array());
            Assert.That(v.ToJson(), Is.EqualTo("{\"z\":1,\"a\":\"two\",\"list\":[true,null,2.5],\"empty\":{},\"none\":[]}"));

            v["z"] = 5;                       // replace keeps the position
            v["new"] = false;
            Assert.That(v.Remove("a"), Is.True);
            Assert.That(v.Remove("a"), Is.False);
            Assert.That(v.ToJson(), Is.EqualTo("{\"z\":5,\"list\":[true,null,2.5],\"empty\":{},\"none\":[],\"new\":false}"));
            Assert.That(v["new"].AsBool(true), Is.False);
            v["list"][0] = "swapped";
            Assert.That(v["list"][0].AsString(), Is.EqualTo("swapped"));
        }

        [Test]
        public void Indented_output_parses_back_to_the_same_document()
        {
            JsonValue v = AtlasJson.Parse("{\"a\":[1,2,{\"b\":null}],\"c\":{\"d\":\"e\"},\"f\":[]}");
            string pretty = v.ToJson(true);
            Assert.That(pretty, Is.EqualTo("{\n  \"a\": [\n    1,\n    2,\n    {\n      \"b\": null\n    }\n  ],\n  \"c\": {\n    \"d\": \"e\"\n  },\n  \"f\": []\n}"));
            Assert.That(AtlasJson.Parse(pretty).DeepEquals(v));
            Assert.That(AtlasJson.Parse(pretty).ToJson(), Is.EqualTo(v.ToJson()));
        }

        [Test]
        public void Doubles_round_trip_exactly()
        {
            double[] values =
            {
                0.0, -0.0, 1.0, -1.0, 0.1, 1.0 / 3.0, Math.PI, Math.E, 1e-320, double.Epsilon, double.MaxValue,
                double.MinValue, 1e21, 123456789.123456789, 4.35, 0.30000000000000004, 2.2250738585072014E-308,
                9007199254740993.0, -1.7976931348623157E+308, 5e-324, 1.2345678901234567e-7
            };
            foreach (double d in values)
            {
                string json = JsonValue.Array().Add(d).ToJson();
                double back = AtlasJson.Parse(json)[0].AsDouble();
                Assert.That(BitConverter.DoubleToInt64Bits(back), Is.EqualTo(BitConverter.DoubleToInt64Bits(d)), json);
            }

            var random = new Random(12345);
            var bytes = new byte[8];
            for (int i = 0; i < 2000; i++)
            {
                random.NextBytes(bytes);
                double d = BitConverter.ToDouble(bytes, 0);
                if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                string json = JsonValue.From(d).ToJson();
                double back = AtlasJson.Parse(json).AsDouble();
                Assert.That(BitConverter.DoubleToInt64Bits(back), Is.EqualTo(BitConverter.DoubleToInt64Bits(d)), json);
            }
        }

        [Test]
        public void Floats_are_written_short_and_read_back_exactly()
        {
            Assert.That(JsonValue.From(0.1f).ToJson(), Is.EqualTo("0.1"));
            Assert.That(JsonValue.From(2.5f).ToJson(), Is.EqualTo("2.5"));
            Assert.That(JsonValue.From(3f).ToJson(), Is.EqualTo("3.0"));
            var random = new Random(7);
            var bytes = new byte[4];
            for (int i = 0; i < 2000; i++)
            {
                random.NextBytes(bytes);
                float f = BitConverter.ToSingle(bytes, 0);
                if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                float back = AtlasJson.Parse(JsonValue.From(f).ToJson()).AsFloat();
                Assert.That(BitConverter.SingleToInt32Bits(back), Is.EqualTo(BitConverter.SingleToInt32Bits(f)));
            }
        }

        [Test]
        public void Integers_round_trip_exactly_up_to_int64()
        {
            long[] values = { 0, 1, -1, int.MaxValue, int.MinValue, 9007199254740993L, long.MaxValue, long.MinValue };
            foreach (long n in values)
            {
                string json = JsonValue.From(n).ToJson();
                Assert.That(json, Is.EqualTo(n.ToString(CultureInfo.InvariantCulture)));
                JsonValue back = AtlasJson.Parse(json);
                Assert.That(back.IsInteger);
                Assert.That(back.AsLong(), Is.EqualTo(n));
            }
            // Beyond Int64 the number is still read, as a double.
            Assert.That(AtlasJson.Parse("18446744073709551616").AsDouble(), Is.EqualTo(18446744073709551616.0));
            Assert.That(AtlasJson.Parse("18446744073709551616").AsLong(), Is.EqualTo(long.MaxValue));
            Assert.That(AtlasJson.Parse("2.9").AsInt(), Is.EqualTo(2));
            Assert.That(AtlasJson.Parse("-2.9").AsInt(), Is.EqualTo(-2));
            Assert.That(AtlasJson.Parse("5000000000").AsInt(), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void A_whole_double_stays_a_double()
        {
            Assert.That(JsonValue.From(3.0).ToJson(), Is.EqualTo("3.0"));
            Assert.That(AtlasJson.Parse("3.0").IsInteger, Is.False);
            Assert.That(JsonValue.From(1e21).ToJson(), Is.EqualTo("1E+21"));
            Assert.That(AtlasJson.Parse("1E+21").AsDouble(), Is.EqualTo(1e21));
        }

        [Test]
        public void Caller_formatted_numbers_are_written_verbatim()
        {
            JsonValue v = JsonValue.Object()
                .Set("w", JsonValue.Number(4.0, "F4"))
                .Set("h", JsonValue.Number(2.44999, "0.###"))
                .Set("raw", JsonValue.Number("1.50"));
            Assert.That(v.ToJson(), Is.EqualTo("{\"w\":4.0000,\"h\":2.45,\"raw\":1.50}"));
            Assert.That(v["w"].AsDouble(), Is.EqualTo(4.0));
            Assert.That(v["h"].AsFloat(), Is.EqualTo(2.45f));
            Assert.That(AtlasJson.Parse(v.ToJson())["raw"].AsDouble(), Is.EqualTo(1.5));
            Assert.That(v["w"].DeepEquals(JsonValue.From(4.0)));
            Assert.That(() => JsonValue.Number("1,5"), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonValue.Number("NaN"), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonValue.Number(" 1"), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonValue.Number("\"1\""), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonValue.Number(double.NaN, "F2"), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void NaN_and_infinity_are_written_as_null()
        {
            Assert.That(JsonValue.Array().Add(double.NaN).Add(double.PositiveInfinity).Add(float.NegativeInfinity).ToJson(),
                Is.EqualTo("[null,null,null]"));
        }

        [Test]
        public void Strings_escape_and_unescape()
        {
            string nasty = "quote\" backslash\\ slash/ \b\f\n\r\t nul\u0000 unit\u001f del\u007f line\u2028para\u2029 caf\u00e9 \u65e5\u672c \ud83d\ude00";
            string json = JsonValue.From(nasty).ToJson();
            Assert.That(json, Is.EqualTo(
                "\"quote\\\" backslash\\\\ slash/ \\b\\f\\n\\r\\t nul\\u0000 unit\\u001f del\u007f line\\u2028para\\u2029 caf\u00e9 \u65e5\u672c \ud83d\ude00\""));
            Assert.That(json.IndexOf('\n'), Is.LessThan(0));
            Assert.That(AtlasJson.Parse(json).AsString(), Is.EqualTo(nasty));
            Assert.That(AtlasJson.Quote("a\"b"), Is.EqualTo("\"a\\\"b\""));
        }

        [Test]
        public void Unicode_escapes_are_decoded()
        {
            Assert.That(AtlasJson.Parse("\"\\u0041\\u00e9\\u65E5\\ud83d\\ude00\\/\"").AsString(), Is.EqualTo("A\u00e9\u65e5\ud83d\ude00/"));
        }

        [Test]
        public void Utf8_round_trip_with_and_without_bom()
        {
            JsonValue v = JsonValue.Object().Set("name", "caf\u00e9 \u65e5\u672c \ud83d\ude00").Set("n", 1);
            byte[] bytes = AtlasJson.ToUtf8(v);
            Assert.That(bytes[0], Is.EqualTo((byte)'{'));
            Assert.That(AtlasJson.ParseUtf8(bytes).DeepEquals(v));

            var withBom = new byte[bytes.Length + 3];
            withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
            Buffer.BlockCopy(bytes, 0, withBom, 3, bytes.Length);
            Assert.That(AtlasJson.ParseUtf8(withBom).DeepEquals(v));

            JsonValue part;
            string error;
            Assert.That(AtlasJson.TryParseUtf8(withBom, 3, bytes.Length, false, out part, out error), Is.True);
            Assert.That(AtlasJson.TryParseUtf8(withBom, 3, bytes.Length + 1, false, out part, out error), Is.False);
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(v.ToJson()));
        }

        [Test]
        public void Malformed_input_is_rejected_with_a_position()
        {
            string[] bad =
            {
                "", "   ", "{", "}", "[1,]", "{\"a\":}", "{\"a\" 1}", "{a:1}", "[1 2]", "01", "1.", ".5", "-", "+1", "1e",
                "tru", "nul", "\"open", "\"bad\\x\"", "\"\\u12G4\"", "\"\\u12\"", "\"line\nbreak\"", "[1] 2", "{\"a\":1,}",
                "'single'", "NaN", "{\"a\":1}}"
            };
            foreach (string text in bad)
            {
                JsonValue v;
                string error;
                Assert.That(AtlasJson.TryParse(text, out v, out error), Is.False, text);
                Assert.That(v.IsNull, text);
                Assert.That(error, Is.Not.Null.And.Not.Empty, text);
                Assert.That(AtlasJson.TryParse(text, out v), Is.False, text);
            }
            Assert.That(() => AtlasJson.Parse("[1,"), Throws.TypeOf<FormatException>());
            JsonValue n;
            Assert.That(AtlasJson.TryParse(null, out n), Is.False);
        }

        [Test]
        public void Trailing_text_is_ignored_only_when_asked()
        {
            JsonValue v;
            string error;
            Assert.That(AtlasJson.TryParse("{\"a\":1} trailing \u0001\u0002", true, out v, out error), Is.True);
            Assert.That(v["a"].AsInt(), Is.EqualTo(1));
            Assert.That(AtlasJson.TryParse("{\"a\":1} trailing", false, out v, out error), Is.False);
        }

        [Test]
        public void Deep_nesting_is_refused_not_fatal()
        {
            string ok = new string('[', AtlasJson.MaxDepth) + new string(']', AtlasJson.MaxDepth);
            JsonValue v;
            Assert.That(AtlasJson.TryParse(ok, out v), Is.True);
            string tooDeep = new string('[', 5000) + new string(']', 5000);
            Assert.That(AtlasJson.TryParse(tooDeep, out v), Is.False);
        }

        [Test]
        public void Duplicate_keys_keep_the_last_value()
        {
            JsonValue v = AtlasJson.Parse("{\"a\":1,\"b\":2,\"a\":3}");
            Assert.That(v.Count, Is.EqualTo(2));
            Assert.That(v["a"].AsInt(), Is.EqualTo(3));
        }

        [Test]
        public void Null_is_shared_and_cannot_be_mutated()
        {
            Assert.That(() => JsonValue.Null.Set("a", 1), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => JsonValue.Null.Add(1), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => JsonValue.Object().Add(1), Throws.TypeOf<InvalidOperationException>());
            Assert.That(JsonValue.From((string)null).IsNull);
            Assert.That(JsonValue.Object().Set("k", null).ToJson(), Is.EqualTo("{\"k\":null}"));
        }

        [Test]
        public void Clone_is_deep_and_DeepEquals_ignores_member_order()
        {
            JsonValue a = AtlasJson.Parse("{\"x\":[1,{\"y\":2}],\"z\":\"s\"}");
            JsonValue b = a.Clone();
            Assert.That(a.DeepEquals(b));
            b["x"][1]["y"] = 3;
            Assert.That(a["x"][1]["y"].AsInt(), Is.EqualTo(2));
            Assert.That(a.DeepEquals(b), Is.False);
            Assert.That(AtlasJson.Parse("{\"a\":1,\"b\":2}").DeepEquals(AtlasJson.Parse("{\"b\":2,\"a\":1}")));
            Assert.That(AtlasJson.Parse("[1,2]").DeepEquals(AtlasJson.Parse("[2,1]")), Is.False);
        }

        [Test]
        public void Numbers_ignore_the_thread_culture()
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try
            {
                CultureInfo comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                comma.NumberFormat.NumberDecimalSeparator = ",";
                comma.NumberFormat.NegativeSign = "~";
                Thread.CurrentThread.CurrentCulture = comma;
                Assert.That(JsonValue.Array().Add(-1.5).Add(0.25f).Add(-7).ToJson(), Is.EqualTo("[-1.5,0.25,-7]"));
                Assert.That(AtlasJson.Parse("[-1.5]")[0].AsDouble(), Is.EqualTo(-1.5));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }
    }
}
