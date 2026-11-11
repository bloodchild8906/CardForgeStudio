using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CardForge.DesignLogic;

namespace CardForge.DesignLogic.Tests
{
    /// <summary>Non-UI regression tests for frame coordinate normalization and icon atlas slicing.</summary>
    public sealed class FrameGeometryTests
    {
        [Test] public void NormalizedRegionsStayWithinImage()
        {
            Assert.IsTrue(FrameGeometry.TryNormalize(960,930,500,200,360,504,out var r));
            Assert.Greater(r.width,0);Assert.LessOrEqual(r.xMax,360.01f);Assert.LessOrEqual(r.yMax,504.01f);
        }
        [Test] public void NonFiniteAndDegenerateRegionsAreRejected()
        {
            Assert.IsFalse(FrameGeometry.TryNormalize(float.NaN,1,20,20,360,504,out _));
            Assert.IsFalse(FrameGeometry.TryNormalize(1,1,0,20,360,504,out _));
        }
        [Test] public void UnevenAtlasCellsCoverEntireImage()
        {
            var rectangles=Enumerable.Range(0,12).Select(i=>AtlasGeometry.Cell(101,103,4,3,i)).ToArray();
            Assert.AreEqual(101*103,rectangles.Sum(r=>r.width*r.height));
            for(int i=0;i<12;i++)for(int j=i+1;j<12;j++)Assert.IsFalse(rectangles[i].Overlaps(rectangles[j]));
        }
        [Test] public void FileNameIsSanitized()
        {
            Assert.AreEqual("mana-icon-2",AtlasGeometry.Slug("Mana Icon #2"));
        }
    }
}
