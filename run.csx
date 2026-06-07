using System;
using System.Reflection;
var assembly = Assembly.LoadFrom(@"C:\Users\wirum\Documents\CosmicBaiter\bin\Release\CosmicBaiter.dll");
var type = assembly.GetType("TestClass");
var instance = Activator.CreateInstance(type);
type.GetMethod("Test").Invoke(instance, null);
