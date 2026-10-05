// Read-only: open documents (also collects the result of a snippet that timed out).
var sb = new System.Text.StringBuilder();
sb.AppendLine("Revit " + document.Application.VersionNumber + " " + document.Application.VersionBuild + " | active: " + document.Title);
foreach (Document d in document.Application.Documents) { try { sb.AppendLine("  " + d.Title + " linked " + d.IsLinked + " workshared " + d.IsWorkshared); } catch { } }
return sb.ToString();
