using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AutomacoesCivil3D
{
    public class PluviometroTools
    {
        /// <summary>
        /// Cria o bocal superior do pluviômetro (anel + funil + transição cilíndrica),
        /// centrado em (x,y), com topo do anel em zTopo.
        /// Depende do método UnirSolidos(Database, Transaction, ObjectId, ObjectId) já existente.
        /// </summary>
        public ObjectId CriarBocalPluviometro(
            Database db,
            Transaction tr,
            BlockTableRecord ms,
            double x,
            double y,
            double zTopo,
            double diamTopoExt,
            double diamColuna,
            double altAnel,
            double altFunil,
            double altCilInferior)
        {
            double raioTopoExt = diamTopoExt * 0.5;
            double raioColuna = diamColuna * 0.5;

            // ========== CILINDRO SUPERIOR (ANEL) ==========
            Solid3d solAnel = new Solid3d();
            solAnel.SetDatabaseDefaults();
            solAnel.CreateFrustum(altAnel, raioColuna/2, raioColuna/2, raioColuna / 2);

            // base do anel em zTopo - altAnel
            Matrix3d movAnel =
                Matrix3d.Displacement(new Vector3d(x, y, zTopo + altAnel));
            solAnel.TransformBy(movAnel);

            ObjectId anelId = ms.AppendEntity(solAnel);
            tr.AddNewlyCreatedDBObject(solAnel, true);

            // ========== FUNIL (TRONCO DE CONE) ==========
            // topo do funil (raio menor) em zTopo - altAnel
            // base do funil (raio maior) em zTopo - altAnel - altFunil
            Solid3d solFunil = new Solid3d();
            solFunil.SetDatabaseDefaults();
            // height, radiusTop, radiusBottom
            solFunil.CreateFrustum(altFunil, raioColuna, raioColuna, raioTopoExt);

            Matrix3d movFunil =
                Matrix3d.Displacement(
                    new Vector3d(x, y, zTopo + altAnel + altFunil));
            solFunil.TransformBy(movFunil);

            ObjectId funilId = ms.AppendEntity(solFunil);
            tr.AddNewlyCreatedDBObject(solFunil, true);

            // ========== CILINDRO INFERIOR (TRANSIÇÃO PARA TUBO) ==========
            Solid3d solCilInf = new Solid3d();
            solCilInf.SetDatabaseDefaults();
            solCilInf.CreateFrustum(altCilInferior, raioColuna, raioColuna, raioColuna);

            // topo do cilindro em zTopo - altAnel - altFunil
            Matrix3d movCilInf =
                Matrix3d.Displacement(
                    new Vector3d(x, y, zTopo - altAnel));
            solCilInf.TransformBy(movCilInf);

            ObjectId cilInfId = ms.AppendEntity(solCilInf);
            tr.AddNewlyCreatedDBObject(solCilInf, true);

            // ========== UNIR OS SÓLIDOS ==========
            ObjectId parcialId = TiltimetroTools.UnirSolidos(db, tr, anelId, funilId);
            ObjectId bocalId = TiltimetroTools.UnirSolidos(db, tr, parcialId, cilInfId);

            return bocalId;
        }
    }
}
