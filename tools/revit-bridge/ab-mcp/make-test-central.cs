// Builds a fresh test set from the running Revit's Snowdon Towers Architectural sample (links unloaded):
// enables worksharing on a COPY, adds test worksets, saves it as a central model in
// %LOCALAPPDATA%\Temp\ABWorksetDev\central, then opens a new local copy in the background.
// The user's open model is never touched. Takes ~2-3 min. Send with "transaction": "none"; past
// waitSeconds it answers with a jobId - collect the result with revit_get_job_result.
var app = uiApp.Application;
var sb = new System.Text.StringBuilder();
try {
for (int pass = 0; pass < 5; pass++) { Document hit = null; foreach (Document d0 in app.Documents) { try { if (!d0.IsLinked && (d0.Title.StartsWith("ABWM_") || d0.Title == "Snowdon Towers Sample Architectural")) { hit = d0; break; } } catch { } } if (hit == null) break; sb.AppendLine("closing " + hit.Title); hit.Close(false); }
string dev = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("LOCALAPPDATA"), @"Temp\ABWorksetDev");
string centralDir = System.IO.Path.Combine(dev, "central"); string localDir = System.IO.Path.Combine(dev, "local"); string work = System.IO.Path.Combine(dev, "work");
foreach (var dir in new[] { centralDir, localDir, work }) { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); System.IO.Directory.CreateDirectory(dir); }
string copy = System.IO.Path.Combine(work, "ABWM_Arch.rvt");
System.IO.File.Copy(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), @"Samples\Snowdon Towers Sample Architectural.rvt"), copy);
System.IO.File.SetAttributes(copy, System.IO.FileAttributes.Normal);
var d = app.OpenDocumentFile(copy);
d.EnableWorksharing("Shared Levels and Grids", "Workset1");
using (var t = new Transaction(d, "setup")) {
  t.Start();
  var ids = new System.Collections.Generic.Dictionary<string, WorksetId>();
  foreach (var n in new[] { "ARC - Walls", "ARC - Doors", "Z-Link-RVT-Old", "Old Furniture", "To Delete A" }) ids[n] = Workset.Create(d, n).Id;
  System.Action<BuiltInCategory, System.Func<int, string>> put = (bic, pick) => { int k = 0; foreach (Element e in new FilteredElementCollector(d).OfCategory(bic).WhereElementIsNotElementType()) { var p = e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM); if (p != null && !p.IsReadOnly) p.Set(ids[pick(k++)].IntegerValue); } };
  put(BuiltInCategory.OST_Walls, k => "ARC - Walls"); put(BuiltInCategory.OST_Doors, k => "ARC - Doors"); put(BuiltInCategory.OST_Furniture, k => k % 2 == 0 ? "Old Furniture" : "To Delete A");
  sb.AppendLine("setup " + t.Commit());
}
var so = new SaveAsOptions(); so.OverwriteExistingFile = true; var wso = new WorksharingSaveAsOptions(); wso.SaveAsCentral = true; so.SetWorksharingOptions(wso);
string central = System.IO.Path.Combine(centralDir, "ABWM_Arch_central.rvt");
d.SaveAs(central, so); d.Close(false);
string local = System.IO.Path.Combine(localDir, "ABWM_Arch_local.rvt");
WorksharingUtils.CreateNewLocal(ModelPathUtils.ConvertUserVisiblePathToModelPath(central), ModelPathUtils.ConvertUserVisiblePathToModelPath(local));
var lo = new OpenOptions(); lo.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
var L = app.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(local), lo);
WorksharingUtils.RelinquishOwnership(L, new RelinquishOptions(true), new TransactWithCentralOptions());
sb.AppendLine("local open: " + L.Title + " (nothing owned)");
} catch (System.Exception ex) { sb.AppendLine("FAILED: " + ex.GetType().Name + ": " + ex.Message); }
return sb.ToString();
