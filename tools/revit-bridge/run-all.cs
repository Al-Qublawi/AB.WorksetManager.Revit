// Loads dev build v<N> from bytes and runs DevHarness.RunAll on the background local test copy
// (ABWM_Arch_local, made by make-test-central.cs). Writes ...\ABWorksetDev\run-all.txt too.
string v = "4";
string dev = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA"), @"Temp\ABWorksetDev");
System.Reflection.Assembly core = null, addin = null;
foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) { var n = a.GetName(); if (n.Version.ToString() == "1.0.0." + v) { if (n.Name == "AB.WorksetManager.Core") core = a; if (n.Name == "AB.WorksetManager.Addin") addin = a; } }
if (core == null) core = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(System.IO.Path.Combine(dev, "v" + v, "AB.WorksetManager.Core.dll")));
var c = core;
System.AppDomain.CurrentDomain.AssemblyResolve += (s, e) => e.Name.StartsWith("AB.WorksetManager.Core, Version=1.0.0." + v) ? c : null;
if (addin == null) addin = System.Reflection.Assembly.Load(System.IO.File.ReadAllBytes(System.IO.Path.Combine(dev, "v" + v, "AB.WorksetManager.Addin.dll")));
Document L = null; foreach (Document x in document.Application.Documents) if (x.Title == "ABWM_Arch_local") L = x;
if (L == null) return "Open the test copy first (make-test-central.cs).";
string result = (string)addin.GetType("AB.WorksetManager.Addin.Diagnostics.DevHarness").GetMethod("RunAll").Invoke(null, new object[] { L });
System.IO.File.WriteAllText(System.IO.Path.Combine(dev, "run-all.txt"), result + "\nEND");
return result;
