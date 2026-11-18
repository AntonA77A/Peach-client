using System;
using Microsoft.Win32;

namespace Peach
{
    // Windows opens the Xbox Game Bar when you press the controller's Xbox button. Peach uses that button as
    // "Home" for controller mode, so this lets the user switch the Windows side off (Settings > Gaming >
    // Xbox Game Bar > "Open Xbox Game Bar using this button on a controller"). It only ever runs because the
    // user clicked the Peach menu item or answered "Ja" to its one-time question.
    internal static class GameBar
    {
        public const string DefaultKey = @"Software\Microsoft\GameBar";
        const string ValueName = "UseNexusForGameBarEnabled";

        // Missing value = Windows default = the Xbox button opens the Game Bar.
        public static bool ControllerButtonOpensGameBar(string keyPath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(keyPath ?? DefaultKey))
                {
                    if (k == null) return true;
                    object v = k.GetValue(ValueName);
                    return !(v is int) || (int)v != 0;
                }
            }
            catch { return true; }
        }

        public static bool SetControllerButtonOpensGameBar(bool enabled, string keyPath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(keyPath ?? DefaultKey))
                {
                    if (k == null) return false;
                    k.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
