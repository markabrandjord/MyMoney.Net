using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Walkabout.UITests.Basics
{
    /// <summary>
    /// Finds top-level windows for a process via raw Win32 EnumWindows, bypassing UIA's own
    /// desktop-children enumeration entirely. Use this when
    /// automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(...)) comes back empty for
    /// a window a screenshot confirms is genuinely open on screen - confirmed in this repo
    /// writing AttachmentsFlaUiTests.cs: AttachmentDialog (a non-modal, owned Window.Show(), not
    /// ShowDialog()) never appeared via desktop enumeration (with or without a ProcessId
    /// filter), with or without a Name filter, despite a screenshot proving it was genuinely
    /// rendered on screen with the seeded attachment visible in it. See the flaui-wpf-testing
    /// skill's references/native-dialogs.md for the full sourced writeup (FlaUI#57/#239) - this
    /// is a documented, known-flaky UIA path, not specific to native common dialogs.
    /// </summary>
    internal static class Win32WindowFallback
    {
        private delegate bool EnumDelegate(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumDelegate lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static List<Window> FindTopLevelWindowsForProcess(UIA3Automation automation, int processId)
        {
            var result = new List<Window>();
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true; // keep enumerating

                GetWindowThreadProcessId(hWnd, out uint foundPid);
                if (foundPid != (uint)processId) return true;

                try
                {
                    var native = automation.NativeAutomation.ElementFromHandle(hWnd);
                    if (native != null)
                    {
                        result.Add(automation.WrapNativeElement(native).AsWindow());
                    }
                }
                catch
                {
                    // A handle Win32 sees but UIA can't wrap for some reason - skip it rather
                    // than fail the whole enumeration.
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }
    }
}
