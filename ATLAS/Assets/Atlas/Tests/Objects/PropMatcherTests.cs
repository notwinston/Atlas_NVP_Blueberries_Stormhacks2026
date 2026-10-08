using System;
using System.Collections.Generic;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Objects.Tests.ObjectsTestData;

namespace Atlas.Objects.Tests
{
    /// <summary>
    /// PropMatcher (docs/plans/standalone-phone/INDEX.md section 6.4, "Matcher rule"): candidates of the
    /// category, quarter turns by facing rule, Score = F + 0.5 A - tags, ties within 0.02 by smaller turn then
    /// ordinal id. Checked against every matcher, fitScore and rotate vector of reference.json, which
    /// tools/props/reference.py computed with the Forge's profile.py.
    /// </summary>
    public class PropMatcherTests
    {
        // ------------------------------------------------------------------ constants

        [Test]
        public void Constants_AreThePlansAndTheReferences()
        {
            JsonValue c = Reference["constants"];
            Assert.That(PropMatcher.AspectWeight, Is.EqualTo(0.5f));
            Assert.That(PropMatcher.TagBonus, Is.EqualTo(0.03f));
            Assert.That(PropMatcher.MaxTagBonus, Is.EqualTo(0.06f));
            Assert.That(PropMatcher.Tie, Is.EqualTo(0.02f));
            Assert.That(PropMatcher.PoorFit, Is.EqualTo(0.20f));
            Assert.That(PropMatcher.MinCoverage, Is.EqualTo(0.30f));
            Assert.That(PropMatcher.AspectWeight, Is.EqualTo(c.GetFloat("aspectWeight")));
            Assert.That(PropMatcher.TagBonus, Is.EqualTo(c.GetFloat("tagBonus")));
            Assert.That(PropMatcher.MaxTagBonus, Is.EqualTo(c.GetFloat("maxTagBonus")));
            Assert.That(PropMatcher.Tie, Is.EqualTo(c.GetFloat("tie")));
            Assert.That(PropMatcher.MinCoverage, Is.EqualTo(c.GetFloat("minCoverage")));
        }

        // ------------------------------------------------------------------ reference.json: matcher

        static PropEntry[] ReferenceLibrary()
        {
            JsonValue lib = Reference["matcher"]["library"];
            var props = new PropEntry[lib.Count];
            for (int k = 0; k < props.Length; k++)
            {
                JsonValue p = lib[k];
                props[k] = new PropEntry
                {
                    Id = p.GetString("id"),
                    Category = Category(p.GetString("category")),
                    Tags = Strings(p["tags"]),
                    Size = Float3(p["size"]),
                    Profile = Profile(p["profile"]),
                    SlotCells = new byte[] { 64 },
                    Mesh = new MeshData(),
                };
            }
            return props;
        }

        static ObjectQuery Query(JsonValue q)
        {
            return new ObjectQuery
            {
                Category = Category(q.GetString("category")),
                Tags = Strings(q["tags"]),
                Size = Float3(q["size"]),
                Profile = Profile(q["profile"]),
                Facing = Facing(q.GetString("facing")),
            };
        }

        static IEnumerable<string> QueryNames()
        {
            JsonValue queries = Reference["matcher"]["queries"];
            for (int k = 0; k < queries.Count; k++) yield return queries[k].GetString("name");
        }

        static JsonValue QueryByName(string name)
        {
            JsonValue queries = Reference["matcher"]["queries"];
            for (int k = 0; k < queries.Count; k++)
                if (queries[k].GetString("name") == name) return queries[k];
            throw new ArgumentException(name);
        }

