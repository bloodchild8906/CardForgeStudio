using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class AstralArtRenderer
{
    private static PointF P(float x, float y) { return new PointF(x, y); }
    private static Color A(Color c, int alpha) { return Color.FromArgb(Math.Min(255,Math.Max(0,alpha)), c.R,c.G,c.B); }
    private static Color RGB(int r,int g,int b) { return Color.FromArgb(r,g,b); }
    private static void Poly(Graphics g,Brush b,params PointF[] points) { g.FillPolygon(b,points); }
    private static void Line(Graphics g,Color c,float w,float x1,float y1,float x2,float y2)
    {
        using(var pen=new Pen(c,w)) {pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;g.DrawLine(pen,x1,y1,x2,y2);}
    }
    private static void Crystal(Graphics g,Brush core,Brush shadow,Color accent,float x,float y,float rx,float ry)
    {
        Poly(g,shadow,P(x,y-ry),P(x+rx,y),P(x,y+ry),P(x-rx,y));
        Poly(g,core,P(x,y-ry),P(x+rx,y),P(x,y+ry),P(x-3,y+5));
        Line(g,A(accent,140),1.3f,x,y-ry,x,y+ry);
    }
    private static void Symbol(Graphics g,Color c,float cx,float cy,float radius,int sides)
    {
        PointF[] points=new PointF[sides];
        for(int i=0;i<sides;i++)
        {
            double angle=Math.PI*2*i/sides-Math.PI/2;
            points[i]=P(cx+(float)Math.Cos(angle)*radius,cy+(float)Math.Sin(angle)*radius);
        }
        using(var pen=new Pen(c,1.9f))g.DrawPolygon(pen,points);
    }
    public static void Render(string id,string type,string faction,string sourceRoot,string serverRoot)
    {
        int number=0;
        int.TryParse(id.Substring(id.LastIndexOf('-')+1),out number);
        int factionIndex= faction=="Solar"?0:faction=="Tidal"?1:faction=="Verdant"?2:3;
        int seed=number*11771+factionIndex*5317+3401;
        var rng=new Random(seed);
        Color top,bottom,glow,mid,dark;
        switch(factionIndex)
        {
            case 0:top=RGB(8,18,46);bottom=RGB(115,40,52);glow=RGB(255,183,78);mid=RGB(236,94,57);dark=RGB(25,28,49);break;
            case 1:top=RGB(8,26,50);bottom=RGB(26,88,111);glow=RGB(140,235,246);mid=RGB(62,146,174);dark=RGB(13,48,80);break;
            case 2:top=RGB(7,33,36);bottom=RGB(38,90,52);glow=RGB(177,245,145);mid=RGB(77,160,111);dark=RGB(21,46,41);break;
            default:top=RGB(21,16,48);bottom=RGB(66,35,94);glow=RGB(215,153,245);mid=RGB(113,73,173);dark=RGB(23,20,52);break;
        }
        const int W=420,H=580;
        Directory.CreateDirectory(sourceRoot);
        Directory.CreateDirectory(serverRoot);
        using(var bmp=new Bitmap(W,H,PixelFormat.Format24bppRgb))
        using(var g=Graphics.FromImage(bmp))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var gradient=new LinearGradientBrush(new Rectangle(0,0,W,H),top,bottom,90))
                g.FillRectangle(gradient,0,0,W,H);
            using(var fog=new SolidBrush(A(mid,42)))
            using(var ink=new SolidBrush(dark))
            using(var deep=new SolidBrush(Color.FromArgb(dark.R/2,dark.G/2,dark.B/2)))
            using(var light=new SolidBrush(A(glow,225)))
            using(var edge=new SolidBrush(A(glow,78)))
            using(var nebula=new SolidBrush(A(glow,16)))
            using(var white=new SolidBrush(A(Color.White,225)))
            {
                int cx=110+rng.Next(200),cy=108+rng.Next(145),rad=45+rng.Next(62);
                for(int i=6;i>=0;i--) {int d=rad*2+i*46;g.FillEllipse(nebula,cx-d/2,cy-d/2,d,d);}
                g.FillEllipse(fog,cx-rad,cy-rad,rad*2,rad*2);
                g.FillEllipse(edge,cx-rad*3/4,cy-rad*3/4,rad*3/2,rad*3/2);
                for(int i=0;i<95;i++)
                {
                    int x=rng.Next(W),y=rng.Next(444),r=rng.Next(1,4);
                    g.FillEllipse(white,x,y,r,r);
                    if(i%13==0) Line(g,A(Color.White,110),1,x-7,y,x+7,y);
                }
                // Layered watercolor-like scenery. Each card has a unique horizon.
                for(int layer=0;layer<3;layer++)
                {
                    int baseline=390+layer*49;
                    PointF[] pts=new PointF[18];
                    pts[0]=P(0,H);pts[1]=P(0,baseline);
                    for(int i=0;i<14;i++)
                    {
                        float x=i*32;
                        float height=rng.Next(12,78);
                        pts[i+2]=P(x,baseline-height);
                    }
                    pts[16]=P(W,baseline);pts[17]=P(W,H);
                    Poly(g,layer==0?fog:layer==1?ink:deep,pts);
                }
                for(int i=0;i<3;i++)
                {
                    float r=125+i*27;
                    using(var pen=new Pen(A(glow,95-i*26),2.4f-i*.5f))
                        g.DrawArc(pen,210-r,255-r,r*2,r*2,18+rng.Next(40),155+rng.Next(120));
                }
                switch(type)
                {
                    case "Commander":
                        Poly(g,deep,P(99,540),P(126,370),P(164,328),P(181,286),P(245,286),P(268,328),P(301,372),P(323,540));
                        Poly(g,ink,P(155,545),P(158,334),P(181,313),P(240,313),P(260,335),P(278,545));
                        g.FillEllipse(ink,169,220,80,100);
                        Poly(g,light,P(163,238),P(179,202),P(194,216),P(211,189),P(224,212),P(243,199),P(256,243),P(230,259),P(190,259));
                        Crystal(g,light,deep,glow,211,345,19,29);
                        Line(g,glow,7,300,172,289,534);
                        Crystal(g,light,deep,glow,300,154,30,45);
                        for(int i=0;i<7;i++)Line(g,A(glow,143),2,116+i*31,354,102+i*37,494);
                        break;
                    case "ManaSource":
                        Symbol(g,A(glow,137),210,286,116,7);
                        Crystal(g,light,deep,glow,210,278,64,152);
                        Crystal(g,edge,deep,glow,150,352,27,91);
                        Crystal(g,edge,deep,glow,270,359,31,101);
                        Poly(g,ink,P(105,483),P(144,427),P(278,427),P(311,483),P(279,521),P(126,521));
                        for(int i=0;i<3;i++)Crystal(g,light,deep,glow,161+i*49,459,12,19);
                        Line(g,A(glow,212),3,110,487,310,487);
                        break;
                    case "Creature":
                        switch(seed%6)
                        {
                            case 0:
                                Poly(g,deep,P(108,526),P(134,376),P(172,322),P(244,322),P(282,379),P(305,526));
                                g.FillEllipse(ink,169,235,80,96);
                                Poly(g,light,P(160,263),P(171,236),P(247,236),P(261,263),P(233,284),P(188,284));
                                Line(g,glow,6,270,182,278,505);Crystal(g,light,ink,glow,267,180,29,48);
                                break;
                            case 1:
                                Poly(g,deep,P(207,331),P(67,232),P(103,380),P(176,369),P(207,432),P(238,369),P(324,380),P(354,232));
                                g.FillEllipse(ink,175,283,67,98);
                                Poly(g,light,P(193,306),P(214,266),P(251,301),P(244,328),P(262,341),P(241,343));
                                Line(g,glow,5,202,393,154,529);
                                break;
                            case 2:
                                g.FillEllipse(ink,91,340,232,106);g.FillEllipse(deep,250,304,86,87);
                                Poly(g,light,P(282,305),P(299,254),P(315,298),P(342,274),P(338,327));
                                for(int i=0;i<4;i++)Poly(g,deep,P(124+i*48,414),P(146+i*48,414),P(139+i*48,525),P(119+i*48,525));
                                Line(g,glow,5,108,376,59,326);
                                break;
                            case 3:
                                Line(g,dark,44,205,522,207,243);
                                for(int i=0;i<8;i++)
                                {
                                    int x=92+rng.Next(230),y=227+rng.Next(211);
                                    Line(g,dark,12,210,y,x,y-68);
                                    g.FillEllipse(light,x-32,y-121,77,80);g.FillEllipse(ink,x-17,y-115,45,54);
                                }
                                g.FillEllipse(ink,184,236,51,51);Crystal(g,light,deep,glow,208,335,19,42);
                                break;
                            case 4:
                                Poly(g,ink,P(164,269),P(207,209),P(252,270),P(237,354),P(178,354));
                                Poly(g,deep,P(150,347),P(255,347),P(277,466),P(244,530),P(159,530),P(119,466));
                                Crystal(g,light,ink,glow,211,369,42,64);
                                Crystal(g,edge,deep,glow,121,398,29,78);
                                Crystal(g,edge,deep,glow,295,398,30,82);
                                g.FillEllipse(light,190,298,11,10);g.FillEllipse(light,221,298,11,10);
                                break;
                            default:
                                Poly(g,deep,P(209,298),P(62,345),P(87,391),P(165,419),P(208,481),P(253,416),P(333,388),P(359,346));
                                Poly(g,ink,P(172,337),P(206,307),P(246,337),P(220,416),P(204,452),P(182,418));
                                Line(g,glow,5,207,402,214,545);
                                g.FillEllipse(light,183,356,9,9);g.FillEllipse(light,228,356,9,9);
                                break;
                        }
                        break;
                    case "Spell":
                        for(int ring=0;ring<3;ring++)Symbol(g,A(glow,214-ring*55),210,293,65+ring*50,6+ring);
                        Crystal(g,light,deep,glow,210,290,44,105);
                        for(int i=0;i<9;i++)
                        {
                            double a=2*Math.PI*i/9+seed*.001;
                            int x=210+(int)(Math.Cos(a)*154),y=291+(int)(Math.Sin(a)*154);
                            Crystal(g,edge,ink,glow,x,y,13,29);
                        }
                        Line(g,A(glow,210),4,76,465,340,180);
                        break;
                    case "QuickSpell":
                        for(int i=0;i<5;i++)
                        {
                            int o=(i-2)*19;
                            Poly(g,i%2==0?light:edge,P(74+o,469),P(187+o,287),P(153+o,294),P(324-o,109),P(229-o,297),P(270-o,285));
                        }
                        Symbol(g,A(glow,140),214,323,82,5);
                        for(int i=0;i<4;i++)Line(g,A(Color.White,178),2,83+i*58,418+rng.Next(47),241+rng.Next(115),138+rng.Next(139));
                        break;
                }
                using(var p1=new Pen(A(glow,120),2))
                {
                    g.DrawRectangle(p1,12,12,395,555);
                    g.DrawRectangle(p1,17,17,385,545);
                }
            }
            var path=Path.Combine(sourceRoot,id+".png");
            bmp.Save(path,ImageFormat.Png);
            File.Copy(path,Path.Combine(serverRoot,id+".png"),true);
        }
    }
}
