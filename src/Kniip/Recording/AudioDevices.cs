using System;
using System.Runtime.InteropServices;

namespace Kniip.Recording;

/// <summary>
/// Standaard audioapparaten via Core Audio. ScreenRecorderLib crasht (niet afvangbaar) als er bijvoorbeeld
/// geen afspeelapparaat is, dus kniip zoekt de apparaten zelf op en geeft alleen bestaande door.
/// </summary>
internal static class AudioDevices
{
    private const int ERender = 0;
    private const int ECapture = 1;
    private const int EConsole = 0;

    public static string? DefaultPlaybackId => GetDefaultId(ERender);

    public static string? DefaultMicrophoneId => GetDefaultId(ECapture);

    private static string? GetDefaultId(int dataFlow)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(dataFlow, EConsole, out IMMDevice? device) != 0 || device == null)
                {
                    return null;
                }

                try
                {
                    return device.GetId(out string? id) == 0 ? id : null;
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints();

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate();

        void OpenPropertyStore();

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
    }
}
