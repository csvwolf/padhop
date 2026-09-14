using System;

// Fixed 32-byte mouse-only protocol; no arbitrary keys, commands, or executable payloads.
internal static class MouseWire
{
    internal const int Size = 32;
    internal static byte[] Pack(int kind, int x, int y, long window, uint pid)
    {
        byte[] b = new byte[Size];
        Array.Copy(BitConverter.GetBytes(kind), 0, b, 0, 4);
        Array.Copy(BitConverter.GetBytes(x), 0, b, 4, 4);
        Array.Copy(BitConverter.GetBytes(y), 0, b, 8, 4);
        Array.Copy(BitConverter.GetBytes(Environment.TickCount), 0, b, 12, 4);
        Array.Copy(BitConverter.GetBytes(window), 0, b, 16, 8);
        Array.Copy(BitConverter.GetBytes(pid), 0, b, 24, 4);
        Array.Copy(BitConverter.GetBytes(1), 0, b, 28, 4);
        return b;
    }
    internal static bool Valid(byte[] b)
    {
        if (b == null || b.Length != Size || BitConverter.ToInt32(b, 28) != 1) return false;
        int k = BitConverter.ToInt32(b, 0), x = BitConverter.ToInt32(b, 4), y = BitConverter.ToInt32(b, 8);
        if (k < 0 || k > 7) return false;
        if (k == 1) return x >= -2048 && x <= 2048 && y >= -2048 && y <= 2048;
        if (k == 2) return x >= -4080 && x <= 4080 && x % 120 == 0 && y == 0;
        return x == 0 && y == 0;
    }
    internal static bool Fresh(byte[] b, int tick) { return unchecked((uint)(tick - BitConverter.ToInt32(b, 12))) <= 250; }
}
