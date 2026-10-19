using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Windows;
using Microsoft.Win32;

namespace Switch2Bridge {
    // A one-time registration for the EXISTING Microsoft WinUSB driver.
    // Does not replace drivers, change HID/audio or install a kernel component.
    internal static class UsbSetup {
        internal const string InterfaceGuid = "{6F13725E-EF0E-4FD3-AE5F-B2DE989EC825}";
        internal const string DeviceKey = @"SYSTEM\CurrentControlSet\Enum\USB\VID_057E&PID_2069&MI_01";
        internal static bool RegistrationMissing() {
            using(var root=Registry.LocalMachine.OpenSubKey(DeviceKey)) {
                if(root==null)return false;
                foreach(string name in root.GetSubKeyNames()) using(var device=root.OpenSubKey(name)) {
                    if(!string.Equals(device.GetValue("Service") as string,"WINUSB",StringComparison.OrdinalIgnoreCase))continue;
                    using(var parameters=device.OpenSubKey("Device Parameters")) {
                        if(parameters==null)return true;
                        var values=parameters.GetValue("DeviceInterfaceGUIDs") as string[];
                        var single=parameters.GetValue("DeviceInterfaceGUID") as string;
                        if((values==null || values.Length==0) && string.IsNullOrEmpty(single))return true;
                    }
                }
            }
            return false;
        }
        internal static int Configure() {
            try {
                if(!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))throw new InvalidOperationException("A configuração precisa da autorização de administrador do Windows.");
                int changes=0;
                using(var root=Registry.LocalMachine.OpenSubKey(DeviceKey)) {
                    if(root==null)throw new InvalidOperationException("Conecte o Pro Controller do Switch 2 por USB antes de configurar.");
                    foreach(string name in root.GetSubKeyNames()) using(var device=root.OpenSubKey(name,true)) {
                        if(!string.Equals(device.GetValue("Service") as string,"WINUSB",StringComparison.OrdinalIgnoreCase))continue;
                        using(var parameters=device.OpenSubKey("Device Parameters",true)) {
                            if(parameters==null)throw new InvalidOperationException("Parâmetros do WinUSB indisponíveis.");
                            var values=new List<string>((parameters.GetValue("DeviceInterfaceGUIDs") as string[]) ?? new string[0]);
                            string single=parameters.GetValue("DeviceInterfaceGUID") as string;
                            if(!string.IsNullOrWhiteSpace(single) && !values.Contains(single,StringComparer.OrdinalIgnoreCase)) values.Add(single);
                            if(!values.Contains(InterfaceGuid,StringComparer.OrdinalIgnoreCase)) {
                                values.Add(InterfaceGuid);
                                parameters.SetValue("DeviceInterfaceGUIDs",values.ToArray(),RegistryValueKind.MultiString);changes++;
                            }
                        }
                    }
                }
                MessageBox.Show(changes>0?"Interface USB registrada. Desconecte e reconecte o cabo USB do controle para o Windows publicar a interface. Depois clique em Reconectar USB no Switch2Bridge.":"A interface já estava registrada. Desconecte e reconecte o cabo USB do controle.","Switch2Bridge · configuração USB",MessageBoxButton.OK,MessageBoxImage.Information);
                return 0;
            }catch(Exception e) { MessageBox.Show(e.Message,"Switch2Bridge · configuração USB",MessageBoxButton.OK,MessageBoxImage.Error);return 1; }
        }
    }
}
