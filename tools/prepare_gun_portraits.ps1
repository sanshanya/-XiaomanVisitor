param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$gunProject = Split-Path -Parent $PSScriptRoot
$gunBase = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_tavern_atlas.png'
$gunOutput = Join-Path $gunProject 'package\XiaomanVisitor\Portraits\xiaoman_gun_atlas.png'
$gunReview = Join-Path $gunProject '.dev\gun-reaction'
$gunLayers = Join-Path $gunProject 'assets\layers'
New-Item -ItemType Directory -Path $gunReview -Force | Out-Null
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
    static Bitmap Layer(string path) {
        // Original source canvas and transform, identical for every component.
        // No independent facial anchors, stretching, masks or skin repainting.
        using(var raw=new Bitmap(path))
        using(var padded=new Bitmap(340,608,PixelFormat.Format32bppArgb)) {
            if(raw.Width!=1024 || raw.Height!=576) throw new InvalidDataException("Original layer dimensions changed");
            for(int y=0;y<576;y++) for(int x=0;x<340;x++)
                padded.SetPixel(x,y+32,raw.GetPixel(x+342,y));
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
    public static void Prepare(string basePath,string layers,string output,string review) {
        using(var original=new Bitmap(basePath))
        using(var eyes=Layer(Path.Combine(layers,"normal_1_1.png")))
        using(var brows=Layer(Path.Combine(layers,"normal_3_1.png")))
        using(var sweat=Layer(Path.Combine(layers,"normal_4_2.png")))
        using(var atlas=new Bitmap(1152,1376,PixelFormat.Format32bppArgb)) {
            if(original.Width!=2304 || original.Height!=2064) throw new InvalidDataException("Approved atlas dimensions changed");
            for(int row=0;row<2;row++) for(int mouth=0;mouth<3;mouth++) {
                // Frames 15..17 already contain reviewed original worried brows,
                // eyes and three mouth states. Reuse them rather than redraw.
                using(var frame=Frame(original,15+mouth)) {
                    if(row==1) { Overlay(frame,eyes); Overlay(frame,brows); }
                    Overlay(frame,sweat);
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
                g.DrawString("Worried + original sweat",font,Brushes.White,396,6);
                g.DrawString("Softer eyes + original sweat",font,Brushes.White,780,6);
                g.DrawImageUnscaled(normal,0,40);
                g.DrawImage(atlas,new Rectangle(384,40,Width,Height),new Rectangle(0,0,Width,Height),GraphicsUnit.Pixel);
                g.DrawImage(atlas,new Rectangle(768,40,Width,Height),new Rectangle(0,Height,Width,Height),GraphicsUnit.Pixel);
                comparison.Save(Path.Combine(review,"native-layer-comparison.png"),ImageFormat.Png);
            }
        }
    }
}
'@
[NativeGunPortraits]::Prepare($gunBase,$gunLayers,$gunOutput,$gunReview)
$gunSources = @('assets/layers/normal_1_1.png','assets/layers/normal_3_1.png','assets/layers/normal_4_2.png') | ForEach-Object {
    [ordered]@{path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $gunProject $_) -Algorithm SHA256).Hash.ToLowerInvariant()}
}
$gunRecord = [ordered]@{
    schema=2; method='original-layer-composition'; sources=@($gunSources)
    base='Portraits/xiaoman_tavern_atlas.png'; base_frames=@(15,16,17)
    packaged='Portraits/xiaoman_gun_atlas.png'; frame=@(384,688); columns=3; rows=@('worried','worried_soft')
    original_canvas=@(1024,576); crop=@(342,0,682,576); padded_frame=@(340,608)
    runtime_rectangle=@(0,4,384,684); edited_bounds=@(69,174,305,334); mouth_bounds=@(176,310,204,334)
    base_sha256=(Get-FileHash -LiteralPath $gunBase -Algorithm SHA256).Hash.ToLowerInvariant()
    sha256=(Get-FileHash -LiteralPath $gunOutput -Algorithm SHA256).Hash.ToLowerInvariant()
    body_and_silhouette_preserved=$true; generated_face_used=$false
    art_status='Original-layer baseline; exaggerated fear and additional forehead shadow await pixel-art review'
    new_runtime_objects=6; lazy_load=$true; mipmaps=$false
}
[IO.File]::WriteAllText((Join-Path $gunProject 'assets\gun-portraits\manifest.json'),($gunRecord | ConvertTo-Json -Depth 5) + "`n",(New-Object Text.UTF8Encoding($false)))
Write-Output ('Prepared six original-layer gun reaction frames: ' + $gunOutput)
