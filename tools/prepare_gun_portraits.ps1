param([switch]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$gunProject = Split-Path -Parent $PSScriptRoot
$gunBase = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_tavern_atlas.png'
$gunOutput = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_gun_atlas.png'
$gunReview = Join-Path $gunProject '.dev\gun-reaction'
$gunLayers = Join-Path $gunProject 'assets\layers'
New-Item -ItemType Directory -Path $gunReview -Force | Out-Null
if ($Preview) { $gunOutput = Join-Path $gunReview 'xiaoman_gun_atlas-candidate.png' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class NativeGunPortraits {
    const int Width=384, Height=688;
    static Bitmap Frame(Bitmap atlas,int index) {
        return atlas.Clone(new Rectangle(index%6*Width,index/6*Height,Width,Height),PixelFormat.Format32bppArgb);
    }
    static Bitmap CanvasLayer(string path) {
        // Original source canvas and transform, identical for every component.
        // No separate face registration or repainting of the original skin.
        using(var raw=new Bitmap(path)) {
            var padded=new Bitmap(340,608,PixelFormat.Format32bppArgb);
            if(raw.Width!=1024 || raw.Height!=576) throw new InvalidDataException("Original layer dimensions changed");
            for(int y=0;y<576;y++) for(int x=0;x<340;x++)
                padded.SetPixel(x,y+32,raw.GetPixel(x+342,y));
            return padded;
        }
    }
    static Bitmap ResizeLayer(Bitmap padded) {
            var result=new Bitmap(Width,Height,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(result))
            using(var attributes=new ImageAttributes()) {
                g.CompositingMode=CompositingMode.SourceCopy;
                g.InterpolationMode=InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode=PixelOffsetMode.Half;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(padded,new Rectangle(0,4,384,684),0,0,340,608,GraphicsUnit.Pixel,attributes);
            }
            return result;
    }
    static void Overlay(Bitmap frame,Bitmap layer) {
        // Touch only actual source-layer pixels. GDI+ drawing over a whole
        // transparent canvas would round unrelated semi-transparent body RGB.
        for(int y=0;y<Height;y++) for(int x=0;x<Width;x++) {
            Color source=layer.GetPixel(x,y);
            if(source.A==0) continue;
            Color target=frame.GetPixel(x,y);
            if(target.A!=255) throw new InvalidDataException("Facial layer escaped the opaque original face at "+x+","+y+"; source alpha="+source.A+", target alpha="+target.A);
            int a=source.A, b=255-a;
            frame.SetPixel(x,y,Color.FromArgb(255,
                (source.R*a+target.R*b+127)/255,
                (source.G*a+target.G*b+127)/255,
                (source.B*a+target.B*b+127)/255));
        }
    }
    static Point[] Points(bool mirror,int phase,params int[] xy) {
        var result=new Point[xy.Length/2];
        for(int i=0;i<result.Length;i++) {
            int x=xy[i*2],y=xy[i*2+1];
            if(phase==1) { if(y<214)y+=4; else if(y>220)y-=2; }
            result[i]=new Point(mirror?336-x:x,y);
        }
        return result;
    }
    static void Eye(Bitmap art,Bitmap original,bool right,int phase) {
        Point[] opening=Points(right,phase,86,218,94,206,104,198,114,196,126,196,138,202,148,214,144,226,136,236,126,242,112,242,100,236,94,228);
        Point[] lid=Points(right,phase,84,218,90,208,100,200,112,194,124,194,136,200,146,210,150,216,144,216,136,206,124,200,114,200,104,204,94,214,90,222);
        using(var g=Graphics.FromImage(art))
        using(var ink=new SolidBrush(Color.FromArgb(0,0,0)))
        using(var white=new SolidBrush(Color.FromArgb(224,236,255)))
        using(var upper=new SolidBrush(Color.FromArgb(187,203,236)))
        using(var path=new GraphicsPath()) {
            g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;
            g.FillPolygon(white,opening);
            path.AddPolygon(opening);g.SetClip(path);
            g.FillRectangle(upper,70,188,200,phase==0?20:24);
            g.ResetClip();g.FillPolygon(ink,lid);
            g.FillPolygon(ink,Points(right,phase,88,214,82,206,84,218,92,222));
            g.FillPolygon(ink,Points(right,phase,94,208,88,200,88,212,96,214));
        }
        // Reuse the original blue iris pixels, with a smaller pupil footprint.
        // Only the iris is resampled, on the original two-pixel drawing grid.
        int cx=right?218:118,cy=220,rx=phase==0?12:14,ry=phase==0?16:17;
        int sx=right?202:104;
        for(int y=cy-ry;y<cy+ry;y+=2) for(int x=cx-rx;x<cx+rx;x+=2) {
            double q=Math.Pow((x+1-cx)/(double)rx,2)+Math.Pow((y+1-cy)/(double)ry,2);
            if(q>1)continue;
            Color c=q>.78?Color.FromArgb(18,39,116):original.GetPixel(sx+(x-cx+rx)*32/(rx*2),206+(y-cy+ry)*32/(ry*2));
            if(c.R==153&&c.G==176&&c.B==231)c=Color.FromArgb(4,50,144);
            for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)art.SetPixel(x+dx,y+dy,c);
        }
    }
    static Bitmap PixelArt(string portrait,string layers,int phase) {
        var art=CanvasLayer(Path.Combine(layers,"normal_1_2.png"));
        using(var original=new Bitmap(portrait))
        using(var brows=CanvasLayer(Path.Combine(layers,"normal_3_1.png"))) {
            using(var g=Graphics.FromImage(art))
                g.DrawImage(brows,new Rectangle(0,0,340,608),new Rectangle(0,0,340,608),GraphicsUnit.Pixel);
            // Shade only the original exposed skin palette, leaving bangs,
            // face contour, nose highlight, cheeks and mouth pixels intact.
            for(int y=174;y<248;y++)for(int x=82;x<256;x++) {
                Color c=art.GetPixel(x,y);if(c.A==0)c=original.GetPixel(x,y);
                if(!((c.R==153&&c.G==176&&c.B==231)||(c.R==135&&c.G==115&&c.B==166)))continue;
                int a=100*(248-y)*(248-y)/(74*74);
                a=a/6*6;
                if(phase==1)a=a*4/5;
                art.SetPixel(x,y,Color.FromArgb(255,(c.R*(255-a)+48*a)/255,(c.G*(255-a)+56*a)/255,(c.B*(255-a)+107*a)/255));
            }
            Eye(art,original,false,phase);Eye(art,original,true,phase);
            using(var g=Graphics.FromImage(art))
            using(var outline=new SolidBrush(Color.FromArgb(35,65,122)))
            using(var fill=new SolidBrush(Color.FromArgb(134,222,248)))
            using(var glint=new SolidBrush(Color.FromArgb(240,252,255))) {
                g.SmoothingMode=SmoothingMode.None;g.PixelOffsetMode=PixelOffsetMode.Half;
                g.FillPolygon(outline,new[]{new Point(254,210),new Point(258,214),new Point(264,226),new Point(264,232),new Point(260,238),new Point(254,238),new Point(250,234),new Point(250,228),new Point(252,218)});
                g.FillPolygon(fill,new[]{new Point(254,216),new Point(258,220),new Point(262,228),new Point(262,232),new Point(258,236),new Point(254,234),new Point(252,230)});
                g.FillRectangle(glint,254,222,2,8);
            }
        }
        return art;
    }
    public static void Prepare(string basePath,string layers,string portrait,string output,string review) {
        using(var original=new Bitmap(basePath))
        using(var atlas=new Bitmap(1152,1376,PixelFormat.Format32bppArgb)) {
            if(original.Width!=2304 || original.Height!=2064) throw new InvalidDataException("Approved atlas dimensions changed");
            for(int row=0;row<2;row++)
            using(var authored=PixelArt(portrait,layers,row))
            using(var detail=ResizeLayer(authored))
            for(int mouth=0;mouth<3;mouth++) {
                using(var frame=Frame(original,15+mouth)) {
                    Overlay(frame,detail);
                    for(int y=0;y<Height;y++) for(int x=0;x<Width;x++)
                        atlas.SetPixel(mouth*Width+x,row*Height+y,frame.GetPixel(x,y));
                    frame.Save(Path.Combine(review,"frame-"+row+"-"+mouth+".png"),ImageFormat.Png);
                }
            }
            atlas.Save(output,ImageFormat.Png);
            using(var comparison=new Bitmap(1152,728,PixelFormat.Format32bppArgb))
            using(var g=Graphics.FromImage(comparison))
            using(var font=new Font("Segoe UI",16))
            using(var normal=Frame(original,0)) {
                g.Clear(Color.FromArgb(25,28,43));
                g.DrawString("Original",font,Brushes.White,12,6);
                g.DrawString("Startled",font,Brushes.White,396,6);
                g.DrawString("Frightened",font,Brushes.White,780,6);
                g.DrawImage(normal,new Rectangle(0,40,Width,Height),new Rectangle(0,0,Width,Height),GraphicsUnit.Pixel);
                g.DrawImage(atlas,new Rectangle(384,40,Width,Height),new Rectangle(0,0,Width,Height),GraphicsUnit.Pixel);
                g.DrawImage(atlas,new Rectangle(768,40,Width,Height),new Rectangle(0,Height,Width,Height),GraphicsUnit.Pixel);
                comparison.Save(Path.Combine(review,"native-layer-comparison.png"),ImageFormat.Png);
            }
        }
    }
}
'@
[NativeGunPortraits]::Prepare($gunBase,$gunLayers,(Join-Path $gunProject 'assets\portraits\xiaoman_neutral_0.png'),$gunOutput,$gunReview)
if ($Preview) { Write-Output ('Prepared preview only: ' + $gunOutput); return }
$gunSources = @('assets/layers/normal_1_2.png','assets/layers/normal_3_1.png','assets/portraits/xiaoman_neutral_0.png') | ForEach-Object {
    [ordered]@{path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $gunProject $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$gunRecord = [ordered]@{
    schema=3; method='manual-native-pixel-art'; sources=@($gunSources)
    base='Portraits/xiaoman_tavern_atlas.png'; base_frames=@(15,16,17)
    packaged='Portraits/xiaoman_gun_atlas.png'; frame=@(384,688); columns=3; rows=@('startled','frightened')
    original_canvas=@(1024,576); crop=@(342,0,682,576); padded_frame=@(340,608)
    runtime_rectangle=@(0,4,384,684); edited_bounds=@(69,174,305,334); mouth_bounds=@(176,310,204,334)
    base_sha256=(Get-FileHash -LiteralPath $gunBase -Algorithm SHA256).Hash.ToLowerInvariant()
    sha256=(Get-FileHash -LiteralPath $gunOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    body_and_silhouette_preserved=$true; generated_face_used=$false
    authoring_file='tools/prepare_gun_portraits.ps1'
    art_status='Manual pixel-art fear, original blue irises and mouths; offline preview inspected, author gameplay review pending'
    new_runtime_objects=6; lazy_load=$true; mipmaps=$false
}
[IO.File]::WriteAllText((Join-Path $gunProject 'assets\gun-portraits\manifest.json'),($gunRecord | ConvertTo-Json -Depth 5) + "`n",(New-Object Text.UTF8Encoding($false)))
Write-Output ('Prepared six original-layer gun reaction frames: ' + $gunOutput)
