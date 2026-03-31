using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutomacoesCivil3D
{
    public class SolidosCorredores
    {
        [CommandMethod("ExportarSolidosCorredores")]
        public void ExportarSolidosCorredores()
        {
            Editor editor = Manager.DocEditor;

            try
            {
                ExportacaoSolidosCorredoresService service = new ExportacaoSolidosCorredoresService();
                ExportacaoSolidosCorredoresDialogData dialogData = service.BuildDialogData();

                if (dialogData.Corridors.Count == 0)
                {
                    AcadApp.ShowAlertDialog(dialogData.BlockingIssue);
                    return;
                }

                ExportacaoSolidosCorredoresDialogViewModel viewModel = new ExportacaoSolidosCorredoresDialogViewModel(dialogData);
                ExportacaoSolidosCorredoresWindow window = new ExportacaoSolidosCorredoresWindow(viewModel);

                bool? confirmed = AutoCadWpfDialogHost.ShowModal(window);
                if (confirmed != true)
                {
                    editor.WriteMessage("\nExportação cancelada pelo usuário.");
                    return;
                }

                ExportacaoSolidosCorredoresResult result = service.Execute(window.BuildRequest());
                AcadApp.ShowAlertDialog(result.BuildSummary());
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                editor.WriteMessage($"\nErro AutoCAD: {ex.Message}");
                AcadApp.ShowAlertDialog("Erro AutoCAD na exportação de sólidos:\n" + ex.Message);
            }
            catch (System.Exception ex)
            {
                editor.WriteMessage($"\nErro geral: {ex.Message}");
                AcadApp.ShowAlertDialog("Erro na exportação de sólidos:\n" + ex.Message);
            }
        }

        internal static class IfcApplyGuard
        {
            [ThreadStatic] internal static bool Busy;
        }
    }

    internal static class AutoCadWpfDialogHost
    {
        public static bool? ShowModal(Window window)
        {
            if (window == null)
            {
                return false;
            }

            IntPtr owner = Process.GetCurrentProcess().MainWindowHandle;
            if (owner != IntPtr.Zero)
            {
                new WindowInteropHelper(window).Owner = owner;
            }

            MethodInfo[] methods = typeof(AcadApp)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => string.Equals(m.Name, "ShowModalWindow", StringComparison.Ordinal))
                .ToArray();

            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();

                try
                {
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(window.GetType()))
                    {
                        return ConvertReturnValue(method.Invoke(null, new object[] { window }));
                    }

                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(IntPtr) &&
                        parameters[1].ParameterType.IsAssignableFrom(window.GetType()))
                    {
                        return ConvertReturnValue(method.Invoke(null, new object[] { owner, window }));
                    }

                    if (parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(IntPtr) &&
                        parameters[1].ParameterType.IsAssignableFrom(window.GetType()) &&
                        parameters[2].ParameterType == typeof(bool))
                    {
                        return ConvertReturnValue(method.Invoke(null, new object[] { owner, window, true }));
                    }
                }
                catch
                {
                }
            }

            return window.ShowDialog();
        }

        private static bool? ConvertReturnValue(object? value)
        {
            if (value is null)
            {
                return null;
            }

            if (value is bool boolValue)
            {
                return boolValue;
            }

            if (value is System.Windows.Forms.DialogResult dialogResult)
            {
                return dialogResult == System.Windows.Forms.DialogResult.OK;
            }

            return windowResultFallback(value);
        }

        private static bool? windowResultFallback(object value)
        {
            string text = value.ToString() ?? string.Empty;
            if (text.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (text.Equals("Cancel", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("False", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }
    }
}
