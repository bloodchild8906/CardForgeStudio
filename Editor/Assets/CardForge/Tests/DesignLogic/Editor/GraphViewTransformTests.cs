using NUnit.Framework;
using CardForge.DesignLogic;

namespace CardForge.DesignLogic.Tests
{
    /// <summary>Regression tests for pan, zoom and fit maths.</summary>
    public sealed class GraphViewTransformTests
    {
        [Test] public void ZoomingKeepsThePointUnderTheCursorFixed()
        {
            var transform = new GraphViewTransform();
            transform.Set(1f, 120f, -40f);
            float worldX = transform.ToWorldX(300f);
            float worldY = transform.ToWorldY(200f);
            transform.ZoomAt(1.5f, 300f, 200f);
            Assert.AreEqual(300f, transform.ToScreenX(worldX), 0.001);
            Assert.AreEqual(200f, transform.ToScreenY(worldY), 0.001);
        }

        [Test] public void ZoomStaysWithinLimits()
        {
            var transform = new GraphViewTransform();
            transform.ZoomAt(100f, 0f, 0f);
            Assert.AreEqual(GraphViewTransform.MaximumZoom, transform.Zoom, 0.0001);
            transform.ZoomAt(0.0001f, 0f, 0f);
            Assert.AreEqual(GraphViewTransform.MinimumZoom, transform.Zoom, 0.0001);
        }

        [Test] public void FitCentresContentInsideThePaddedView()
        {
            var transform = new GraphViewTransform();
            transform.FitTo(0f, 0f, 1000f, 500f, 500f, 400f, 20f);
            Assert.AreEqual(0.46f, transform.Zoom, 0.0001);
            Assert.AreEqual(250f, transform.ToScreenX(500f), 0.01);
            Assert.AreEqual(200f, transform.ToScreenY(250f), 0.01);
        }

        [Test] public void FitNeverEnlargesSmallContent()
        {
            var transform = new GraphViewTransform();
            transform.FitTo(10f, 10f, 100f, 100f, 2000f, 1500f, 20f);
            Assert.AreEqual(1f, transform.Zoom, 0.0001);
        }

        [Test] public void ScreenAndWorldConversionsRoundTrip()
        {
            var transform = new GraphViewTransform();
            transform.Set(0.7f, 55f, 31f);
            Assert.AreEqual(123f, transform.ToScreenX(transform.ToWorldX(123f)), 0.001);
            Assert.AreEqual(-9f, transform.ToScreenY(transform.ToWorldY(-9f)), 0.001);
        }

        [Test] public void PanMovesTheOrigin()
        {
            var transform = new GraphViewTransform();
            transform.PanBy(15f, -5f);
            Assert.AreEqual(15f, transform.PanX, 0.0001);
            Assert.AreEqual(-5f, transform.PanY, 0.0001);
        }
    }
}
