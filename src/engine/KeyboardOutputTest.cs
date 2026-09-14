using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
internal static class KeyboardOutputTest {
 internal static void Run(){using(var form=new Form{Text=L.T("SC2 键盘输出验证"),Width=420,Height=150,KeyPreview=true,TopMost=true}){int down=0,up=0;form.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.A)down++;};form.KeyUp+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.A)up++;};form.Show();form.Activate();Application.DoEvents();string path=Process.GetCurrentProcess().MainModule.FileName;string cfg=Path.Combine(Path.GetTempPath(),"sc2-key-test-"+Guid.NewGuid().ToString("N")+".ini");File.WriteAllText(cfg,"global=False\nsteamDesktopMuted=True\nallow="+path+"\n");try{using(var c=new BridgeClient(new string[0],false,cfg)){c.Send("KeyDown vk=65",form.Handle,(uint)Process.GetCurrentProcess().Id);Pump();c.Send("KeyUp vk=65",form.Handle,(uint)Process.GetCurrentProcess().Id);Pump();c.Send("Reset",form.Handle,(uint)Process.GetCurrentProcess().Id);if(down<1 || up<1)throw new Exception("Keyboard roundtrip failed (test window must remain foreground)");Probe.Say("KEYBOARD OUTPUT PASS: signed helper scan-code A down/up received in isolated test window.");}}finally{File.Delete(cfg);}form.Close();}}
 static void Pump(){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<150){Application.DoEvents();System.Threading.Thread.Sleep(5);}}
}
