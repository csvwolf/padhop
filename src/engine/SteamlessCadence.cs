using System;
// Adapted from SteamlessController ControllerManager.cpp (MIT; see THIRD-PARTY-NOTICES).
// Upstream 250 Hz frame windows are expressed as milliseconds for Bluetooth report cadence.
internal sealed class SteamlessCadence {
 bool touching,held,raw,seeded;int px,py;double travel;long edgeAt,touchAt,releaseAt,lastTick,idleAt;
 internal void Reset(){touching=held=raw=seeded=false;travel=0;edgeAt=touchAt=releaseAt=lastTick=idleAt=-100000;}
 internal SteamlessCadence(){Reset();}
 internal int Step(bool touch,bool click,int x,int y,int area,long now,int interval,double spacing,int touchGrace=48,int releaseGrace=180,int pressureLimit=900){
  if(!seeded){seeded=true;raw=click;held=click;edgeAt=now;}
  if(click!=raw){raw=click;edgeAt=now;}
  int result=0;
  if(!held && click && now-edgeAt>=8){held=true;result=2;}
  else if(held && !click && now-edgeAt>=16){held=false;releaseAt=now;result=3;}
  if(touch && !touching){px=x;py=y;travel=0;touchAt=idleAt=now;}
  if(result!=0){travel=0;px=x;py=y;lastTick=now;touching=touch;return result;}
  if(!touch){travel=0;touching=false;return 0;}
  if(touching && !click && !held && now-touchAt>=touchGrace){
   double dx=x-px,dy=y-py,dist=Math.Sqrt(dx*dx+dy*dy);
   if(now-releaseAt<releaseGrace || area>pressureLimit || dist>4000){px=x;py=y;}
   else if(dist>=45){idleAt=now;travel+=dist;if(travel>=spacing){if(now-lastTick>=interval){result=1;lastTick=now;travel=0;}else travel=spacing;}px=x;py=y;}
   else if(now-idleAt>=120){travel=0;px=x;py=y;}
  }
  touching=touch;return result;
 }
 internal static void Test(){var c=new SteamlessCadence();for(int i=0;i<10;i++)if(c.Step(true,false,i*100,0,100,i*5,50,6500)!=0)throw new Exception("Touch grace failed");c.Reset();c.Step(true,false,0,0,100,0,50,6500);if(c.Step(true,false,10000,0,100,60,50,6500)!=0)throw new Exception("Teleport emitted haptic");if(c.Step(true,false,11000,0,2000,70,50,6500)!=0)throw new Exception("Pressure motion emitted haptic");c.Step(true,true,11000,0,2000,80,50,6500);if(c.Step(true,true,11000,0,2000,88,50,6500)!=2)throw new Exception("Press feedback missing");c.Step(true,false,11000,0,100,100,50,6500);if(c.Step(true,false,11000,0,100,116,50,6500)!=3)throw new Exception("Release feedback missing");if(c.Step(true,false,14000,0,100,160,50,1)!=0)throw new Exception("Release grace failed");c.Reset();c.Step(true,false,0,0,100,0,50,6500);int ticks=0;for(int i=1;i<=30;i++)if(c.Step(true,false,i*500,0,100,50+i*8,50,6500)==1)ticks++;if(ticks!=2)throw new Exception("Movement cadence mismatch: "+ticks);Probe.Say("STEAMLESS HAPTICS PASS: touch/pressure/jump gates, press/release, settling and distance cadence.");}
}
