using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Mirrors the diagnostic-refresh step observed in signed Microsoft BTVS 1.14.0:
// Full Packet Logging -> control interface 0850302a... -> IOCTL 0x411008,
// 4-byte input 0x1000, no output. Does not enable pairing/debug-key modes.
internal static class LoggingRefresh
{
    [StructLayout(LayoutKind.Sequential)] struct Interface { internal uint Size;internal Guid Class;internal uint Flags;internal IntPtr Reserved; }
    static Guid Control=new Guid("0850302a-b344-4fda-9be9-90576b8d46f0");
    static int Main(){try{Refresh();Console.WriteLine("Bluetooth diagnostic configuration refresh completed.");return 0;}catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}}
    internal static void Refresh()
    {
        IntPtr set=SetupDiGetClassDevs(ref Control,null,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1))throw new Win32Exception();
        try{
            var entry=new Interface{Size=(uint)Marshal.SizeOf(typeof(Interface))};
            if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref Control,0,ref entry))throw new Win32Exception();
            var extra=new Interface{Size=entry.Size};
            if(SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref Control,1,ref extra))throw new Exception("Multiple diagnostic interfaces; no guessed target.");
            if(Marshal.GetLastWin32Error()!=259)throw new Win32Exception();
            uint needed;SetupDiGetDeviceInterfaceDetail(set,ref entry,IntPtr.Zero,0,out needed,IntPtr.Zero);
            if(needed<8 || needed>65536)throw new Exception("Invalid interface path length.");
            IntPtr detail=Marshal.AllocHGlobal((int)needed);
            try{Marshal.WriteInt32(detail,IntPtr.Size==8?8:6);
                if(!SetupDiGetDeviceInterfaceDetail(set,ref entry,detail,needed,out needed,IntPtr.Zero))throw new Win32Exception();
                string path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                using(var file=CreateFile(path,0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero)){
                    if(file.IsInvalid)throw new Win32Exception();uint refresh=0x1000,written;
                    if(!DeviceIoControl(file,0x411008,ref refresh,4,IntPtr.Zero,0,out written,IntPtr.Zero))throw new Win32Exception();
                }
            }finally{Marshal.FreeHGlobal(detail);}
        }finally{SetupDiDestroyDeviceInfoList(set);}
    }
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g,string enumerator,IntPtr parent,uint flags);
    [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set,IntPtr device,ref Guid g,uint index,ref Interface data);
    [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set,ref Interface data,IntPtr detail,uint size,out uint required,IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr sec,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool DeviceIoControl(SafeFileHandle file,uint code,ref uint input,uint inputSize,IntPtr output,uint outputSize,out uint returned,IntPtr ov);
}
