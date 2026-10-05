using System;
using AB.WorksetManager.Addin.Infrastructure;
using AB.WorksetManager.Addin.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AB.WorksetManager.Addin.Commands
{
    /// <summary>
    /// Opens the Worksets Manager window - modal, so the model cannot change underneath it and every
    /// change it makes runs in Revit's normal command context. "Pick in model" hides the window, lets
    /// the user pick, and shows the same window again with everything as it was.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public sealed class ShowManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication uiapp = commandData.Application;
                UIDocument uidoc = uiapp.ActiveUIDocument;
                Document doc = uidoc?.Document;

                string problem = DocumentGuard.ProblemForUse(doc);
                if (problem != null)
                {
                    var dialog = new TaskDialog(App.ProductName)
                    {
                        MainInstruction = doc != null && !doc.IsWorkshared ? "This model has no worksets" : "Worksets Manager cannot open here",
                        MainContent = problem,
                        CommonButtons = TaskDialogCommonButtons.Close
                    };
                    dialog.Show();
                    return Result.Succeeded;
                }

                var session = new ManagerSession(uiapp, uidoc);
                var window = new ManagerWindow(session);
                try { new System.Windows.Interop.WindowInteropHelper(window).Owner = uiapp.MainWindowHandle; }
                catch (Exception ex) { Log.Warn("Window owner not set: " + ex.Message); }

                while (true)
                {
                    window.ShowDialog();
                    Action pick = session.TakePendingPick();
                    if (pick == null) break;
                    try { pick(); }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { /* Esc - keep what was there */ }
                    catch (Exception ex)
                    {
                        Log.Error("Picking failed.", ex);
                        session.AddMessage("Picking in the model failed: " + ex.Message);
                    }
                    window.AfterPick();
                }
                window.SaveSettings();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Log.Error("Worksets Manager failed.", ex);
                TaskDialog.Show(App.ProductName,
                    "Something went wrong and the window was closed. The model was not changed by the failed step." +
                    Environment.NewLine + Environment.NewLine + ex.Message +
                    Environment.NewLine + Environment.NewLine + "Details are in the log: " + Log.Folder);
                return Result.Succeeded;
            }
        }
    }

    /// <summary>Button available in project documents only (not in families or with nothing open).</summary>
    public sealed class ProjectAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                Document doc = applicationData?.ActiveUIDocument?.Document;
                return doc != null && !doc.IsFamilyDocument;
            }
            catch { return false; }
        }
    }
}
