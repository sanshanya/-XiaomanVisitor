"""Offline temporal checks for an explicitly rendered tavern loop (OpenCV)."""
import json
import re
import shutil
import subprocess
from pathlib import Path

import cv2
import numpy as np
from tavern_animation import AmbientFrames

ROOT=Path(__file__).resolve().parents[1]


def main():
    scene=ROOT/'assets/tavern-scene'
    recipe=json.loads((scene/'scene.json').read_text(encoding='utf-8'))
    renderer=AmbientFrames(scene,recipe)
    assert np.array_equal(renderer.frame(0),renderer.frame(1)), 'Raw animation must close exactly'
    # A moving rain layer must never paint mullions or unrelated room pixels.
    for phase in (0,.37):
        rain=renderer.rain(phase)
        assert not np.any(rain[renderer.alpha==0]), 'Rain escaped glass mask'
    first_raw=renderer.frame(0)
    other_raw=renderer.frame(.37)
    assert not np.array_equal(first_raw[438:460,1017:1084],other_raw[438:460,1017:1084]), 'Kit equalizer bars must animate'
    assert not np.array_equal(first_raw[444:462,949:969],other_raw[444:462,949:969]), 'Kit reel highlights must animate'
    for phase in (0,.37):
        ambient=renderer.frame(phase,cassette=False)
        difference=cv2.absdiff(renderer.frame(phase),ambient)
        difference[417:500,924:1093]=0
        assert not np.any(difference), 'Cassette animation changed unrelated room pixels'
    path=scene/recipe['animation']['file']
    cap=cv2.VideoCapture(str(path))
    assert cap.isOpened(), 'Video cannot be decoded'
    assert (int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)),int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)))==tuple(recipe['background']['encoded_size'])
    assert abs(cap.get(cv2.CAP_PROP_FPS)-recipe['animation']['fps'])<.001
    regions={'warm_lamp':(688,140,732,190),'ceiling':(890,24,1050,62),
             'counter':(650,547,1000,568),'floor':(900,810,1400,930),'dark_wall':(730,420,790,470)}
    luminance={key:[] for key in regions}
    first=previous=last=None; steps=[]; count=0
    try:
        while True:
            ok,frame=cap.read()
            if not ok: break
            if first is None: first=frame.copy()
            if previous is not None:
                steps.append(float(np.abs(frame.astype(np.int16)-previous.astype(np.int16)).mean()))
            if count%8==0:
                for key,(x0,y0,x1,y1) in regions.items():
                    roi=frame[y0:y1,x0:x1].astype(np.float32)
                    luminance[key].append(roi@np.array([.114,.587,.299],np.float32))
            previous=last=frame; count+=1
    finally: cap.release()
    assert count==recipe['animation']['fps']*recipe['animation']['seconds']
    seam=float(np.abs(last.astype(np.int16)-first.astype(np.int16)).mean())
    # Account for the intended moving frame at the seam separately from codec
    # error; stronger lighting legitimately changes adjacent frames more.
    raw_step=float(np.abs(renderer.frame((count-1)/count).astype(np.int16)-first_raw.astype(np.int16)).mean())
    codec_excess=max(0,seam-raw_step)
    luma_weights=np.array([.114,.587,.299],np.float32)
    seam_luma=float(np.abs((last.astype(np.float32)-first.astype(np.float32))@luma_weights).mean())
    raw_luma=float(np.abs((renderer.frame((count-1)/count).astype(np.float32)-first_raw.astype(np.float32))@luma_weights).mean())
    # Brightness discontinuity is the flicker check; keep RGB/chroma differences
    # in the report too, rather than treating chroma quantization as exposure.
    luma_excess=max(0,seam_luma-raw_luma)
    assert luma_excess<.5 and seam<3*max(steps), f'Encoded seam needs review: {seam}, luminance excess {luma_excess}'
    # Average each pixel's temporal range, rather than averaging the entire
    # region first: moving bright/dark reflection bands cancel in a region mean.
    light_ranges={key:float(np.ptp(np.stack(values),axis=0).mean()) for key,values in luminance.items()}
    assert light_ranges['warm_lamp']>=12 and light_ranges['ceiling']>=8, 'Lamp animation is too faint after encoding'
    assert light_ranges['counter']>=5 and light_ranges['floor']>=4, 'Counter/floor reflections must visibly change'
    assert light_ranges['dark_wall']<2, 'Unlit wall is flashing with the lights'
    trace=subprocess.run([shutil.which('ffmpeg'),'-hide_banner','-loglevel','verbose','-i',str(path),
        '-map','0:v:0','-c:v','copy','-bsf:v','trace_headers','-frames:v','1','-f','null','-'],
        capture_output=True,text=True,check=True).stderr
    for field in ('colour_primaries','transfer_characteristics','matrix_coefficients'):
        assert re.search(r'\b'+field+r'\s+[01]+\s+=\s+1\b',trace), 'Missing BT.709 video metadata: '+field
    result=dict(frames=count,bytes=path.stat().st_size,raw_loop_equal=True,rain_confined=True,
        cassette_animated=True,cassette_local_only=True,seam_mean_channel_delta=seam,adjacent_median_delta=float(np.median(steps)),
        adjacent_max_delta=max(steps),raw_seam_delta=raw_step,codec_seam_excess=codec_excess,
        codec_seam_luma_excess=luma_excess,light_luma_ranges=light_ranges,color_standard='bt709',gameplay_tested=False)
    out=ROOT/'.dev/reports/tavern-animation.json'; out.parent.mkdir(parents=True,exist_ok=True)
    out.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(result,indent=2))


if __name__=='__main__': main()
