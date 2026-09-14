using System;
using System.Collections.Generic;
using System.Linq;

// Single-threaded scheduler; no sleeps, background injection, or catch-up bursts.
internal sealed class BindingEngine {
 readonly Dictionary<string,BindingAction> bindings;
 readonly Dictionary<string,Job> jobs=new Dictionary<string,Job>();
 readonly Dictionary<string,int> refs=new Dictionary<string,int>();
 bool seeded;ulong previous;
 sealed class Job{internal BindingAction Action;internal int Step,Cycle;internal double Due;internal bool Holding;internal List<string> Held=new List<string>();}
 internal BindingEngine(PadBook b){bindings=b.EffectiveBindings().Where(e=>!(InputCatalog.Mask(e.Key)!=0 && e.Value.Kind=="gamepad" && e.Value.Chord==e.Key)).ToDictionary(e=>e.Key,e=>e.Value);}
 internal List<string> Step(ulong bits,double now){var a=new List<string>();if(!seeded){seeded=true;previous=bits;return a;}foreach(var entry in bindings){ulong mask=InputCatalog.SourceMask(entry.Key);bool down=(bits&mask)!=0,was=(previous&mask)!=0;Job j;bool exists=jobs.TryGetValue(entry.Key,out j);if(!down && was && exists && (j.Action.Kind!="macro" || j.Action.WhileHeld)){Release(j,a);jobs.Remove(entry.Key);}if(down && !was && !jobs.ContainsKey(entry.Key)){var act=entry.Value;if(act.Kind=="osk"){a.Add("ToggleKeyboard");continue;}if(act.Kind=="none")continue;j=new Job{Action=act,Due=now};jobs.Add(entry.Key,j);if(act.Kind!="macro"){Hold(j,act.Kind,act.Chord,a);j.Holding=true;}}}previous=bits;a.AddRange(Tick(now));return a;}
 internal List<string> Tick(double now){var a=new List<string>();foreach(var entry in jobs.ToArray()){var j=entry.Value;if(j.Action.Kind!="macro" || now<j.Due)continue;var step=j.Action.Steps[j.Step];if(!j.Holding){Hold(j,step.Kind,step.Chord,a);j.Holding=true;j.Due=now+step.HoldMs;}else{Release(j,a);j.Holding=false;j.Step++;if(j.Step>=j.Action.Steps.Count){j.Step=0;j.Cycle++;if(!j.Action.WhileHeld && j.Cycle>=j.Action.Repeat){jobs.Remove(entry.Key);continue;}}j.Due=now+step.WaitMs;}}return a;}
 void Hold(Job j,string kind,string chord,List<string> a){if(kind=="keyboard"){foreach(int key in InputCatalog.Parse(chord))Acquire(j,"Key:"+key,a);}else if(kind=="gamepad")Acquire(j,"Game:"+chord,a);else if(chord=="wheelUp" || chord=="wheelDown")a.Add("Wheel delta="+(chord=="wheelUp"?120:-120));else Acquire(j,"Mouse:"+chord,a);}
 void Acquire(Job j,string key,List<string> a){int count;refs.TryGetValue(key,out count);refs[key]=count+1;j.Held.Add(key);if(count==0)a.Add(Event(key,true));}
 void Release(Job j,List<string> a){for(int i=j.Held.Count-1;i>=0;i--){string k=j.Held[i];int n=refs[k]-1;if(n==0){refs.Remove(k);a.Add(Event(k,false));}else refs[k]=n;}j.Held.Clear();}
 static string Event(string key,bool down){if(key.StartsWith("Game:"))return (down?"GameDown button=":"GameUp button=")+key.Substring(5);if(key.StartsWith("Key:"))return (down?"KeyDown vk=":"KeyUp vk=")+key.Substring(4);string b=key.Substring(6);return b=="left"?(down?"LeftDown":"LeftUp"):b=="right"?(down?"RightDown":"RightUp"):(down?"MouseDown button=":"MouseUp button=")+(b=="middle"?3:b=="x1"?4:5);}
 internal List<string> Reset(){var a=new List<string>();foreach(var j in jobs.Values)Release(j,a);jobs.Clear();refs.Clear();seeded=false;previous=0;return a;}
 internal static void Test(){var b=new PadBook{Bindings=new Dictionary<string,BindingAction>{{"L4",new BindingAction{Kind="keyboard",Chord="Win+D"}},{"R4",new BindingAction{Kind="keyboard",Chord="Win+E"}}}};var e=new BindingEngine(b);uint l=InputCatalog.Mask("L4"),r=InputCatalog.Mask("R4");if(e.Step(l,0).Count!=0)throw new Exception("Held on entry");e.Step(0,1);var a=e.Step(l,2);if(a.Count!=2 || a[0]!="KeyDown vk=91" || a[1]!="KeyDown vk=68")throw new Exception("Windows chord ordering");if(e.Step(l|r,3).Contains("KeyDown vk=91"))throw new Exception("Shared modifier pressed twice");if(e.Step(r,4).Contains("KeyUp vk=91"))throw new Exception("Shared modifier released early");if(!e.Reset().Contains("KeyUp vk=91"))throw new Exception("Reset missed Windows key");
 b.Bindings["L4"]=new BindingAction{Kind="macro",Repeat=2,Steps=new List<MacroStroke>{new MacroStroke{Chord="A",HoldMs=20,WaitMs=20}}};e=new BindingEngine(b);e.Step(0,0);if(!e.Step(l,1).Contains("KeyDown vk=65"))throw new Exception("Macro start");if(e.Tick(20).Count!=0 || !e.Tick(21).Contains("KeyUp vk=65") || !e.Tick(41).Contains("KeyDown vk=65") || !e.Tick(61).Contains("KeyUp vk=65") || e.Tick(1000).Count!=0)throw new Exception("Macro timing/repeats");b.Bindings["L4"].WhileHeld=true;e=new BindingEngine(b);e.Step(0,0);e.Step(l,1);if(!e.Step(0,2).Contains("KeyUp vk=65") || e.Tick(1000).Count!=0)throw new Exception("Held repeat cancellation");Probe.Say("BINDINGS PASS: held-entry, Win chord ordering, shared modifiers, timed macro, repeat and cancellation.");}
}

internal sealed class MouseOwnership {
 readonly bool[,] down=new bool[2,2];
 internal List<string> Mix(List<string> actions,int source){var a=new List<string>();foreach(string s in actions){int b=s.StartsWith("Left")?0:s.StartsWith("Right")?1:-1;if(b<0 || (s!="LeftDown" && s!="LeftUp" && s!="RightDown" && s!="RightUp")){a.Add(s);continue;}bool before=down[0,b]||down[1,b];down[source,b]=s.EndsWith("Down");if(before!=(down[0,b]||down[1,b]))a.Add(s);}return a;}
 internal void Clear(){Array.Clear(down,0,down.Length);}
}
