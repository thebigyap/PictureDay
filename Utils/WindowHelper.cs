using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PictureDay.Utils
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO
        {
            [MarshalAs(UnmanagedType.U4)]
            public uint cbSize;
            [MarshalAs(UnmanagedType.U4)]
            public uint dwTime;
        }

        public static uint GetLastInputTime()
        {
            LASTINPUTINFO lastInputInfo = new LASTINPUTINFO();
            lastInputInfo.cbSize = (uint)Marshal.SizeOf(lastInputInfo);
            GetLastInputInfo(ref lastInputInfo);
            return lastInputInfo.dwTime;
        }

        public static string? GetWindowTitle(IntPtr hWnd)
        {
            StringBuilder title = new StringBuilder(256);
            if (GetWindowText(hWnd, title, title.Capacity) > 0)
            {
                return title.ToString();
            }
            return null;
        }

        public static string? GetProcessNameFromWindow(IntPtr hWnd)
        {
            if (GetWindowThreadProcessId(hWnd, out uint processId) != 0)
            {
                try
                {
                    Process process = Process.GetProcessById((int)processId);
                    return process.ProcessName;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }
}

