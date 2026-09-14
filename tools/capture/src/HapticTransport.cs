using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// One-shot Triton output report experiment, based on SDL controller_structs.h.
// No controller settings/feature reports, no lizard-mode commands, no OS input.
internal static class HapticTransport
{
    internal static void Send(byte[] template, string expectedPath = null)
    {
            if(template.Length<2 || (template[0]!=0x81 && template[0]!=0x82 && template[0]!=0x83) || template[1]!=(template[0]==0x81?0:1))throw new Exception("Only report-specific right trackpad output is allowed");
            var devices=new List<Device>();
            foreach(Device d in Devices.Enumerate(false).Values)
                if(d.Vid==0x28de && d.Pid==0x1303 && d.Page==0xff00 && d.Usage==1)devices.Add(d);
            if(devices.Count!=1)throw new Exception("Expected exactly one SC2 BLE FF00:1 collection, found "+devices.Count);
            if(expectedPath!=null && !string.Equals(devices[0].Path,expectedPath,StringComparison.OrdinalIgnoreCase))throw new Exception("Controller changed during channel verification");
            // Inspect uses no read/write access. Actual output uses shared write only.
            using(var file=CreateFile(devices[0].Path,0x40000000u,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero)) {
                if(file.IsInvalid)throw new Win32Exception();
                IntPtr data; if(!HidD_GetPreparsedData(file,out data))throw new Win32Exception();
                int length; bool supported=false;
                try {
                    IntPtr caps=Marshal.AllocHGlobal(64);
                    try {
                        if(HidP_GetCaps(data,caps)!=0x110000)throw new Exception("GetCaps failed");
                        length=(ushort)Marshal.ReadInt16(caps,6);
                        Console.WriteLine("OutputReportByteLength="+length);
                        for(int kind=0;kind<2;kind++) {
                            ushort count=(ushort)Marshal.ReadInt16(caps,kind==0?54:52);
                            if(count==0)continue;
                            IntPtr entries=Marshal.AllocHGlobal(count*72);
                            try {
                                int status=kind==0?HidP_GetValueCaps(1,entries,ref count,data):HidP_GetButtonCaps(1,entries,ref count,data);
                                if(status!=0x110000)throw new Exception("Output caps failed");
                                for(int i=0;i<count;i++) { byte id=Marshal.ReadByte(entries,i*72+2); Console.WriteLine("OutputReportID=0x"+id.ToString("X2")); if(id==template[0])supported=true; }
                            } finally { Marshal.FreeHGlobal(entries); }
                        }
                    } finally { Marshal.FreeHGlobal(caps); }
                } finally { HidD_FreePreparsedData(data); }
                if(!supported || length<template.Length || length>128)throw new Exception("设备未声明支持该波形；未发送。");
                byte[] bytes=new byte[length]; Array.Copy(template,bytes,template.Length);
                WriteOnce(file,bytes);
            }
    }
    static void WriteOnce(SafeFileHandle file,byte[] bytes) {
        IntPtr buffer=Marshal.AllocHGlobal(bytes.Length), ov=Marshal.AllocHGlobal(32), signal=CreateEvent(IntPtr.Zero,true,false,null);
        bool pending=false;
        try {
            if(signal==IntPtr.Zero)throw new Win32Exception();
            Marshal.Copy(bytes,0,buffer,bytes.Length); for(int i=0;i<32;i++)Marshal.WriteByte(ov,i,0); Marshal.WriteIntPtr(ov,24,signal);
            bool immediate=WriteFile(file,buffer,(uint)bytes.Length,IntPtr.Zero,ov); int error=immediate?0:Marshal.GetLastWin32Error();
            if(!immediate && error!=997)throw new Win32Exception(error);
            pending=true;
            if(WaitForSingleObject(signal,1000)!=0)throw new Exception("Output timed out; canceling, no retry");
            uint written; bool ok=GetOverlappedResult(file,ov,out written,false); pending=false;
            if(!ok)throw new Win32Exception(); if(written!=bytes.Length)throw new Exception("Short output report");
        } finally {
            if(pending){ CancelIoEx(file,ov); uint ignored; GetOverlappedResult(file,ov,out ignored,true); }
            if(signal!=IntPtr.Zero)CloseHandle(signal); Marshal.FreeHGlobal(ov); Marshal.FreeHGlobal(buffer);
        }
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sec,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] static extern bool HidD_GetPreparsedData(SafeFileHandle file,out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data,IntPtr caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type,IntPtr caps,ref ushort length,IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type,IntPtr caps,ref ushort length,IntPtr data);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteFile(SafeFileHandle file,IntPtr buffer,uint length,IntPtr written,IntPtr ov);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle file,IntPtr ov,out uint written,bool wait);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle file,IntPtr ov);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateEvent(IntPtr sec,bool manual,bool initial,string name);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint time);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
