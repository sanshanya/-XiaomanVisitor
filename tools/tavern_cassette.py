"""Author-kit v1.1 cassette motion, rendered before the single final encode.

Only the local cassette rectangle is written. The screen restoration is cached
once, and the original synthetic two-bar rhythm and reel glints are retained.
This is decorative motion, not music analysis or gameplay interaction.
"""
import math

import cv2
import numpy as np


class CassetteRhythm:
    BOUNDS = (924, 417, 1093, 500)
    SCREEN = (1017, 438, 1084, 460)

    def __init__(self, base):
        # Exactly the original kit's small display-only inpaint; retain casing,
        # buttons, reels and power indicator. No generative redraw is involved.
        crop=base[431:464,1009:1088]
        mask=np.zeros(crop.shape[:2],np.uint8)
        mask[438-431:460-431,1017-1009:1084-1009]=255
        clean=cv2.inpaint(crop,mask,5,cv2.INPAINT_TELEA)
        self.display=clean[438-431:460-431,1017-1009:1084-1009].copy()
        self.colors=[]
        left=np.array([238,229,50],np.float32)
        middle=np.array([156,246,72],np.float32)
        right=np.array([53,182,243],np.float32)
        for i in range(11):
            u=i/10
            color=left*(1-u/.6)+middle*(u/.6) if u<.6 else middle*(1-(u-.6)/.4)+right*((u-.6)/.4)
            self.colors.append(color)
        yy,xx=np.mgrid[427:467,1009:1090]
        self.halo=np.exp(-.5*(((xx-1048)/35)**2+((yy-447)/17)**2))
        self.glint=np.array([[.05,.18,.05],[.18,.37,.18],[.05,.18,.05]],np.float32)[...,None]

    @staticmethod
    def energy(phase):
        def pulse(center,width):
            distance=((phase-center+.5)%1)-.5
            return math.exp(-.5*(distance/width)**2)
        kick=sum(pulse(i/8+.013,.016) for i in range(8))
        snare=sum(pulse(i/8+.013,.019) for i in (1,3,5,7))
        hats=sum(pulse(i/16+.017,.010) for i in range(16))
        energy=[]
        for i in range(11):
            low,high=1-i/10,i/10
            phrase=.5+.5*math.sin(2*math.pi*(2*phase+i*.17+.07))
            counter=.5+.5*math.sin(2*math.pi*(4*phase+i*.31+.23))
            side=.5+.5*math.sin(2*math.pi*(3*phase+i*.2+.43))
            value=.13+.28*phrase+.18*counter+.11*side+(.5*low+.23)*kick+(.2+.3*high)*snare+(.1+.17*high)*hats
            energy.append(float(np.clip(value,.08,1)))
        return energy,min(1,kick),min(1,hats)

    def apply(self, frame, phase):
        phase%=1
        x0,y0,x1,y1=self.BOUNDS
        patch=frame[y0:y1,x0:x1].copy()
        sx0,sy0,sx1,sy1=self.SCREEN
        patch[sy0-y0:sy1-y0,sx0-x0:sx1-x0]=self.display
        energy,kick,hats=self.energy(phase)
        for i,value in enumerate(energy):
            x=1019+6*i-x0
            segments=min(6,max(1,int(round(value*5.5))))
            color=self.colors[i]*(.84+.10*kick)
            for level in range(segments):
                bottom=458-level*3-y0
                shade=np.clip(color*(1-.045*level),0,255).astype(np.uint8)
                cv2.rectangle(patch,(x,bottom-1),(x+3,bottom),shade.tolist(),-1,cv2.LINE_8)
            if segments<2:
                patch[458-y0,x:x+4]=np.clip(patch[458-y0,x:x+4].astype(np.float32)*.45+color*.22,0,255).astype(np.uint8)
        for i,(cx,cy) in enumerate(((958.5,453.1),(988,453.1))):
            angle=2*math.pi*(6*phase+(.12 if i==0 else .60))
            px=int(round(cx+6*math.cos(angle)))-x0
            py=int(round(cy+6*math.sin(angle)))-y0
            spot=patch[py-1:py+2,px-1:px+2].astype(np.float32)
            spot=spot*(1-self.glint)+np.array([195,187,237],np.float32)*self.glint
            patch[py-1:py+2,px-1:px+2]=np.clip(spot,0,255).astype(np.uint8)
        halo=self.halo*(1.8+2*kick+.6*hats)
        light=patch[427-y0:467-y0,1009-x0:1090-x0].astype(np.float32)
        light+=halo[...,None]*np.array([.85,.68,.18],np.float32)
        patch[427-y0:467-y0,1009-x0:1090-x0]=np.clip(light,0,255).astype(np.uint8)
        frame[y0:y1,x0:x1]=patch
        return frame
