param([switch]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$gunProject = Split-Path -Parent $PSScriptRoot
$gunSource = Join-Path $gunProject 'assets\gun-portraits\source.png'
$gunBase = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_tavern_atlas.png'
$gunOutput = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_gun_atlas.png'
$gunReview = Join-Path $gunProject '.dev\gun-reaction'
New-Item -ItemType Directory -Path $gunReview -Force | Out-Null
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class GunPortraitAuthoring {
    static void Pixels(Graphics g) {
        g.InterpolationMode=InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode=PixelOffsetMode.Half;g.SmoothingMode=SmoothingMode.None;
    }
    static Bitmap Crop(Bitmap source,Rectangle rect) { return source.Clone(rect,PixelFormat.Format32bppArgb); }
    static Bitmap Mouth(Bitmap source,int x,int y) {
        int l=x+30,r=x-30,t=y+22,b=y-22;
        for(int py=y-19;py<=y+19;py++) for(int px=x-24;px<=x+24;px++) {
            Color c=source.GetPixel(px,py);
            if(c.R<110&&c.G<110&&c.B<160) { l=Math.Min(l,px);r=Math.Max(r,px);t=Math.Min(t,py);b=Math.Max(b,py); }
        }
        if(r<l||b<t) throw new InvalidDataException("No mouth in source cell");
        var rect=Rectangle.FromLTRB(l-2,t-2,r+3,b+3);
        Bitmap result=Crop(source,rect);
        Color skin=source.GetPixel(x-30,y);
        for(int py=0;py<result.Height;py++) for(int px=0;px<result.Width;px++) {
            Color c=result.GetPixel(px,py);
            double delta=Math.Sqrt(Math.Pow(c.R-skin.R,2)+Math.Pow(c.G-skin.G,2)+Math.Pow(c.B-skin.B,2));
            if(delta<44) result.SetPixel(px,py,Color.Transparent);
        }
        return result;
    }
    public static void Prepare(string sourcePath,string basePath,string outputPath,string review) {
        using(var source=new Bitmap(sourcePath))
        using(var bodyAtlas=new Bitmap(basePath))
        using(var body=Crop(bodyAtlas,new Rectangle(0,0,384,688)))
        using(var atlas=new Bitmap(1152,1376,PixelFormat.Format32bppArgb))
        using(var face=new GraphicsPath()) {
            if(source.Width!=887||source.Height!=1774) throw new InvalidDataException("Source registration changed; review new facial anchors first");
            face.AddPolygon(new[]{new Point(78,205),new Point(90,185),new Point(103,174),new Point(125,184),
                new Point(143,205),new Point(166,218),new Point(185,206),new Point(205,187),new Point(234,183),
                new Point(250,201),new Point(258,225),new Point(248,254),new Point(231,285),new Point(200,307),
                new Point(169,312),new Point(134,306),new Point(105,285),new Point(85,254),new Point(76,229)});
            int[] noseX={147,444,740};
            using(var ag=Graphics.FromImage(atlas)) {
                Pixels(ag);
                for(int row=0;row<2;row++) for(int mouth=0;mouth<3;mouth++) {
                    int noseY=row==0?373:1259;
                    int mouthY=row==0?405:1291;
                    using(var patch=new Bitmap(340,608,PixelFormat.Format32bppArgb))
                    using(var lips=Mouth(source,noseX[mouth],mouthY))
                    using(var composed=Crop(body,new Rectangle(0,0,384,688))) {
                        using(var pg=Graphics.FromImage(patch)) {
                            Pixels(pg);pg.SetClip(face);
                            // Lock one facial template per row. AI-generated mouth
                            // variants must never make eyes, brows or sweat jitter.
                            pg.DrawImage(source,new RectangleF(170-noseX[0]*1.12f,258-noseY*1.18f,source.Width*1.12f,source.Height*1.18f));
                            Color skin=source.GetPixel(noseX[0]-30,mouthY);
                            using(var fill=new SolidBrush(skin)) pg.FillRectangle(fill,143,272,55,40);
                            int w=mouth==0?12:mouth==1?14:16;
                            int h=mouth==0?3:mouth==1?8:12;
                            pg.DrawImage(lips,new Rectangle(170-w/2,281-h/2,w,h));
                        }
                        using(var cg=Graphics.FromImage(composed)) {
                            Pixels(cg);cg.DrawImage(patch,new Rectangle(0,4,384,684));
                        }
                        // Everything outside the facial patch is the exact reviewed
                        // native frame. No whole-body resize or new silhouette.
                        ag.DrawImageUnscaled(composed,mouth*384,row*688);
                        composed.Save(Path.Combine(review,"frame-"+row+"-"+mouth+".png"),ImageFormat.Png);
                    }
                }
            }
            // GDI+ compositing rounds RGB along semi-transparent body edges.
            // Restore straight RGBA from the original PNG outside the face,
            // rather than accepting a new body resample as an art change.
            for(int row=0;row<2;row++) for(int mouth=0;mouth<3;mouth++)
                for(int y=0;y<688;y++) for(int x=0;x<384;x++)
                    if(x<83||x>=293||y<193||y>=357)
                        atlas.SetPixel(mouth*384+x,row*688+y,bodyAtlas.GetPixel(x,y));
            atlas.Save(outputPath,ImageFormat.Png);
        }
    }
}
'@
[GunPortraitAuthoring]::Prepare($gunSource,$gunBase,$gunOutput,$gunReview)
$gunRecord = [ordered]@{
    schema=1; source='assets/gun-portraits/source.png'; base='Portraits/xiaoman_tavern_atlas.png'
    packaged='Portraits/xiaoman_gun_atlas.png'; frame=@(384,688); columns=3; rows=@('startled','frightened')
    edited_bounds=@(83,193,293,357); source_sha256=(Get-FileHash -LiteralPath $gunSource -Algorithm SHA256).Hash.ToLowerInvariant()
    base_sha256=(Get-FileHash -LiteralPath $gunBase -Algorithm SHA256).Hash.ToLowerInvariant()
    sha256=(Get-FileHash -LiteralPath $gunOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    body_and_silhouette_preserved=$true; new_runtime_objects=6; lazy_load=$true; mipmaps=$false
}
$gunJson = $gunRecord | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $gunProject 'assets\gun-portraits\manifest.json'),$gunJson + "`n",(New-Object Text.UTF8Encoding($false)))
Write-Output ('Prepared six gun reaction frames: ' + $gunOutput)