        [TestCaseSource(nameof(QueryNames))]
        public void Reference_MatcherQuery(string name)
        {
            JsonValue q = QueryByName(name);
            JsonValue expected = q["expected"];
            PropEntry[] library = ReferenceLibrary();
            ObjectQuery query = Query(q);

            bool found = PropMatcher.Choose(in query, library, out PropMatch match);

            Assert.That(found, Is.EqualTo(expected.GetBool("found")), name + ": found");
            if (!found) return;
            Assert.That(match.Index, Is.EqualTo(expected.GetInt("index")), name + ": index");
            Assert.That(library[match.Index].Id, Is.EqualTo(expected.GetString("id")), name + ": id");
            Assert.That(match.QuarterTurns, Is.EqualTo(expected.GetInt("quarterTurns")), name + ": quarter turns");
            Assert.That(match.Fit, Is.EqualTo(expected.GetFloat("fit")).Within(Tolerance), name + ": fit");
            Assert.That(match.Score, Is.EqualTo(expected.GetFloat("score")).Within(Tolerance), name + ": score");
        }

        static IEnumerable<TestCaseData> AllCandidates()
        {
            JsonValue queries = Reference["matcher"]["queries"];
            for (int k = 0; k < queries.Count; k++)
            {
                JsonValue candidates = queries[k]["candidates"];
                for (int c = 0; c < candidates.Count; c++)
                    yield return new TestCaseData(queries[k].GetString("name"), c)
                        .SetName("Reference_Candidate_" + queries[k].GetString("name") + "_" + candidates[c].GetString("id")
                                 + "_turn" + candidates[c].GetInt("quarterTurns"));
            }
        }

