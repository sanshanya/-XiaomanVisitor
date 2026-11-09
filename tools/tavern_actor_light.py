"""Small RGB vertex-light table sampled from the same rendered room frames."""
import base64
import json

import numpy as np


def sample_sources(frame, base):
    weights=np.array([.114,.587,.299],np.float32)
    # Existing warm spot, cool ceiling strip, and pink counter neon.
    regions=((800,296,828,320),(890,24,1050,62),(780,604,900,622))
    return [float(np.mean((frame[y:y1,x:x1].astype(np.float32)-base[y:y1,x:x1])@weights))
            for x,y,x1,y1 in regions]


def write_table(scene, recipe, samples):
    cfg=recipe['character']['lighting']; signals=np.asarray(samples,np.float32)
    span=np.maximum(np.ptp(signals,axis=0),1.0)
    signals=np.clip(2*(signals-signals.min(axis=0))/span-1,-1,1)
    palette=np.array([cfg['warm_rgb'],cfg['cool_rgb'],cfg['neon_rgb']],np.float32)
    ambient=np.asarray(cfg['ambient_rgb'],np.float32)
    colors=[]
    for signal in signals:
        frame=[]
        for v in cfg['y']:
            for u in cfg['x']:
                face=float(np.exp(-((v-.62)/.17)**2-((u-.5)/.28)**2))
                base=ambient*(1-.6*face)+.985*(.6*face)
                spatial=np.array([.2+.8*v*v,(1-u)**2*(.3+.7*v),(1-v)**2],np.float32)
                diffuse=((spatial*(1+cfg['dynamic_strength']*signal))[:,None]*palette).sum(axis=0)
                gain=base+diffuse*(1-cfg['face_protection']*face)
                frame.append(np.rint(np.clip(gain/cfg['gain_scale'],0,1)*255).astype(np.uint8))
        colors.append(frame)
    raw=np.asarray(colors,np.uint8).tobytes()
    data=dict(schema=2,gain_scale=cfg['gain_scale'],x=cfg['x'],y=cfg['y'],frame_count=len(samples),fps=recipe['animation']['fps'],
              rgb=base64.b64encode(raw).decode('ascii'))
    (scene/cfg['file']).write_text(json.dumps(data,separators=(',',':'))+'\n',encoding='utf-8',newline='\n')
    return data


def apply_preview(portrait, data, frame=0):
    """Use the same two triangles per grid cell as the native UI mesh."""
    colors=np.frombuffer(base64.b64decode(data['rgb']),np.uint8).reshape(data['frame_count'],len(data['y']),len(data['x']),3)
    nodes=colors[frame%data['frame_count']].astype(np.float32)*(data.get('gain_scale',1)/255)
    array=np.array(portrait.convert('RGBA'),copy=True); height,width=array.shape[:2]
    yy,xx=np.mgrid[:height,:width]
    u=(xx+.5)/width; v=1-(yy+.5)/height
    xs=np.array(data['x']); ys=np.array(data['y'])
    col=np.clip(np.searchsorted(xs,u,side='right')-1,0,len(xs)-2)
    row=np.clip(np.searchsorted(ys,v,side='right')-1,0,len(ys)-2)
    a=((u-xs[col])/(xs[col+1]-xs[col]))[...,None]
    b=((v-ys[row])/(ys[row+1]-ys[row]))[...,None]
    upper=nodes[row,col]*(1-b)+nodes[row+1,col]*(b-a)+nodes[row+1,col+1]*a
    lower=nodes[row,col]*(1-a)+nodes[row+1,col+1]*b+nodes[row,col+1]*(a-b)
    gain=np.where(b>=a,upper,lower)
    array[:,:,:3]=np.clip(np.rint(array[:,:,:3]*gain),0,255).astype(np.uint8)
    from PIL import Image
    return Image.fromarray(array)
