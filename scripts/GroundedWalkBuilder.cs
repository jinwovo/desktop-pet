using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public sealed class RigFootSample
{
    public double X, Lift, Swing;
    public bool IsPlanted;
}
public sealed class RigPoseSample
{
    public RigFootSample Near, Far;
    public double BodyBob;
}
public static class GroundedWalkBuilder
{
    private static double Wrap(double p) { return p - Math.Floor(p); }
    public static RigFootSample SampleFoot(double phase, double distance, double lift)
    {
        double p = Wrap(phase);
        if (p < 0.6) return new RigFootSample { X = (0.3 - p) * distance, Lift = 0, IsPlanted = true, Swing = 0 };
        double u = (p - 0.6) / 0.4;
        double u2 = u * u, u3 = u2 * u;
        double x = (2*u3-3*u2+1)*(-0.3*distance) + (u3-2*u2+u)*(-0.4*distance)
                 + (-2*u3+3*u2)*(0.3*distance) + (u3-u2)*(-0.4*distance);
        return new RigFootSample { X = x, Lift = lift * Math.Pow(Math.Sin(Math.PI*u),2), IsPlanted = false, Swing = u };
    }
    public static RigPoseSample Sample(double phase, double distance, double lift)
    {
        double p=Wrap(phase);
        return new RigPoseSample { Near=SampleFoot(p,distance,lift), Far=SampleFoot(p+0.5,distance,lift),
            BodyBob=-0.18*lift*Math.Cos(4*Math.PI*(p-0.3)) };
    }
    public static Bitmap ResizePart(Bitmap source,int height)
    {
        int width=Math.Max(1,(int)Math.Round(source.Width*(double)height/source.Height));
        var result=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(result)) {
            g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            g.DrawImage(source,new Rectangle(0,0,width,height));
        }
        return result;
    }
    private static void Foot(Graphics g,Bitmap bitmap,RigFootSample foot,double settle,bool far)
    {
        double lift=foot.Lift*(1-settle);
        double pitch=foot.IsPlanted?0:-Math.Min(12,lift*2.5)*Math.Sin(2*Math.PI*foot.Swing);
        var state=g.Save();
        g.TranslateTransform((float)(64+foot.X),(float)(122-lift));
        g.RotateTransform((float)pitch);
        if(far) {
            using(var attributes=new ImageAttributes()) {
                var matrix=new ColorMatrix();matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=0.91f;
                attributes.SetColorMatrix(matrix);
                g.DrawImage(bitmap,new Rectangle(-bitmap.Width/2,-bitmap.Height,bitmap.Width,bitmap.Height),
                    0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attributes);
            }
        } else g.DrawImage(bitmap,(float)(-bitmap.Width/2.0),-bitmap.Height,bitmap.Width,bitmap.Height);
        g.Restore(state);
    }
    public static Bitmap Render(Bitmap body,Bitmap nearFoot,Bitmap farFoot,double phase,double distance,double lift,double settle)
    {
        var pose=Sample(phase,distance,lift);
        var result=new Bitmap(128,128,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(result)) {
            g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.SmoothingMode=SmoothingMode.HighQuality;
            Foot(g,farFoot,pose.Far,settle,true);
            Foot(g,nearFoot,pose.Near,settle,false);
            g.DrawImage(body,(float)(64-body.Width/2.0),(float)(112-body.Height+pose.BodyBob*(1-settle)),body.Width,body.Height);
        }
        return result;
    }
    public static Bitmap ExportPart(Bitmap part,bool body,bool far)
    {
        var result=new Bitmap(128,128,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(result)) {
            g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            if(far) {
                using(var attributes=new ImageAttributes()) {
                    var matrix=new ColorMatrix();matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=0.91f;
                    attributes.SetColorMatrix(matrix);
                    g.DrawImage(part,new Rectangle(64-part.Width/2,122-part.Height,part.Width,part.Height),
                        0,0,part.Width,part.Height,GraphicsUnit.Pixel,attributes);
                }
            } else g.DrawImage(part,(float)(64-part.Width/2.0),body?112-part.Height:122-part.Height,part.Width,part.Height);
        }
        return result;
    }
}
