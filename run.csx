using System;
using System.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;

Type t = typeof(WKSMissionModule.MissionState);
foreach(var prop in t.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
    Console.WriteLine($"Field: $(.Name) - $(.FieldType.Name)");
}
foreach(var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
    Console.WriteLine($"Property: $(.Name) - $(.PropertyType.Name)");
}
