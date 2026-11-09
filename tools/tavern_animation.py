"""Offline ambient rendering, adapted from the author's Animation Kit v1.1.

Keep gameplay interactions independent of decorative animation. Coordinates,
seed and timing come from scene.json; the supplied glass PNG is the real mask.
Only the authoring environment needs OpenCV. The game plays the packaged MP4.
"""
import hashlib
import json
import platform
from pathlib import Path
import shutil
import subprocess
import tempfile

import cv2
import numpy as np
from tavern_cassette import CassetteRhythm
from tavern_actor_light import sample_sources, write_table


class AmbientFrames:
    def __init__(self, scene, recipe):
        self.settings = recipe['animation']
        background = recipe['background']
        self.source = cv2.imread(str(scene / background['source']))
        if self.source is None or list(self.source.shape[1::-1]) != background['texture_size']:
            raise ValueError('Unexpected tavern source dimensions')
        w, h = background['encoded_size']
        self.base = cv2.copyMakeBorder(self.source, 0, h-self.source.shape[0], 0,
                                      w-self.source.shape[1], cv2.BORDER_REPLICATE)
        self.mask = cv2.imread(str(scene / background['mask']), cv2.IMREAD_GRAYSCALE)
        if self.mask is None or self.mask.shape != (h, w):
            raise ValueError('Glass mask must match encoded dimensions')
        self.alpha = self.mask[:530, :466].astype(np.float32) / 255
        self.base_f = self.base.astype(np.float32)
        self.yy, self.xx = np.mgrid[:h, :w].astype(np.float32)
        rng = np.random.default_rng(self.settings['seed'])
        self.drops = []
        for count, lengths, brightness, cycles, thickness in (
            (380, (2,6), (16,46), (1,2), (1,)),
            (250, (7,18), (25,72), (1,2), (1,1,1,2)),
            (48, (22,56), (8,21), (1,), (1,))):
            self.drops.append(dict(x=rng.uniform(0,466,count).astype(np.float32),
                y=rng.uniform(0,530,count).astype(np.float32), length=rng.integers(*lengths,size=count),
                brightness=rng.integers(*brightness,size=count), cycles=rng.choice(cycles,size=count),
                sway=rng.uniform(.08,.55,count).astype(np.float32),
                phase=rng.uniform(0,2*np.pi,count).astype(np.float32), thickness=rng.choice(thickness,size=count)))
        self._lighting_fields()
        self.cassette = CassetteRhythm(self.base) if self.settings['cassette_meter_baked'] else None

    def _lighting_fields(self):
        def g(x,y,sx,sy):
            return np.exp(-.5*(((self.xx-x)/sx)**2+((self.yy-y)/sy)**2))
        self.warm=(g(708,162,31,52)+.42*g(708,205,60,68)
            +.95*g(1170,190,31,52)+.4*g(1170,228,60,68)
            +.95*g(1292,162,31,52)+.4*g(1292,208,60,70)
            +.22*g(812,307,34,24)+.22*g(969,307,34,24)
            +.35*g(1296,525,40,45)+.16*g(220,568,25,20)
            +.28*g(444,398,22,20)+.28*g(621,398,22,20)
            +.15*g(694,563,135,25)+.18*g(1300,559,120,32))
        self.blue=(.78*g(972,43,145,34)+.28*g(972,77,190,68)
            +.2*g(420,850,100,110)+.26*g(568,531,38,34)+.24*g(1055,838,125,110))
        self.pink=(.84*g(722,611,290,25)+.72*g(1176,612,290,27)
            +.25*g(195,810,90,125)+.22*g(1137,861,115,105)
            +.16*g(750,548,300,22)+.16*g(1390,550,230,23))
        self.floor=np.clip((self.yy-760)/80,0,1)*(.35*g(200,842,105,210)
            +.52*g(445,847,95,225)+.52*g(1054,860,170,235)+.56*g(1335,858,135,245))
        self.counter=np.exp(-.5*((self.yy-558)/13)**2)*(.42*g(670,562,165,90)
            +.58*g(950,562,100,90)+.42*g(1297,561,150,90))
        # The kit's local cassette pass owns the screen, while room lighting
        # remains independent. Future playback interaction can replace the pass.
        self.device=np.ones(self.mask.shape,np.float32)
        self.device[429:461,1010:1086]=0

    def rain(self, phase):
        layers=[]
        for drops in self.drops:
            layer=np.zeros((530,466),np.uint8)
            for i in range(len(drops['x'])):
                x=drops['x'][i]+drops['sway'][i]*np.sin(2*np.pi*phase+drops['phase'][i])
                y=(drops['y'][i]+drops['cycles'][i]*530*phase)%530
                xi,yi=int(round(x)),int(round(y))
                if 0 <= xi < 466:
                    cv2.line(layer,(xi,yi-2),(xi,yi+int(drops['length'][i])),
                             int(drops['brightness'][i]),int(drops['thickness'][i]),cv2.LINE_AA)
            layers.append(layer.astype(np.float32))
        far,near,rivulets=layers
        soft=cv2.GaussianBlur(near,(5,5),.75)
        return (.38*far+.58*near+.34*soft+.28*rivulets)*self.alpha*self.settings['rain_gain']

    def frame(self, phase, cassette=True):
        phase=phase%1.0
        t=2*np.pi*phase
        lighting=self.settings['lighting']
        # Offset local lamp phases so the room does not brighten/darken as one
        # exposure adjustment. Spatial masks keep unlit walls steady.
        warm=(.65*np.sin(t+.2+self.xx*.004)+.2*np.sin(2*t+1.36+self.xx*.003)
              +.12*np.sin(3*t+2.03))*4.8*lighting['warm_gain']*self.warm
        blue=(.65*np.sin(t+1.84)+.18*np.sin(2*t+.25))*4.1*lighting['cool_gain']*self.blue
        pink=(.62*np.sin(t+.67)+.16*np.sin(2*t+2.2))*4*lighting['neon_gain']*self.pink
        shimmer=(1.35*lighting['floor_gain']*self.floor*np.sin(2*t+self.xx*.037+self.yy*.022)
                 +1.45*lighting['counter_gain']*self.counter*np.sin(t+self.xx*.025))
        delta=np.stack((warm*.3+blue+pink*.67+shimmer*.83,
                        warm*.66+blue*.72+pink*.38+shimmer*.55,
                        warm+blue*.46+pink*.97+shimmer*.8),axis=2)
        out=self.base_f+delta*self.device[...,None]
        rain=self.rain(phase)
        out[:530,:466]+=rain[...,None]*np.array([1,.8,.55],np.float32)
        # Round once at the end: tiny negative Gaussian tails must not darken
        # otherwise static pixels by one unit across most of the room.
        out=np.clip(np.rint(out),0,255).astype(np.uint8)
        return self.cassette.apply(out,phase) if cassette and self.cassette is not None else out


