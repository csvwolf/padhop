using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

internal static class ChannelCalibration
{
    [DllImport("kernel32.dll")] static extern void GetSystemTimePreciseAsFileTime(out long time);
    static decimal Now(){long value;GetSystemTimePreciseAsFileTime(out value);return (value-116444736000000000L)/10000000m;}
    internal static byte[] Packet(int group,int index)
    {
        if(index<0 || index>2 || group<0 || group>2)throw new ArgumentOutOfRangeException();
        if(group==0)return new byte[]{0x82,1,1,unchecked((byte)(-30+index))};
        if(group==1)return Waves.Tone(157+index*17,2,-30+index);
        return new byte[]{0x81,0,(byte)(60+index*10),0,44,1,1,0};
    }
    internal static void Run(string folder,string path,CancellationToken cancel)
    {
        var results=new List<Dictionary<string,object>>();
        try{
            if(cancel.WaitHandle.WaitOne(700))return;
            for(int group=0;group<3;group++)for(int index=0;index<3;index++){
                cancel.ThrowIfCancellationRequested();byte[] packet=Packet(group,index);
                var record=new Dictionary<string,object>{{"report_id",packet[0]}, {"payload_hex",BitConverter.ToString(packet,1).Replace("-","").ToLowerInvariant()},{"before",Now()},{"success",false}};
                try{HapticTransport.Send(packet,path);record["success"]=true;}
                catch(Exception e){record["error"]=e.Message;}
                finally{record["after"]=Now();results.Add(record);}
                if(cancel.WaitHandle.WaitOne(450))return;
            }
        }catch(OperationCanceledException){}
        finally{File.WriteAllText(Path.Combine(folder,"calibration.json"),new JavaScriptSerializer().Serialize(results));}
    }
    internal static void Test(){for(int g=0;g<3;g++)for(int i=0;i<3;i++){byte[] p=Packet(g,i);if(p[1]!=(g==2?0:1))throw new Exception("Calibration side");if(g==1 && p[5]!=2)throw new Exception("Calibration duration");if(g==2 && BitConverter.ToUInt16(p,6)!=1)throw new Exception("Calibration pulse count");}}
}
