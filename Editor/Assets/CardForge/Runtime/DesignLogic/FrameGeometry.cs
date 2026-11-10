using System;
using System.Linq;
using UnityEngine;

namespace CardForge.DesignLogic
{
    /// <summary>Pure coordinate conversion from a vision model's normalized 1000x1000 bounding box.</summary>
    public static class FrameGeometry
    {
        /// <summary>Rejects invalid rectangles; returns a clamped top-left-origin card rectangle.</summary>
        public static bool TryNormalize(float x,float y,float w,float h,int imageWidth,int imageHeight,out Rect result)
        {
            result=default;
            if(imageWidth<1||imageHeight<1||float.IsNaN(x)||float.IsNaN(y)||float.IsNaN(w)||
               float.IsNaN(h)||float.IsInfinity(x)||float.IsInfinity(y)||float.IsInfinity(w)||
               float.IsInfinity(h)||w<=5||h<=5)return false;
            float left=Mathf.Clamp(x,0,990),top=Mathf.Clamp(y,0,990);
            result=new Rect(left*imageWidth/1000f,top*imageHeight/1000f,
                Mathf.Clamp(w,5,1000-left)*imageWidth/1000f,
                Mathf.Clamp(h,5,1000-top)*imageHeight/1000f);
            return result.width>=1&&result.height>=1;
        }
    }

    /// <summary>Defines independent, lossless atlas slices and consistent asset names.</summary>
    public static class AtlasGeometry
    {
        /// <summary>Bottom-left-origin rectangle for row-major, top-left-indexed sprite atlas cell.</summary>
        public static RectInt Cell(int width,int height,int columns,int rows,int index,int inset=0)
        {
            if(width<1||height<1||columns<1||rows<1||columns>width||rows>height||
                index<0||index>=columns*rows)
                throw new ArgumentOutOfRangeException(nameof(index));
            int col=index%columns,row=index/columns;
            int left=col*width/columns,right=(col+1)*width/columns;
            int top=row*height/rows,bottom=(row+1)*height/rows;
            int pad=Mathf.Clamp(inset,0,Math.Min(right-left-1,bottom-top-1)/2);
            return new RectInt(left+pad,height-bottom+pad,right-left-pad*2,bottom-top-pad*2);
        }
        /// <summary>Normalizes untrusted names for asset file and folder paths.</summary>
        public static string Slug(string name)
        {
            var value=new string((name??"").ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)?c:'-').ToArray()).Trim('-');
            while(value.Contains("--"))value=value.Replace("--","-");
            return string.IsNullOrEmpty(value)?"asset":value;
        }
    }
}
