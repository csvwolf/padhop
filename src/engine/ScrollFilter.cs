using System;
// Track the furthest accepted position, not each noisy report. Opposite travel
// must cross a small hysteresis band before reversing; never replay the band.
internal sealed class ScrollFilter {
 bool seeded;int edge,direction;double pending;
 internal void Reset(){seeded=false;direction=0;pending=0;}
 internal int Step(int y,bool active,PadProfile p){
  if(!active){Reset();return 0;}
  if(!seeded){seeded=true;edge=y;return 0;}
  double delta=y-edge,band=p.ScrollReversalPercent*655.34;
  if(delta==0)return 0;
  int sign=Math.Sign(delta);
  if(direction!=0 && sign!=direction){
   if(Math.Abs(delta)<=band)return 0;
   delta-=sign*band;pending=0;
  }
  direction=sign;edge=y;
  pending+=delta/p.ScrollUnits*(p.FineScroll?120:1);
  int limit=p.FineScroll?4080:34;
  int amount=Math.Max(-limit,Math.Min(limit,(int)pending));
  pending-=Math.Truncate(pending); // discard oversized excess rather than queue it
  return (p.ReverseScroll?-1:1)*amount*(p.FineScroll?1:120);
 }
}