def render(scene, recipe):
    ffmpeg=shutil.which('ffmpeg')
    if ffmpeg is None:
        raise RuntimeError('FFmpeg is required only for offline tavern rendering')
    cfg=recipe['animation']; fps,seconds=cfg['fps'],cfg['seconds']
    if not isinstance(fps,int) or not isinstance(seconds,int) or min(fps,seconds)<1:
        raise ValueError('fps and seconds must be positive integers')
    frames=AmbientFrames(scene,recipe)
    count=fps*seconds; w,h=recipe['background']['encoded_size']
    target=scene/cfg['file']; temporary=target.with_suffix('.rendering.mp4')
    # Equal I/P quantizers and disabled adaptive quantization reduce differences
    # between the first keyframe and predicted frames at the loop boundary.
    command=[ffmpeg,'-hide_banner','-loglevel','error','-y','-f','rawvideo','-pix_fmt','bgr24',
        '-s',f'{w}x{h}','-r',str(fps),'-i','pipe:0','-an','-c:v','libx264','-preset','slow',
        '-qp',str(cfg['quality_qp']),'-vf','scale=in_range=full:out_range=limited:out_color_matrix=bt709,'
        'setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709',
        '-pix_fmt','yuv420p','-color_primaries','bt709','-color_trc','bt709','-colorspace','bt709','-profile:v','high',
        '-g',str(count),'-keyint_min',str(count),'-sc_threshold','0','-bf','0',
        '-x264-params','ipratio=1:aq-mode=0:mbtree=0:colorprim=bt709:transfer=bt709:colormatrix=bt709',
        '-movflags','+faststart+write_colr',str(temporary)]
    errors=tempfile.TemporaryFile()
    proc=subprocess.Popen(command,stdin=subprocess.PIPE,stderr=errors)
    light_samples=[]
    try:
        for i in range(count):
            image=frames.frame(i/count)
            light_samples.append(sample_sources(image,frames.base))
            proc.stdin.write(image.tobytes())
        proc.stdin.close()
        rc=proc.wait()
        errors.seek(0)
        error=errors.read().decode('utf-8',errors='replace')
        if rc!=0:
            raise RuntimeError('Tavern encoding failed: '+error)
        temporary.replace(target)
    except BaseException:
        if proc.poll() is None:
            proc.kill()
        proc.wait()
        temporary.unlink(missing_ok=True)
        raise
    finally:
        if not proc.stdin.closed: proc.stdin.close()
        errors.close()
    decoded=subprocess.run([ffmpeg,'-hide_banner','-loglevel','error','-i',str(target),
        '-frames:v','1','-f','rawvideo','-pix_fmt','bgr24','pipe:1'],capture_output=True,check=True).stdout
    first=np.frombuffer(decoded,np.uint8).reshape(h,w,3)
    first=first[:recipe['background']['texture_size'][1],:recipe['background']['texture_size'][0]]
    cv2.imwrite(str(scene/recipe['background']['file']),first)
    write_table(scene,recipe,light_samples)
    config=json.dumps({key:value for key,value in recipe.items() if key!='files'},sort_keys=True,ensure_ascii=False).encode('utf-8')
    return {'config_sha256':hashlib.sha256(config).hexdigest(),
            'source_sha256':hashlib.sha256((scene/recipe['background']['source']).read_bytes()).hexdigest(),
            'mask_sha256':hashlib.sha256((scene/recipe['background']['mask']).read_bytes()).hexdigest(),
            'renderer_sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
            'cassette_renderer_sha256':hashlib.sha256(Path(__file__).with_name('tavern_cassette.py').read_bytes()).hexdigest(),
            'actor_light_renderer_sha256':hashlib.sha256(Path(__file__).with_name('tavern_actor_light.py').read_bytes()).hexdigest(),
            'command':command[1:-1], 'frames':count,
            'python':platform.python_version(),'opencv':cv2.__version__,'numpy':np.__version__,
            'ffmpeg':subprocess.run([ffmpeg,'-version'],capture_output=True,text=True,check=True).stdout.splitlines()[0],
            'periodic_pixels_equal':bool(np.array_equal(frames.frame(0),frames.frame(1)))}
