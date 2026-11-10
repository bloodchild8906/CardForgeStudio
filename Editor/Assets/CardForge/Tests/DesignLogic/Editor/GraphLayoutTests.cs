using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using CardForge.DesignLogic;

namespace CardForge.DesignLogic.Tests
{
    /// <summary>Regression tests for the layered graph layout.</summary>
    public sealed class GraphLayoutTests
    {
        private static KeyValuePair<string, string> Link(string from, string to) => new KeyValuePair<string, string>(from, to);

        private static GraphPlacement Find(IReadOnlyList<GraphPlacement> placements, string id) => placements.First(placement => placement.Id == id);

        [Test] public void LinearChainRunsLeftToRightOnOneRow()
        {
            var placements = GraphLayout.Arrange(new[] { "a", "b", "c" }, new[] { Link("a", "b"), Link("b", "c") });
            Assert.Less(Find(placements, "a").X, Find(placements, "b").X);
            Assert.Less(Find(placements, "b").X, Find(placements, "c").X);
            Assert.AreEqual(Find(placements, "a").Y, Find(placements, "c").Y, 0.001);
        }

        [Test] public void BranchesShareAColumnAndDifferInHeight()
        {
            var placements = GraphLayout.Arrange(new[] { "a", "b", "c" }, new[] { Link("a", "b"), Link("a", "c") });
            Assert.AreEqual(Find(placements, "b").X, Find(placements, "c").X, 0.001);
            Assert.AreNotEqual(Find(placements, "b").Y, Find(placements, "c").Y);
        }

        [Test] public void CyclesTerminateAndKeepForwardOrder()
        {
            var placements = GraphLayout.Arrange(new[] { "a", "b", "c" }, new[] { Link("a", "b"), Link("b", "c"), Link("c", "a") });
            Assert.AreEqual(3, placements.Count);
            Assert.Less(Find(placements, "a").X, Find(placements, "b").X);
            Assert.Less(Find(placements, "b").X, Find(placements, "c").X);
        }

        [Test] public void LongChainsWrapIntoRows()
        {
            var ids = Enumerable.Range(0, 7).Select(index => "n" + index).ToArray();
            var links = Enumerable.Range(1, 6).Select(index => Link(ids[index - 1], ids[index])).ToArray();
            var placements = GraphLayout.Arrange(ids, links, new GraphLayoutSettings { MaximumColumns = 3 });
            Assert.AreEqual(Find(placements, "n0").X, Find(placements, "n3").X, 0.001);
            Assert.Greater(Find(placements, "n3").Y, Find(placements, "n0").Y);
            Assert.Greater(Find(placements, "n2").X, Find(placements, "n1").X);
        }

        [Test] public void NodesNeverOverlap()
        {
            var ids = Enumerable.Range(0, 14).Select(index => "n" + index).ToArray();
            var links = new[] { Link("n0", "n1"), Link("n0", "n2"), Link("n0", "n3"), Link("n1", "n4"), Link("n2", "n4"),
                Link("n3", "n5"), Link("n4", "n6"), Link("n5", "n6"), Link("n6", "n7"), Link("n7", "n8"), Link("n8", "n0") };
            var settings = new GraphLayoutSettings();
            var placements = GraphLayout.Arrange(ids, links, settings);
            for (int first = 0; first < placements.Count; first++)
            {
                for (int second = first + 1; second < placements.Count; second++)
                {
                    bool apartHorizontally = System.Math.Abs(placements[first].X - placements[second].X) >= settings.NodeWidth;
                    bool apartVertically = System.Math.Abs(placements[first].Y - placements[second].Y) >= settings.NodeHeight;
                    Assert.IsTrue(apartHorizontally || apartVertically, placements[first].Id + " overlaps " + placements[second].Id);
                }
            }
        }

        [Test] public void UnknownSelfAndDuplicateLinksAreIgnored()
        {
            var placements = GraphLayout.Arrange(new[] { "a", "b" },
                new[] { Link("a", "a"), Link("a", "ghost"), Link("ghost", "b"), Link("a", "b"), Link("a", "b"), Link(null, "b") });
            Assert.AreEqual(2, placements.Count);
            Assert.Less(Find(placements, "a").X, Find(placements, "b").X);
        }

        [Test] public void EmptyGraphHasNoPlacements()
        {
            Assert.AreEqual(0, GraphLayout.Arrange(new string[0], new KeyValuePair<string, string>[0]).Count);
        }

        [Test] public void ResultKeepsInputOrder()
        {
            var placements = GraphLayout.Arrange(new[] { "z", "y", "x" }, new[] { Link("x", "z") });
            CollectionAssert.AreEqual(new[] { "z", "y", "x" }, placements.Select(placement => placement.Id).ToArray());
        }
    }
}
