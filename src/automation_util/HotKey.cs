using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace game_automation_in_csharp.util
{
    public class HotKey
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static void register(DependencyObject control, int id, uint modifiers, uint vk)
        {
            Window parentWindow = Window.GetWindow(control);
            if (parentWindow != null) 
                RegisterHotKey(new WindowInteropHelper(parentWindow).Handle, id, modifiers, vk);
        }

        public static void unregister(DependencyObject control, int id)
        {
            Window parentWindow = Window.GetWindow(control);
            if (parentWindow != null)
                UnregisterHotKey(new WindowInteropHelper(parentWindow).Handle, id);
        }
    }
}
