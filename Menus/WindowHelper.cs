using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;
using System.Diagnostics;
using System.Drawing;


namespace XaiNet2.Menus
{
    public class WindowHelper
    {

        public static void ApplyBlurEffect(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND
                    //GradientColor = (38 << 24) | 0x222222
                };
                //accent.GradientColor = Color.FromArgb(30, 255, 255, 0).ToArgb();
                Debug.WriteLine($"GradientColor: {accent.GradientColor}");
                int size = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error applying blur effect: {ex.Message}");
            }
        }

        public static void ApplyBlurToAllWindows(Application app)
        {
            foreach (Window window in app.Windows)
            {
                ApplyBlurEffect(window);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    }
}
