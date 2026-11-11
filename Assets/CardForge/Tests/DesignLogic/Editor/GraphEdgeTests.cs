using System.Collections.Generic;
using NUnit.Framework;
using CardForge.DesignLogic;

namespace CardForge.DesignLogic.Tests
{
    /// <summary>Regression tests for connection geometry and connection rules.</summary>
    public sealed class GraphEdgeTests
    {
        [Test] public void PointsOnTheCurveHaveNoDistance()
        {
            GraphEdgeGeometry.PointAt(100f, 50f, 400f, 250f, 0.5f, out float x, out float y);
            Assert.Less(GraphEdgeGeometry.DistanceToEdge(100f, 50f, 400f, 250f, x, y), 0.6f);
        }

        [Test] public void FarPointsAreFarFromTheCurve()
        {
            Assert.Greater(GraphEdgeGeometry.DistanceToEdge(100f, 50f, 400f, 250f, 250f, 900f), 400f);
        }

        [Test] public void CurveStartsAndEndsAtTheAnchors()
        {
            GraphEdgeGeometry.PointAt(10f, 20f, 300f, 90f, 0f, out float startX, out float startY);
            GraphEdgeGeometry.PointAt(10f, 20f, 300f, 90f, 1f, out float endX, out float endY);
            Assert.AreEqual(10f, startX, 0.001);
            Assert.AreEqual(20f, startY, 0.001);
            Assert.AreEqual(300f, endX, 0.001);
            Assert.AreEqual(90f, endY, 0.001);
        }

        [Test] public void SelfAndEmptyConnectionsAreRejected()
        {
            var none = new List<KeyValuePair<string, string>>();
            Assert.IsFalse(GraphConnectionRules.CanConnect("a", "a", none));
            Assert.IsFalse(GraphConnectionRules.CanConnect(null, "a", none));
            Assert.IsFalse(GraphConnectionRules.CanConnect("a", "", none));
        }

        [Test] public void DuplicateConnectionsAreRejectedButReverseIsAllowed()
        {
            var existing = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("a", "b") };
            Assert.IsFalse(GraphConnectionRules.CanConnect("a", "b", existing));
            Assert.IsTrue(GraphConnectionRules.CanConnect("b", "a", existing));
            Assert.IsTrue(GraphConnectionRules.CanConnect("a", "c", existing));
        }
    }
}
