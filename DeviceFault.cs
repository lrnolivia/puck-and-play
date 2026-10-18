namespace SteamControllerGuard
{
    public static class DeviceFault
    {
        // These can reflect a transient failed start or a stopped device stack.
        // Disabled devices, missing/blocked drivers and reboot requirements are
        // deliberately excluded from automatic USB resets.
        public static bool CanRestart(uint code)
        {
            return code == 10 || code == 31 || code == 43;
        }

        public static string Advice(uint code)
        {
            switch (code)
            {
                case 0:
                    return "Windows reports that the receiver is ready.";
                case 10:
                case 31:
                case 43:
                    return "Automatic care can try a device restart, then one targeted USB port cycle.";
                case 14:
                case 21:
                case 38:
                case 44:
                case 53:
                    return "Restart Windows to finish the pending device change.";
                case 22:
                    return "The receiver is disabled in Device Manager. Enable it there when you want to use it.";
                case 18:
                case 19:
                case 28:
                case 37:
                case 39:
                case 40:
                    return "Windows needs a driver repair. Check Windows Update and reinstall Steam; this helper does not replace drivers.";
                case 32:
                case 48:
                case 52:
                    return "Windows has disabled or blocked the driver. Resolve the driver issue in Device Manager; this helper will not bypass it.";
                case 45:
                    return "Reconnect the receiver's USB cable.";
                case 47:
                    return "The receiver was prepared for safe removal. Unplug and reconnect its USB cable.";
                case 51:
                case 54:
                    return "Windows is waiting for another device or completing a reset. Let it finish before retrying.";
                case 12:
                    return "Windows could not allocate device resources. Try another USB port or restart Windows.";
                default:
                    return "Check the receiver in Device Manager. Reconnect its cable, try another data cable or port, and restart Windows if the fault remains.";
            }
        }
    }
}
