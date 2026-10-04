using System;
using NUnit.Framework;

namespace Atlas.Rendering.Tests.Look
{
    /// <summary>The renting rules of the material pool (lane A contract, section 11), without the engine.</summary>
    public class PoolLedgerTests
    {
        [Test]
        public void Rent_HandsOutEverySlotOnce()
        {
            var ledger = new PoolLedger(4);
            var seen = new bool[4];
            for (int i = 0; i < 4; i++)
            {
                int slot = ledger.Rent();
                Assert.That(seen[slot], Is.False);
                seen[slot] = true;
            }
            Assert.That(ledger.Rented, Is.EqualTo(4));
            Assert.That(ledger.Exhausted, Is.True);
            Assert.Throws<InvalidOperationException>(() => ledger.Rent());
        }

        [Test]
        public void Return_MakesTheSlotAvailableAgain()
        {
            var ledger = new PoolLedger(2);
            int a = ledger.Rent();
            ledger.Rent();
            ledger.Return(a);
            Assert.That(ledger.Rented, Is.EqualTo(1));
            Assert.That(ledger.IsRented(a), Is.False);
            Assert.That(ledger.Rent(), Is.EqualTo(a));
        }

        [Test]
        public void Return_TwiceOrForeign_Throws()
        {
            var ledger = new PoolLedger(2);
            int a = ledger.Rent();
            ledger.Return(a);
            Assert.Throws<ArgumentException>(() => ledger.Return(a));
            Assert.Throws<ArgumentException>(() => ledger.Return(1), "never rented");
            Assert.Throws<ArgumentException>(() => ledger.Return(7));
            Assert.Throws<ArgumentException>(() => ledger.Return(-1));
        }

        [Test]
        public void Grow_DoublesAndKeepsRentedSlots()
        {
            var ledger = new PoolLedger(2);
            int a = ledger.Rent(), b = ledger.Rent();
            Assert.That(ledger.Grow(), Is.EqualTo(2));
            Assert.That(ledger.Capacity, Is.EqualTo(4));
            Assert.That(ledger.Rented, Is.EqualTo(2));
            int c = ledger.Rent(), d = ledger.Rent();
            Assert.That(new[] { c, d }, Is.EquivalentTo(new[] { 2, 3 }));
            Assert.That(ledger.IsRented(a) && ledger.IsRented(b), Is.True);
        }

        [Test]
        public void RentAndReturn_AllocateNothingInSteadyState()
        {
            var ledger = new PoolLedger(256);
            for (int i = 0; i < 100; i++) ledger.Rent();
            ledger.Return(ledger.Rent());   // warm the code paths
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int round = 0; round < 1000; round++)
            {
                int a = ledger.Rent(), b = ledger.Rent();
                ledger.Return(a);
                ledger.Return(b);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        [Test]
        public void Capacity_MustBePositive()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PoolLedger(0));
        }
    }
}
