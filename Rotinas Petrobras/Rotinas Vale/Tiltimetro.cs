using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Color = Autodesk.AutoCAD.Colors.Color;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace AutomacoesCivil3D
{
    public class TiltimetroTools
    {
        [CommandMethod("CRIA_TILT")]
        public static void ComandoCriarTiltimetro()
        {
            Document doc = Manager.DocCad;
            Editor ed = Manager.DocEditor;
            Database db = doc.Database;

            PromptPointOptions ppo = new PromptPointOptions("\nPonto base do tiltímetro:");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            double x = ppr.Value.X;
            double y = ppr.Value.Y;
            double zTerreno = ppr.Value.Z;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms =
                    (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                ObjectId tiltId = CriarTiltimetro3D(db, tr, ms, x, y, zTerreno);

                tr.Commit();
            }
        }

        public static ObjectId CriarTiltimetro3D(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double zTerreno)
        {
            // Dimensões padrão (ajuste conforme seu padrão)
            double baseL = 0.40;
            double baseW = 0.40;
            double baseH = 0.10;

            double pedL = 0.25;
            double pedW = 0.25;
            double pedH = 0.45;

            double hMastro = 0.80;
            double dMastro = 0.06;

            double caixaL = 0.24;
            double caixaW = 0.12;
            double caixaH = 0.10;
            double caixaAltBase = zTerreno + 0.8; // base da caixa

            double prismaL = 0.10;
            double prismaW = 0.10;
            double prismaH = 0.10;

            // ===== BASE DE CONCRETO =====
            Solid3d solBase = new Solid3d();
            solBase.SetDatabaseDefaults();
            solBase.CreateBox(baseL, baseW, baseH);
            Matrix3d movBase = Matrix3d.Displacement(
                new Vector3d(x , y , zTerreno));
            solBase.TransformBy(movBase);
            ObjectId baseId = ms.AppendEntity(solBase);
            tr.AddNewlyCreatedDBObject(solBase, true);


            // ===== PEDESTAL SUPERIOR =====
            Solid3d solPed = new Solid3d();
            solPed.SetDatabaseDefaults();
            solPed.CreateBox(pedL, pedW, pedH);
            Matrix3d movPed = Matrix3d.Displacement(
                new Vector3d(
                    x,
                    y,
                    zTerreno));
            solPed.TransformBy(movPed);
            ObjectId pedId = ms.AppendEntity(solPed);
            tr.AddNewlyCreatedDBObject(solPed, true);



            // Unir base + pedestal
            ObjectId corpoBaseId = UnirSolidos(db, tr, baseId, pedId);

            // ===== MASTRO =====
            Solid3d solMastro = new Solid3d();
            solMastro.SetDatabaseDefaults();
            solMastro.CreateFrustum(hMastro, dMastro * 0.5, dMastro * 0.5, dMastro * 0.5);
            Matrix3d movMastro = Matrix3d.Displacement(
                new Vector3d(x, y, zTerreno + pedH));
            solMastro.TransformBy(movMastro);
            ObjectId mastroId = ms.AppendEntity(solMastro);
            tr.AddNewlyCreatedDBObject(solMastro, true);

            // Unir mastro ao conjunto base
            ObjectId corpoTiltId = UnirSolidos(db, tr, corpoBaseId, mastroId);

            // ===== CAIXA DO SENSOR =====
            Solid3d solCaixa = new Solid3d();
            solCaixa.SetDatabaseDefaults();
            solCaixa.CreateBox(caixaL, caixaW, caixaH);

            double caixaX = x + (caixaL * 0.5);
            double caixaY = y;
            double caixaZ = caixaAltBase;

            Matrix3d movCaixa = Matrix3d.Displacement(
                new Vector3d(caixaX, caixaY, caixaZ));
            solCaixa.TransformBy(movCaixa);

            ObjectId caixaId = ms.AppendEntity(solCaixa);
            tr.AddNewlyCreatedDBObject(solCaixa, true);

           

            //Unir caixa ao corpo (se quiser tudo sólido único)
            corpoTiltId = UnirSolidos(db, tr, corpoTiltId, caixaId);

            // ===== PRISMA / ALVO (SEPARADO) =====
            Solid3d solPrisma = new Solid3d();
            solPrisma.SetDatabaseDefaults();
            solPrisma.CreateBox(prismaL, prismaW, prismaH);

            double prismaX = x - (prismaL * 0.5);
            double prismaY = y;
            double prismaZ = zTerreno + hMastro - prismaH;

            Matrix3d movPrisma = Matrix3d.Displacement(
                new Vector3d(prismaX, prismaY, prismaZ));
            solPrisma.TransformBy(movPrisma);

            ObjectId prismaId = ms.AppendEntity(solPrisma);
            tr.AddNewlyCreatedDBObject(solPrisma, true);


            //Unir caixa ao corpo (se quiser tudo sólido único)
            corpoTiltId = UnirSolidos(db, tr, corpoTiltId, prismaId);

            // Prisma fica em sólido separado para leitura; se quiser unir, chame UnirSolidos.

            return corpoTiltId;
        }

        public static ObjectId UnirSolidos(
            Database db,
            Transaction tr,
            ObjectId primeiroId,
            ObjectId segundoId)
        {
            if (primeiroId.IsNull || segundoId.IsNull) return ObjectId.Null;

            Solid3d sol1 = (Solid3d)tr.GetObject(primeiroId, OpenMode.ForWrite);
            Solid3d sol2 = (Solid3d)tr.GetObject(segundoId, OpenMode.ForWrite);

            sol1.BooleanOperation(BooleanOperationType.BoolUnite, sol2);
            sol2.Erase();

            return sol1.ObjectId;
        }


        public static ObjectId Cortar(
            Database db,
            Transaction tr,
            ObjectId primeiroId,
            ObjectId segundoId)
        {
            if (primeiroId.IsNull || segundoId.IsNull) return ObjectId.Null;

            Solid3d sol1 = (Solid3d)tr.GetObject(primeiroId, OpenMode.ForWrite);
            Solid3d sol2 = (Solid3d)tr.GetObject(segundoId, OpenMode.ForWrite);

            sol1.BooleanOperation(BooleanOperationType.BoolSubtract, sol2);
            sol2.Erase();

            return sol1.ObjectId;
        }
    }
}
