using System;
internal sealed class ButtonEdge {
 bool seeded,down;
 internal void Reset(){seeded=down=false;}
 internal bool Step(uint bits,string key){uint mask=key=="L4"?1u<<17:key=="L5"?1u<<18:key=="R4"?1u<<7:key=="R5"?1u<<8:0;bool next=(bits&mask)!=0;bool fire=seeded && next && !down;seeded=true;down=next;return fire;}
 internal static void Test(){foreach(string key in new[]{"L4","L5","R4","R5"}){uint mask=key=="L4"?1u<<17:key=="L5"?1u<<18:key=="R4"?1u<<7:1u<<8;var e=new ButtonEdge();if(e.Step(mask,key)||e.Step(mask,key))throw new Exception("Held button activated on entry");e.Step(0,key);if(!e.Step(mask,key)||e.Step(mask,key))throw new Exception("Button must fire once per press");e.Reset();if(e.Step(mask,key))throw new Exception("Focus reset must suppress held button");e.Step(0,"none");if(e.Step(uint.MaxValue,"none"))throw new Exception("Unbound button fired");}}
}