        /// <summary>Each candidate of the reference on its own (a library of that prop only, and the Profile rule
        /// so turn 0 is the only turn): its fit and score, so aspect and tag terms are checked one by one, not
        /// only through the winner. A candidate at turn t is checked through a copy of its prop already turned t
        /// times (profile rotate(profile, t), size with w and d swapped for odd t, same tags): by the matcher rule
        /// that copy at turn 0 has exactly the turn-t candidate's e, F and tags.</summary>
        [TestCaseSource(nameof(AllCandidates))]
        public void Reference_CandidateScore(string name, int candidateIndex)
        {
            JsonValue q = QueryByName(name);
            JsonValue cand = q["candidates"][candidateIndex];
            PropEntry prop = ReferenceLibrary()[cand.GetInt("index")];
            int turns = cand.GetInt("quarterTurns");
            if (turns != 0)
            {
                prop.Profile = prop.Profile == null ? null : Rotate(prop.Profile, turns);
                if ((turns & 1) == 1) prop.Size = prop.Size.zyx;
            }
            ObjectQuery query = Query(q);
            query.Facing = ObjectFacingRule.Profile;

            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out PropMatch match), Is.True);
            Assert.That(match.Index, Is.EqualTo(0));
            Assert.That(match.QuarterTurns, Is.EqualTo(0));
            Assert.That(match.Fit, Is.EqualTo(cand.GetFloat("fit")).Within(Tolerance), "fit");
            Assert.That(match.Score, Is.EqualTo(cand.GetFloat("score")).Within(Tolerance), "score");
        }

        // ------------------------------------------------------------------ reference.json: fit_score and rotate

        static IEnumerable<TestCaseData> FitScoreVectors()
        {
            JsonValue v = Reference["fitScore"];
            for (int k = 0; k < v.Count; k++)
                yield return new TestCaseData(k).SetName("Reference_FitScore_" + v[k].GetString("label").Replace(' ', '_'));
        }

        /// <summary>fit_score(scan, generated) through the matcher: one prop whose profile is "generated", the same
        /// size as the query (A = 0), no tags, turn 0 only. Then Fit = fit_score when the scan's coverage is at
        /// least 0.30, else 0, and Score = Fit.</summary>
        [TestCaseSource(nameof(FitScoreVectors))]
        public void Reference_FitScore(int k)
        {
            JsonValue v = Reference["fitScore"][k];
            byte[] scan = Profile(v["scan"]);
            var size = new float3(0.8f, 0.9f, 0.7f);
            PropEntry prop = SyntheticProp("generated", ObjectCategory.Chair, size, new byte[] { 64 }, new MeshData(),
                                           Profile(v["generated"]));
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Chair, Tags = Array.Empty<string>(), Size = size, Profile = scan,
                Facing = ObjectFacingRule.Profile,
            };
            float expected = Coverage(scan) >= 0.30f ? v.GetFloat("fit") : 0f;

            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out PropMatch match), Is.True);
            Assert.That(match.Fit, Is.EqualTo(expected).Within(Tolerance), "fit");
            Assert.That(match.Score, Is.EqualTo(expected).Within(Tolerance), "score");
        }

        static IEnumerable<int> RotateTurns() { return new[] { 0, 1, 2, 3 }; }

        /// <summary>
        /// A scan whose profile is rotate(source, k), with the source's size turned k times, against the source
        /// prop under the Box rule: the matcher picks turn k with F = 0 (its rotate is reference.py's).
        /// </summary>
        [TestCaseSource(nameof(RotateTurns))]
        public void Reference_Rotate_MatcherPicksThatTurn(int turns)
        {
            JsonValue rotate = Reference["rotate"];
            byte[] rotated = null;
            for (int k = 0; k < rotate.Count; k++)
                if (rotate[k].GetInt("turns") == turns) rotated = Profile(rotate[k]["profile"]);
            Assert.That(rotated, Is.Not.Null);
            PropEntry source = FixtureProp(Reference.GetString("rotateSource"));
            float3 size = (turns & 1) == 1 ? source.Size.zyx : source.Size;

            var query = new ObjectQuery
            {
                Category = source.Category, Tags = Array.Empty<string>(), Size = size, Profile = rotated,
                Facing = ObjectFacingRule.Box,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { source }, out PropMatch match), Is.True);
            Assert.That(match.QuarterTurns, Is.EqualTo(turns));
            Assert.That(match.Fit, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(match.Score, Is.EqualTo(0f).Within(Tolerance));
        }

        static float Coverage(byte[] profile)
        {
            if (profile == null || profile.Length != 256) return 0f;
            int seen = 0;
            foreach (byte b in profile) if (b != 0) seen++;
            return seen / 256f;
        }

        // ------------------------------------------------------------------ quarter turns by facing rule

        static IEnumerable<TestCaseData> FacingRules()
        {
            yield return new TestCaseData(ObjectFacingRule.Profile, 0);
            yield return new TestCaseData(ObjectFacingRule.Clearance, 0);
            yield return new TestCaseData(ObjectFacingRule.Box, 2);
            yield return new TestCaseData(ObjectFacingRule.Table, 2);
        }

        /// <summary>The scan is the couch turned half way: Box and Table may turn the prop (and pick 2); Profile
        /// and Clearance keep turn 0, whatever the fit.</summary>
        [TestCaseSource(nameof(FacingRules))]
        public void QuarterTurns_FollowTheFacingRule(ObjectFacingRule facing, int expectedTurns)
        {
            PropEntry couch = FixtureProp("couch_box3_min");
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Couch, Tags = Array.Empty<string>(), Size = couch.Size,
                Profile = Rotate(couch.Profile, 2), Facing = facing,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { couch }, out PropMatch match), Is.True);
            Assert.That(match.QuarterTurns, Is.EqualTo(expectedTurns));
            if (expectedTurns == 0) Assert.That(match.Fit, Is.GreaterThan(0.1f), "turn 0 keeps its poor fit");
            else Assert.That(match.Fit, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(ObjectFacingRule.Profile)]
        [TestCase(ObjectFacingRule.Clearance)]
        public void ProfileAndClearance_NeverTurn_EvenWhenTurningFitsBetter(ObjectFacingRule facing)
        {
            PropEntry[] library = ReferenceLibrary();
            for (int turns = 1; turns < 4; turns++)
            {
                foreach (PropEntry p in library)
                {
                    var query = new ObjectQuery
                    {
                        Category = p.Category, Tags = Array.Empty<string>(),
                        Size = (turns & 1) == 1 ? p.Size.zyx : p.Size,
                        Profile = Rotate(p.Profile, turns), Facing = facing,
                    };
                    Assert.That(PropMatcher.Choose(in query, library, out PropMatch match), Is.True);
                    Assert.That(match.QuarterTurns, Is.EqualTo(0), p.Id + " turned " + turns);
                }
            }
        }

        // Reference rotation (reference.json conventions.rotate): new cell (i, j) takes old cell
        // (j, 15 - i) for k = 1, (15 - i, 15 - j) for k = 2, (15 - j, i) for k = 3.
        static byte[] Rotate(byte[] p, int k)
        {
            var r = new byte[256];
            for (int j = 0; j < 16; j++)
                for (int i = 0; i < 16; i++)
                {
                    int oi = i, oj = j;
                    if (k == 1) { oi = j; oj = 15 - i; }
                    else if (k == 2) { oi = 15 - i; oj = 15 - j; }
                    else if (k == 3) { oi = 15 - j; oj = i; }
                    r[j * 16 + i] = p[oj * 16 + oi];
                }
            return r;
        }

        // ------------------------------------------------------------------ categories and empty input

        [Test]
        public void NoPropOfTheCategory_ReturnsFalse()
        {
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Toilet, Tags = Array.Empty<string>(), Size = new float3(0.4f, 0.8f, 0.7f),
                Facing = ObjectFacingRule.Box,
            };
            Assert.That(PropMatcher.Choose(in query, ReferenceLibrary(), out _), Is.False);
        }

        [Test]
        public void EmptyLibrary_ReturnsFalse()
        {
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Chair, Tags = Array.Empty<string>(), Size = new float3(0.5f, 0.9f, 0.5f),
                Facing = ObjectFacingRule.Box,
            };
            bool found = true;
            Assert.DoesNotThrow(() => found = PropMatcher.Choose(in query, Array.Empty<PropEntry>(), out _));
            Assert.That(found, Is.False);
        }

        [Test]
        public void OnlyTheQuerysCategoryIsACandidate()
        {
            // A table query with a chair's exact size: a chair would fit it better, but only tables are candidates.
            PropEntry[] library = ReferenceLibrary();
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Table, Tags = Array.Empty<string>(), Size = new float3(0.46f, 0.9f, 0.5f),
                Facing = ObjectFacingRule.Profile,
            };
            Assert.That(PropMatcher.Choose(in query, library, out PropMatch match), Is.True);
            Assert.That(library[match.Index].Category, Is.EqualTo(ObjectCategory.Table));
        }

        [Test]
        public void NullTagsAndNullProfile_NoException_FitIsZero()
        {
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Couch, Tags = null, Size = new float3(1.8f, 0.8f, 0.85f), Profile = null,
                Facing = ObjectFacingRule.Box,
            };
            bool found = false;
            PropMatch match = default;
            Assert.DoesNotThrow(() => found = PropMatcher.Choose(in query, ReferenceLibrary(), out match));
            Assert.That(found, Is.True);
            Assert.That(match.Fit, Is.EqualTo(0f));
        }

        [Test]
        public void PropWithoutTagsOrProfile_NoException()
        {
            PropEntry bare = SyntheticProp("bare", ObjectCategory.Bed, new float3(1.6f, 0.5f, 2.0f), new byte[] { 64 },
                                           new MeshData(), profile: null, tags: Array.Empty<string>());
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Bed, Tags = new[] { "king" }, Size = new float3(1.6f, 0.5f, 2.0f),
                Profile = FlatProfile(200), Facing = ObjectFacingRule.Box,
            };
            bool found = false;
            PropMatch match = default;
            Assert.DoesNotThrow(() => found = PropMatcher.Choose(in query, new[] { bare }, out match));
            Assert.That(found, Is.True);
            Assert.That(match.Index, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ coverage threshold and tags

        [TestCase(76, false)]  // 76 / 256 = 0.297 < 0.30: F = 0
        [TestCase(77, true)]   // 77 / 256 = 0.301: F counts
        public void Fit_CountsOnlyFromThirtyPercentCoverage(int seenCells, bool counts)
        {
            var scan = new byte[256];
            for (int k = 0; k < seenCells; k++) scan[k] = 255;
            var size = new float3(1f, 1f, 1f);
            PropEntry prop = SyntheticProp("p", ObjectCategory.Storage, size, new byte[] { 64 }, new MeshData(), FlatProfile(1));
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Storage, Tags = Array.Empty<string>(), Size = size, Profile = scan,
                Facing = ObjectFacingRule.Profile,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out PropMatch match), Is.True);
            float expected = counts ? 254f / 255f : 0f;
            Assert.That(match.Fit, Is.EqualTo(expected).Within(Tolerance));
        }

        static float TagScore(string[] queryTags, string[] propTags)
        {
            var size = new float3(0.5f, 0.9f, 0.5f);
            PropEntry prop = SyntheticProp("p", ObjectCategory.Chair, size, new byte[] { 64 }, new MeshData(), null, propTags);
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Chair, Tags = queryTags, Size = size, Profile = null,
                Facing = ObjectFacingRule.Profile,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out PropMatch match), Is.True);
            return match.Score;
        }

        [Test]
        public void TagBonus_IsThreeHundredthsPerSharedTag_CappedAtSixHundredths()
        {
            string[] prop = { "a", "b", "c" };
            Assert.That(TagScore(new string[0], prop), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(TagScore(new[] { "a" }, prop), Is.EqualTo(-0.03f).Within(Tolerance));
            Assert.That(TagScore(new[] { "a", "b" }, prop), Is.EqualTo(-0.06f).Within(Tolerance));
            Assert.That(TagScore(new[] { "a", "b", "c" }, prop), Is.EqualTo(-0.06f).Within(Tolerance));
        }

        [Test]
        public void TagBonus_CountsDistinctStrings_Ordinal()
        {
            string[] prop = { "dining" };
            Assert.That(TagScore(new[] { "dining", "dining" }, prop), Is.EqualTo(-0.03f).Within(Tolerance), "a repeat counts once");
            Assert.That(TagScore(new[] { "Dining" }, prop), Is.EqualTo(0f).Within(Tolerance), "case matters");
            Assert.That(TagScore(new[] { "notATag" }, prop), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Aspect_IsMeanAbsoluteDeviationOfLogRatios()
        {
            // prop 1 x 1 x 1, query 2 x 1 x 0.5: lambda = (ln 2, 0, -ln 2), mean 0, A = 2 ln 2 / 3.
            PropEntry prop = SyntheticProp("p", ObjectCategory.Storage, new float3(1f), new byte[] { 64 }, new MeshData());
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Storage, Tags = Array.Empty<string>(), Size = new float3(2f, 1f, 0.5f),
                Facing = ObjectFacingRule.Profile,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out PropMatch match), Is.True);
            float a = 2f * math.log(2f) / 3f;
            Assert.That(match.Score, Is.EqualTo(0.5f * a).Within(Tolerance));
            Assert.That(match.Fit, Is.EqualTo(0f));

            // A uniform scale is no aspect cost.
            query.Size = new float3(3f, 3f, 3f);
            Assert.That(PropMatcher.Choose(in query, new[] { prop }, out match), Is.True);
            Assert.That(match.Score, Is.EqualTo(0f).Within(Tolerance));
        }

        // ------------------------------------------------------------------ ties and determinism

        [Test]
        public void Tie_WithinTwoHundredths_GoesToTheOrdinalSmallerId()
        {
            var size = new float3(1f);
            // Scores 0.010 and 0.000: tied (difference < 0.02), so "Alpha" (ordinal before "beta") wins over the lower score.
            PropEntry beta = SyntheticProp("beta", ObjectCategory.Storage, size, new byte[] { 64 }, new MeshData(), FlatProfile(255));
            PropEntry alpha = SyntheticProp("Alpha", ObjectCategory.Storage, size, new byte[] { 64 }, new MeshData(), FlatProfile(255));
            byte[] scan = FlatProfile(255);
            // Make alpha's fit 0.0098 (2.5 / 255): every cell 2 or 3 lower.
            byte[] alphaProfile = FlatProfile(253);
            for (int k = 0; k < 128; k++) alphaProfile[k] = 252;
            alpha.Profile = alphaProfile;
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Storage, Tags = Array.Empty<string>(), Size = size, Profile = scan,
                Facing = ObjectFacingRule.Profile,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { beta, alpha }, out PropMatch match), Is.True);
            Assert.That(match.Index, Is.EqualTo(1), "Alpha < beta in ordinal order");
            Assert.That(match.Fit, Is.EqualTo(2.5f / 255f).Within(Tolerance));

            // Out of the tie (alpha 0.03 worse): the lower score wins.
            alpha.Profile = FlatProfile(247);
            Assert.That(PropMatcher.Choose(in query, new[] { beta, alpha }, out match), Is.True);
            Assert.That(match.Index, Is.EqualTo(0));
        }

        [Test]
        public void Tie_GoesToTheSmallerTurnBeforeTheId()
        {
            // A square table with a flat profile: every turn scores 0 for both props. "a_table" turn 0 wins.
            var size = new float3(0.9f, 0.75f, 0.9f);
            PropEntry b = SyntheticProp("b_table", ObjectCategory.Table, size, new byte[] { 64 }, new MeshData(), FlatProfile(255));
            PropEntry a = SyntheticProp("a_table", ObjectCategory.Table, size, new byte[] { 64 }, new MeshData(), FlatProfile(255));
            var query = new ObjectQuery
            {
                Category = ObjectCategory.Table, Tags = Array.Empty<string>(), Size = size, Profile = FlatProfile(255),
                Facing = ObjectFacingRule.Table,
            };
            Assert.That(PropMatcher.Choose(in query, new[] { b, a }, out PropMatch match), Is.True);
            Assert.That(match.Index, Is.EqualTo(1));
            Assert.That(match.QuarterTurns, Is.EqualTo(0));
        }

        [Test]
        public void Choice_DoesNotDependOnLibraryOrder_AndRepeats()
        {
            PropEntry[] library = ReferenceLibrary();
            var reversed = (PropEntry[])library.Clone();
            Array.Reverse(reversed);
            JsonValue queries = Reference["matcher"]["queries"];
            for (int k = 0; k < queries.Count; k++)
            {
                ObjectQuery q = Query(queries[k]);
                bool f1 = PropMatcher.Choose(in q, library, out PropMatch m1);
                bool f2 = PropMatcher.Choose(in q, library, out PropMatch m2);
                bool f3 = PropMatcher.Choose(in q, reversed, out PropMatch m3);
                string name = queries[k].GetString("name");
                Assert.That(f2, Is.EqualTo(f1), name);
                Assert.That(f3, Is.EqualTo(f1), name);
                if (!f1) continue;
                Assert.That(m2.Index, Is.EqualTo(m1.Index), name + ": repeat");
                Assert.That(m2.QuarterTurns, Is.EqualTo(m1.QuarterTurns), name + ": repeat");
                Assert.That(m2.Score, Is.EqualTo(m1.Score), name + ": repeat");
                Assert.That(reversed[m3.Index].Id, Is.EqualTo(library[m1.Index].Id), name + ": reversed library");
                Assert.That(m3.QuarterTurns, Is.EqualTo(m1.QuarterTurns), name + ": reversed library");
            }
        }
    }
}
