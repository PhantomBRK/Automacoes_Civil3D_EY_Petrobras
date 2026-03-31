using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Microsoft.Office.Interop.Excel;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;


namespace AutomacoesCivil3D
{
    public class Tutoriais
    {


        [CommandMethod("TutorialDre")]
        public static void TutorialDRE()
        {
            
            string csvPathPavimentacao = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Tutorial - Quantitativos Drenagem.pdf");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = csvPathPavimentacao,
                UseShellExecute = true // Importante para usar o programa padrão
            };

            Process.Start(startInfo);
        }


        [CommandMethod("TutorialPav")]
        public static void TutorialPav()
        {

            string csvPathPavimentacao = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Tutorial - Quantitativos Pavimentação.pdf");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = csvPathPavimentacao,
                UseShellExecute = true // Importante para usar o programa padrão
            };

            Process.Start(startInfo);
        }

        [CommandMethod("TutorialTrp")]
        public static void TutorialTrp()
        {

            string csvPathPavimentacao = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "Tutorial - Quantitativos Terraplenagem.pdf");
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = csvPathPavimentacao,
                UseShellExecute = true // Importante para usar o programa padrão
            };

            Process.Start(startInfo);
        }
    }
}
