using Autodesk.AutoCAD.DatabaseServices;

namespace AutomacoesCivil3D
{
    public class ExclusaoObjetos
    {
        public void ApagarSolid3d(ObjectIdCollection ids, Transaction tr)
        {
            if (tr == null || ids == null || ids.Count == 0) { return; }

            foreach (ObjectId id in ids)
            {
                if (!id.IsNull && id.IsValid)
                {
                    Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite, false);
                    if (ent != null && !ent.IsErased) { ent.Erase(); }
                }
            }
        }
    }
}
