using System;
using System.Reflection;

class Program {
    static void Main() {
        string dllPath = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\Principia\ksp_plugin_adapter.dll";
        string kspData = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed";

        AppDomain.CurrentDomain.AssemblyResolve += (s, args) => {
            string name = new AssemblyName(args.Name).Name + ".dll";
            string p = System.IO.Path.Combine(kspData, name);
            if (System.IO.File.Exists(p)) return Assembly.LoadFile(p);
            return null;
        };

        Assembly asm = Assembly.LoadFile(dllPath);
        foreach (Type t in asm.GetTypes()) {
            if (t.IsEnum && (t.Name.Contains("Frame") || t.Name.Contains("Navigation"))) {
                Console.WriteLine("Enum: " + t.FullName);
                foreach (var name in Enum.GetNames(t)) {
                    Console.WriteLine("  " + name + " = " + Convert.ToInt32(Enum.Parse(t, name)));
                }
            }
        }
    }
}
