using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Document = Autodesk.AutoCAD.ApplicationServices.Document; // Add this line to resolve the conflict

namespace CriaProfiles
{
    public static class Manager
    {

        public static Document DocCad => Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument;
        public static Editor DocEditor
        {
            get
            {
                return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Editor;

            }

        }

        public static Database DocData
        {
            get
            {
                return Autodesk.AutoCAD.ApplicationServices.Core.Application.DocumentManager.MdiActiveDocument.Database;
            }
        }

        public static CivilDocument DocCivil
        {
            get
            {
                return CivilApplication.ActiveDocument;

            }
        }


    }


}
