using System;
using System.Collections.Generic;
using System.Linq;
public sealed class StickProfile {
 public string Action{get;set;} public int Deadzone{get;set;} public double Speed{get;set;} public Dictionary<string,BindingAction> Directions{get;set;}
 public StickProfile(){Action="none";Deadzone=20;Speed=800;Directions=new Dictionary<string,BindingAction>();string[] dirs={"UP","DOWN","LEFT","RIGHT"},keys={"W","S","A","D"};for(int i=0;i<4;i++)Directions[dirs[i]]=new BindingAction{Kind="keyboard",Chord=keys[i]};}
 public void Validate(){if(!new[]{"none","leftStick","rightStick","wasd","arrows","directions","mouse"}.Contains(Action) || Deadzone<0 || Deadzone>80 || double.IsNaN(Speed) || Speed<50 || Speed>3000)throw new Exception("摇杆动作或参数无效。");if(Directions==null || Directions.Count!=4)throw new Exception("摇杆方向配置缺失。");foreach(string d in new[]{"UP","DOWN","LEFT","RIGHT"}){if(!Directions.ContainsKey(d)||Directions[d]==null)throw new Exception("摇杆方向配置缺失。");Directions[d].Validate();}}
 public Dictionary<string,BindingAction> Bindings(){if(Action=="directions")return Directions;var b=new Dictionary<string,BindingAction>();if(Action=="wasd" || Action=="arrows"){string[] dirs={"UP","DOWN","LEFT","RIGHT"},keys=Action=="wasd"?new[]{"W","S","A","D"}:new[]{"Up","Down","Left","Right"};for(int i=0;i<4;i++)b[dirs[i]]=new BindingAction{Kind="keyboard",Chord=keys[i]};}return b;}
}
public sealed partial class PadBook {
 public bool VirtualOutputEnabled{get;set;}
 public int InputLayoutVersion{get;set;} public StickProfile LeftStick{get;set;} public StickProfile RightStick{get;set;}
 public void EnsureInputLayout(){if(InputLayoutVersion==1)return;if(InputLayoutVersion!=0)throw new Exception("不支持的输入配置版本。");if(Bindings==null){Bindings=new Dictionary<string,BindingAction>();if(KeyboardButton!="none")Bindings[KeyboardButton]=new BindingAction{Kind="osk"};}if(GamepadEnabled)foreach(string key in InputCatalog.Gamepad){BindingAction a;if(!Bindings.TryGetValue(key,out a) || (a!=null && a.Kind=="none"))Bindings[key]=new BindingAction{Kind="gamepad",Chord=key};}VirtualOutputEnabled=GamepadEnabled;LeftStick=new StickProfile{Action=LeftStickWasd?"wasd":GamepadEnabled?"leftStick":"none",Deadzone=LeftStickWasd?24:0};RightStick=new StickProfile{Action=GamepadEnabled?"rightStick":"none",Deadzone=0};InputLayoutVersion=1;}
 public bool NeedsVirtualGamepad{get{EnsureInputLayout();return new[]{LeftStick.Action,RightStick.Action,Left.Action,Right.Action}.Any(x=>x=="leftStick"||x=="rightStick") || EffectiveBindings().Values.Any(a=>a.Kind=="gamepad" || (a.Kind=="macro" && a.Steps.Any(s=>s.Kind=="gamepad")));}}
}
